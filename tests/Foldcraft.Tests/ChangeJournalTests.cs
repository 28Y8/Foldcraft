using System.Text;
using Foldcraft.Core.Journal;

namespace Foldcraft.Tests;

public sealed class ChangeJournalTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private static ChangeRecord Change(string folder, byte[]? original = null) => new()
    {
        BatchId = Guid.NewGuid(),
        FolderPath = folder,
        DesktopIniExisted = original is not null,
        OriginalDesktopIni = original,
        OriginalDesktopIniAttributes = original is null ? null : FileAttributes.Hidden | FileAttributes.System,
        OriginalFolderAttributes = FileAttributes.Directory,
        IconPath = @"C:\icons\a.ico",
        WrittenSha256 = "abc",
    };

    [Fact]
    public void An_empty_or_absent_journal_reads_as_empty()
    {
        var snapshot = _box.Journal.Read();

        Assert.Empty(snapshot.Entries);
        Assert.Equal(0, snapshot.CorruptRecords);
    }

    [Fact]
    public void A_record_round_trips_every_byte_and_field()
    {
        var original = new byte[256];
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = (byte)i;
        }

        var now = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var journal = new ChangeJournal(_box.Paths.JournalFile, new FixedTime(now));
        var change = Change(@"C:\Ünï\日本語", original);

        var id = journal.RecordChange(change);
        var entry = Assert.Single(journal.Read().Entries);

        Assert.Equal(id, entry.Id);
        Assert.Equal(now, entry.Timestamp);
        Assert.Equal(JournalEntryState.Pending, entry.State);
        Assert.Equal(change.FolderPath, entry.FolderPath);
        Assert.Equal(original, entry.Change.OriginalDesktopIni);
        Assert.Equal(change.OriginalDesktopIniAttributes, entry.Change.OriginalDesktopIniAttributes);
        Assert.Equal(change.OriginalFolderAttributes, entry.Change.OriginalFolderAttributes);
        Assert.Equal(change.BatchId, entry.Change.BatchId);
        Assert.Equal(change.WrittenSha256, entry.Change.WrittenSha256);
    }

    [Fact]
    public void A_folder_with_no_desktop_ini_is_recorded_as_such()
    {
        _box.Journal.RecordChange(Change(@"C:\a"));

        var entry = Assert.Single(_box.Journal.Read().Entries);

        Assert.False(entry.Change.DesktopIniExisted);
        Assert.Null(entry.Change.OriginalDesktopIni);
    }

    [Fact]
    public void States_replay_in_order()
    {
        var applied = _box.Journal.RecordChange(Change(@"C:\a"));
        var rolled = _box.Journal.RecordChange(Change(@"C:\b"));
        var undone = _box.Journal.RecordChange(Change(@"C:\c"));
        var forgotten = _box.Journal.RecordChange(Change(@"C:\d"));
        var pending = _box.Journal.RecordChange(Change(@"C:\e"));

        _box.Journal.MarkApplied(applied);
        _box.Journal.MarkRolledBack(rolled);
        _box.Journal.MarkApplied(undone);
        _box.Journal.MarkUndone(undone);
        _box.Journal.MarkForgotten(forgotten);

        var states = _box.Journal.Read().Entries.ToDictionary(e => e.Id, e => e.State);

        Assert.Equal(JournalEntryState.Applied, states[applied]);
        Assert.Equal(JournalEntryState.RolledBack, states[rolled]);
        Assert.Equal(JournalEntryState.Undone, states[undone]);
        Assert.Equal(JournalEntryState.Forgotten, states[forgotten]);
        Assert.Equal(JournalEntryState.Pending, states[pending]);
        Assert.Equal([applied, pending], _box.Journal.Read().Active.Select(e => e.Id));
    }

    [Fact]
    public void A_final_record_cut_off_at_any_byte_never_harms_earlier_records()
    {
        _box.Journal.RecordChange(Change(@"C:\first", [1, 2, 3]));
        var intact = File.ReadAllBytes(_box.Paths.JournalFile);

        _box.Journal.RecordChange(Change(@"C:\second", [4, 5, 6]));
        var full = File.ReadAllBytes(_box.Paths.JournalFile);

        // The last byte is only the newline, and the record is complete without it.
        for (var cut = full.Length - 2; cut > intact.Length; cut--)
        {
            File.WriteAllBytes(_box.Paths.JournalFile, full[..cut]);

            var snapshot = _box.Journal.Read();

            var entry = Assert.Single(snapshot.Entries);
            Assert.Equal(@"C:\first", entry.FolderPath);
            Assert.Equal(1, snapshot.CorruptRecords);
        }
    }

    [Fact]
    public void A_complete_record_missing_only_its_newline_still_counts()
    {
        _box.Journal.RecordChange(Change(@"C:\first"));
        var bytes = File.ReadAllBytes(_box.Paths.JournalFile);
        File.WriteAllBytes(_box.Paths.JournalFile, bytes[..^1]);

        Assert.Equal(@"C:\first", Assert.Single(_box.Journal.Read().Entries).FolderPath);
    }

    [Fact]
    public void Appending_after_a_torn_tail_keeps_everything_else_readable()
    {
        var first = _box.Journal.RecordChange(Change(@"C:\first"));
        _box.Journal.RecordChange(Change(@"C:\torn"));
        var bytes = File.ReadAllBytes(_box.Paths.JournalFile);
        File.WriteAllBytes(_box.Paths.JournalFile, bytes[..^20]);

        var third = _box.Journal.RecordChange(Change(@"C:\third"));

        var snapshot = _box.Journal.Read();

        Assert.Equal([first, third], snapshot.Entries.Select(e => e.Id));
        Assert.Equal(1, snapshot.CorruptRecords);
    }

    [Fact]
    public void The_torn_bytes_are_kept_because_the_journal_is_append_only()
    {
        _box.Journal.RecordChange(Change(@"C:\first"));
        var bytes = File.ReadAllBytes(_box.Paths.JournalFile);
        var torn = bytes[..^20];
        File.WriteAllBytes(_box.Paths.JournalFile, torn);

        _box.Journal.RecordChange(Change(@"C:\second"));

        Assert.Equal(torn, File.ReadAllBytes(_box.Paths.JournalFile)[..torn.Length]);
    }

    [Fact]
    public void A_flipped_byte_mid_journal_loses_only_that_record()
    {
        _box.Journal.RecordChange(Change(@"C:\a"));
        var firstLength = new FileInfo(_box.Paths.JournalFile).Length;
        _box.Journal.RecordChange(Change(@"C:\b"));
        var secondEnd = new FileInfo(_box.Paths.JournalFile).Length;
        _box.Journal.RecordChange(Change(@"C:\c"));

        var bytes = File.ReadAllBytes(_box.Paths.JournalFile);
        bytes[(firstLength + secondEnd) / 2] ^= 0x01;
        File.WriteAllBytes(_box.Paths.JournalFile, bytes);

        var snapshot = _box.Journal.Read();

        Assert.Equal([@"C:\a", @"C:\c"], snapshot.Entries.Select(e => e.FolderPath));
        Assert.Equal(1, snapshot.CorruptRecords);
    }

    [Fact]
    public void Garbage_in_the_file_is_counted_not_fatal()
    {
        _box.Journal.RecordChange(Change(@"C:\a"));
        File.AppendAllText(_box.Paths.JournalFile, "not a record at all\n\n{\"k\":0}\n", Encoding.UTF8);
        _box.Journal.RecordChange(Change(@"C:\b"));

        var snapshot = _box.Journal.Read();

        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Equal(2, snapshot.CorruptRecords);
    }

    [Fact]
    public void A_status_record_for_an_unknown_change_is_ignored()
    {
        _box.Journal.MarkUndone(Guid.NewGuid());

        var snapshot = _box.Journal.Read();

        Assert.Empty(snapshot.Entries);
        Assert.Equal(1, snapshot.CorruptRecords);
    }

    [Fact]
    public void Records_are_appended_and_never_rewritten()
    {
        _box.Journal.RecordChange(Change(@"C:\a"));
        var before = File.ReadAllBytes(_box.Paths.JournalFile);

        _box.Journal.MarkUndone(_box.Journal.Read().Entries[0].Id);

        var after = File.ReadAllBytes(_box.Paths.JournalFile);
        Assert.True(after.Length > before.Length);
        Assert.Equal(before, after[..before.Length]);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
