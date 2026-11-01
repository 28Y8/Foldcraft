using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foldcraft.Core.Journal;

/// <summary>
/// The undo history: an append-only file that is never rewritten.
/// <para>
/// Each record is one line, <c>checksum TAB json LF</c>. A record counts only if its checksum
/// matches, so a write cut short by a crash is ignored and the records before it stay
/// readable. The next append starts on a fresh line, leaving the torn bytes in place.
/// </para>
/// <para>
/// Every append is flushed to disk before returning, which is what makes it safe to record
/// a change first and touch the folder second.
/// </para>
/// </summary>
public sealed class ChangeJournal
{
    private const byte NewLine = (byte)'\n';
    private const byte Separator = (byte)'\t';
    private const int ChecksumHexLength = 32;
    private const int MaxOpenAttempts = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    /// <summary>Raised after <see cref="QueuePreviewCheck"/> records something new to do.</summary>
    public event Action? PreviewCheckQueued;

    public ChangeJournal(string journalFile, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalFile);
        _path = Path.GetFullPath(journalFile);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Durably records a change. Call this before touching the folder: if it throws, the
    /// folder must stay untouched.
    /// </summary>
    public Guid RecordChange(ChangeRecord change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var id = Guid.NewGuid();

        Append(new Record
        {
            Kind = RecordKind.Change,
            Id = id,
            Timestamp = _time.GetUtcNow(),
            BatchId = change.BatchId,
            FolderPath = change.FolderPath,
            DesktopIniExisted = change.DesktopIniExisted,
            OriginalDesktopIni = change.OriginalDesktopIni,
            OriginalDesktopIniAttributes = change.OriginalDesktopIniAttributes,
            OriginalFolderAttributes = change.OriginalFolderAttributes,
            IconPath = change.IconPath,
            WrittenSha256 = change.WrittenSha256,
            FolderId = change.FolderId,
            OriginalFolderLastWriteUtc = change.OriginalFolderLastWriteUtc,
            OriginalDesktopIniLastWriteUtc = change.OriginalDesktopIniLastWriteUtc,
            SpecJson = change.SpecJson,
            IsRepair = change.IsRepair ? true : null,
            CreatedFolder = change.CreatedFolder ? true : null,
            CreatedFiles = change.CreatedFiles.Count > 0 ? change.CreatedFiles.ToArray() : null,
        });

        return id;
    }

    /// <summary>Records that a change finished.</summary>
    /// <param name="id">The change.</param>
    /// <param name="folderLastWriteUtc">The date it left the folder with, if known.</param>
    public void MarkApplied(Guid id, DateTime? folderLastWriteUtc = null) => Append(new Record
    {
        Kind = RecordKind.Applied,
        Id = id,
        Timestamp = _time.GetUtcNow(),
        AppliedFolderLastWriteUtc = folderLastWriteUtc,
    });

    public void MarkRolledBack(Guid id) => MarkState(RecordKind.RolledBack, id);

    public void MarkUndone(Guid id) => MarkState(RecordKind.Undone, id);

    public void MarkForgotten(Guid id) => MarkState(RecordKind.Forgotten, id);

    /// <summary>
    /// Records that a change's folder now lives somewhere else. The entry keeps its place and
    /// its original bytes, and from here on is read with the new location.
    /// </summary>
    public void MarkMoved(Guid id, string newFolderPath, string? folderId)
    {
        ArgumentException.ThrowIfNullOrEmpty(newFolderPath);

        Append(new Record
        {
            Kind = RecordKind.Moved,
            Id = id,
            Timestamp = _time.GetUtcNow(),
            FolderPath = newFolderPath,
            FolderId = folderId,
        });
    }

    /// <summary>Records the date an undo is about to leave, before it touches the folder.</summary>
    /// <param name="id">The oldest change the undo reverses.</param>
    /// <param name="plan">The date.</param>
    public void MarkUndoing(Guid id, UndoPlan plan) => Append(new Record
    {
        Kind = RecordKind.Undoing,
        Id = id,
        Timestamp = _time.GetUtcNow(),
        PlannedFolderLastWriteUtc = plan.FolderDate,
        PlannedMoveOn = plan.MoveOn ? true : null,
    });

    /// <summary>
    /// Records folders to look at again in a while. Being in the journal, it outlives this
    /// process, so whichever Foldcraft runs next can finish it.
    /// </summary>
    public void QueuePreviewCheck(IReadOnlyCollection<string> folders, int attempt = 1)
    {
        ArgumentNullException.ThrowIfNull(folders);

        Append(new Record
        {
            Kind = RecordKind.PreviewCheck,
            Id = Guid.NewGuid(),
            Timestamp = _time.GetUtcNow(),
            Folders = folders.ToArray(),
            Attempt = attempt,
        });

        PreviewCheckQueued?.Invoke();
    }

    public void MarkPreviewChecked(Guid id) => MarkState(RecordKind.PreviewChecked, id);

    /// <summary>Replays the journal. Unreadable records are counted and skipped, never fatal.</summary>
    public JournalSnapshot Read()
    {
        var bytes = ReadAllBytes();

        if (bytes.Length == 0)
        {
            return new JournalSnapshot([], 0);
        }

        var ordered = new List<Guid>();
        var entries = new Dictionary<Guid, JournalEntry>();
        var checks = new Dictionary<Guid, PreviewCheck>();
        var corrupt = 0;
        long sequence = 0;
        var start = 0;

        while (start < bytes.Length)
        {
            var end = Array.IndexOf(bytes, NewLine, start);
            var stop = end < 0 ? bytes.Length : end;
            var line = bytes.AsSpan(start, stop - start);
            start = stop + 1;

            if (line.IsEmpty)
            {
                continue;
            }

            var record = Decode(line);

            if (record is null)
            {
                corrupt++;
                continue;
            }

            sequence++;

            if (record.Kind == RecordKind.PreviewCheck)
            {
                checks[record.Id] = new PreviewCheck(record.Id, record.Timestamp, record.Folders ?? [], record.Attempt ?? 1);
            }
            else if (record.Kind == RecordKind.PreviewChecked)
            {
                checks.Remove(record.Id);
            }
            else if (record.Kind == RecordKind.Change)
            {
                var entry = ToEntry(record, sequence);

                if (entry is null || !entries.TryAdd(entry.Id, entry))
                {
                    corrupt++;
                }
                else
                {
                    ordered.Add(entry.Id);
                }
            }
            else if (entries.TryGetValue(record.Id, out var existing))
            {
                entries[record.Id] = record.Kind switch
                {
                    RecordKind.Moved => Moved(existing, record),
                    RecordKind.Undoing => Planned(existing, record),
                    _ => Settled(existing, record),
                };
            }
            else
            {
                corrupt++;
            }
        }

        return new JournalSnapshot(ordered.Select(id => entries[id]).ToArray(), corrupt)
        {
            PreviewChecks = checks.Values.ToArray(),
        };
    }

    private void MarkState(RecordKind kind, Guid id) => Append(new Record
    {
        Kind = kind,
        Id = id,
        Timestamp = _time.GetUtcNow(),
    });

    private static JournalEntry Moved(JournalEntry entry, Record record) => string.IsNullOrEmpty(record.FolderPath)
        ? entry
        : entry with
        {
            Change = entry.Change with { FolderPath = record.FolderPath, FolderId = record.FolderId ?? entry.Change.FolderId },
        };

    private static JournalEntry Planned(JournalEntry entry, Record record) => record.PlannedFolderLastWriteUtc is { } date
        ? entry with { UndoPlan = new UndoPlan(date, record.PlannedMoveOn == true) }
        : entry;

    private static JournalEntry Settled(JournalEntry entry, Record record) => entry with
    {
        State = ToState(record.Kind),
        Change = entry.Change with { AppliedFolderLastWriteUtc = record.AppliedFolderLastWriteUtc ?? entry.Change.AppliedFolderLastWriteUtc },
    };

    private static JournalEntryState ToState(RecordKind kind) => kind switch
    {
        RecordKind.Applied => JournalEntryState.Applied,
        RecordKind.RolledBack => JournalEntryState.RolledBack,
        RecordKind.Undone => JournalEntryState.Undone,
        RecordKind.Forgotten => JournalEntryState.Forgotten,
        _ => JournalEntryState.Pending,
    };

    private static JournalEntry? ToEntry(Record record, long sequence)
    {
        if (record.BatchId is not { } batchId ||
            string.IsNullOrEmpty(record.FolderPath) ||
            record.DesktopIniExisted is not { } existed ||
            record.OriginalFolderAttributes is not { } folderAttributes ||
            record.IconPath is null ||
            record.WrittenSha256 is null ||
            (existed && (record.OriginalDesktopIni is null || record.OriginalDesktopIniAttributes is null)))
        {
            return null;
        }

        return new JournalEntry
        {
            Id = record.Id,
            Sequence = sequence,
            Timestamp = record.Timestamp,
            State = JournalEntryState.Pending,
            Change = new ChangeRecord
            {
                BatchId = batchId,
                FolderPath = record.FolderPath,
                DesktopIniExisted = existed,
                OriginalDesktopIni = record.OriginalDesktopIni,
                OriginalDesktopIniAttributes = record.OriginalDesktopIniAttributes,
                OriginalFolderAttributes = folderAttributes,
                IconPath = record.IconPath,
                WrittenSha256 = record.WrittenSha256,
                FolderId = record.FolderId,
                OriginalFolderLastWriteUtc = record.OriginalFolderLastWriteUtc,
                OriginalDesktopIniLastWriteUtc = record.OriginalDesktopIniLastWriteUtc,
                SpecJson = record.SpecJson,
                IsRepair = record.IsRepair == true,
                CreatedFolder = record.CreatedFolder == true,
                CreatedFiles = record.CreatedFiles ?? [],
            },
        };
    }

    private static Record? Decode(ReadOnlySpan<byte> line)
    {
        if (line.Length <= ChecksumHexLength + 1 || line[ChecksumHexLength] != Separator)
        {
            return null;
        }

        var payload = line[(ChecksumHexLength + 1)..];

        if (!Encoding.ASCII.GetBytes(Checksum(payload)).AsSpan().SequenceEqual(line[..ChecksumHexLength]))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Record>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Checksum(ReadOnlySpan<byte> payload) =>
        Convert.ToHexStringLower(SHA256.HashData(payload).AsSpan(0, ChecksumHexLength / 2));

    private void Append(Record record)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        var checksum = Encoding.ASCII.GetBytes(Checksum(payload));
        var line = new byte[checksum.Length + 1 + payload.Length + 1];

        checksum.CopyTo(line, 0);
        line[checksum.Length] = Separator;
        payload.CopyTo(line, checksum.Length + 1);
        line[^1] = NewLine;

        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            using var stream = OpenForAppend();

            if (stream.Length > 0)
            {
                stream.Seek(-1, SeekOrigin.End);

                if (stream.ReadByte() != NewLine)
                {
                    // A torn record stays where it is, and this one must not merge into it.
                    stream.Seek(0, SeekOrigin.End);
                    stream.WriteByte(NewLine);
                }
            }

            stream.Seek(0, SeekOrigin.End);
            stream.Write(line);
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>Another Foldcraft process may hold the file for a moment, so retry briefly.</summary>
    private FileStream OpenForAppend()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            }
            catch (IOException) when (attempt < MaxOpenAttempts)
            {
                Thread.Sleep(25);
            }
        }
    }

    private byte[] ReadAllBytes()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
    }

    private enum RecordKind
    {
        Change,
        Applied,
        RolledBack,
        Undone,
        Forgotten,
        Moved,
        Undoing,
        PreviewCheck,
        PreviewChecked,
    }

    private sealed record Record
    {
        [JsonPropertyName("k")] public RecordKind Kind { get; init; }
        [JsonPropertyName("id")] public Guid Id { get; init; }
        [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; init; }
        [JsonPropertyName("batch")] public Guid? BatchId { get; init; }
        [JsonPropertyName("folder")] public string? FolderPath { get; init; }
        [JsonPropertyName("iniExisted")] public bool? DesktopIniExisted { get; init; }
        [JsonPropertyName("iniBytes")] public byte[]? OriginalDesktopIni { get; init; }
        [JsonPropertyName("iniAttrs")] public FileAttributes? OriginalDesktopIniAttributes { get; init; }
        [JsonPropertyName("folderAttrs")] public FileAttributes? OriginalFolderAttributes { get; init; }
        [JsonPropertyName("icon")] public string? IconPath { get; init; }
        [JsonPropertyName("written")] public string? WrittenSha256 { get; init; }
        [JsonPropertyName("folderId")] public string? FolderId { get; init; }

        // Older records have none of these, so each stays optional and the journal stays readable.
        [JsonPropertyName("folderTime")] public DateTime? OriginalFolderLastWriteUtc { get; init; }
        [JsonPropertyName("appliedTime")] public DateTime? AppliedFolderLastWriteUtc { get; init; }
        [JsonPropertyName("iniTime")] public DateTime? OriginalDesktopIniLastWriteUtc { get; init; }
        [JsonPropertyName("spec")] public string? SpecJson { get; init; }
        [JsonPropertyName("repair")] public bool? IsRepair { get; init; }
        [JsonPropertyName("created")] public bool? CreatedFolder { get; init; }
        [JsonPropertyName("files")] public CreatedFile[]? CreatedFiles { get; init; }
        [JsonPropertyName("planTime")] public DateTime? PlannedFolderLastWriteUtc { get; init; }
        [JsonPropertyName("planMoveOn")] public bool? PlannedMoveOn { get; init; }
        [JsonPropertyName("folders")] public string[]? Folders { get; init; }
        [JsonPropertyName("attempt")] public int? Attempt { get; init; }
    }
}
