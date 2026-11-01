using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Foldcraft.App.Services;
using Foldcraft.Core;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Ini;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

public sealed class FolderCustomizerTests : IDisposable
{
    private const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;

    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private static string IconResource(string folder) =>
        DesktopIni.Load(Sandbox.IniPath(folder)).GetValue(".ShellClassInfo", "IconResource")!;

    [Fact]
    public void Applying_writes_a_referenced_icon_and_marks_file_and_folder()
    {
        var folder = _box.NewFolder();

        var result = _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Applied, result.Outcome);

        var value = IconResource(folder);
        Assert.EndsWith(",0", value);

        var icon = value[..value.LastIndexOf(',')];
        Assert.True(File.Exists(icon));
        Assert.Equal(_box.Paths.IconsDirectory, Path.GetDirectoryName(icon), ignoreCase: true);

        var iniAttributes = File.GetAttributes(Sandbox.IniPath(folder));
        Assert.True(iniAttributes.HasFlag(FileAttributes.Hidden));
        Assert.True(iniAttributes.HasFlag(FileAttributes.System));
        Assert.True(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));

        Assert.Contains(folder, _box.Shell.Notified, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_file_written_is_utf16_with_a_bom()
    {
        var folder = _box.NewFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var bytes = File.ReadAllBytes(Sandbox.IniPath(folder));
        Assert.Equal(new byte[] { 0xFF, 0xFE }, bytes[..2]);
    }

    [Fact]
    public void A_system_folder_is_not_also_made_read_only()
    {
        var folder = _box.NewFolder();
        File.SetAttributes(folder, FileAttributes.System);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.False(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
        Assert.True(File.GetAttributes(folder).HasFlag(FileAttributes.System));
    }

    [Fact]
    public void Foreign_content_in_an_existing_desktop_ini_survives()
    {
        var folder = _box.NewFolder();
        Sandbox.PlantIni(folder, Encoding.UTF8.GetBytes(
            "; note\r\n[.ShellClassInfo]\r\nLocalizedResourceName=@shell32.dll,-21770\r\nInfoTip=mine\r\n\r\n[ViewState]\r\nMode=\r\nVid=\r\nFolderType=Pictures\r\n"));

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var text = DesktopIni.Load(Sandbox.IniPath(folder)).ToText();
        var withoutIcon = string.Join("\r\n", text.Split("\r\n").Where(l => !l.StartsWith("IconResource=", StringComparison.Ordinal)));

        Assert.Equal(
            "; note\r\n[.ShellClassInfo]\r\nLocalizedResourceName=@shell32.dll,-21770\r\nInfoTip=mine\r\n\r\n[ViewState]\r\nMode=\r\nVid=\r\nFolderType=Pictures\r\n",
            withoutIcon);
    }

    [Fact]
    public void Sibling_files_and_subfolders_are_byte_identical_after_apply_and_undo()
    {
        var folder = _box.NewFolder();
        var note = Path.Combine(folder, "note.txt");
        var binary = Path.Combine(folder, "data.bin");
        var hidden = Path.Combine(folder, "secret.dat");
        var child = Path.Combine(folder, "child");
        var nestedIni = Path.Combine(child, "desktop.ini");

        Directory.CreateDirectory(child);
        File.WriteAllText(note, "hello");
        File.WriteAllBytes(binary, RandomNumberGenerator.GetBytes(4096));
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.ReadOnly);
        File.WriteAllText(nestedIni, "[.ShellClassInfo]\r\nInfoTip=child\r\n");
        File.SetAttributes(nestedIni, HiddenSystem);

        var before = Fingerprint(folder);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Assert.Equal(before, Fingerprint(folder, exclude: "desktop.ini"));

        _box.Restorer().UndoLast();
        Assert.Equal(before, Fingerprint(folder));
    }

    /// <summary>Hash, attributes and write time of everything under a folder.</summary>
    private static string[] Fingerprint(string root, string? exclude = null)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };

        return Directory.EnumerateFileSystemEntries(root, "*", options)
            .Where(p => exclude is null || !p.Equals(Path.Combine(root, exclude), StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p =>
            {
                var attributes = File.GetAttributes(p);
                var hash = attributes.HasFlag(FileAttributes.Directory) ? "dir" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
                var written = attributes.HasFlag(FileAttributes.Directory) ? 0 : File.GetLastWriteTimeUtc(p).Ticks;
                return $"{p}|{attributes}|{hash}|{written}";
            })
            .ToArray();
    }

    [Fact]
    public void Applying_the_same_icon_twice_changes_nothing_and_records_nothing()
    {
        var folder = _box.NewFolder();
        var customizer = _box.Customizer();

        customizer.Apply(folder, Sandbox.BlueStar);
        var second = customizer.Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Unchanged, second.Outcome);
        Assert.Single(_box.Journal.Read().Entries);
    }

    [Fact]
    public void A_refused_folder_is_untouched_and_unrecorded()
    {
        var folder = _box.NewFolder();
        var link = Sandbox.CreateJunction(Path.Combine(_box.Root, "folders", "link"), folder);

        var result = _box.Customizer().Apply(link, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Refused, result.Outcome);
        Assert.Equal(FolderRefusal.ReparsePoint, result.Refusal);
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.False(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
        Assert.Empty(_box.Journal.Read().Entries);
    }

    [Fact]
    public void The_original_is_journaled_before_the_folder_is_touched()
    {
        var folder = _box.NewFolder();
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);

        JournalSnapshot? atWrite = null;

        var ops = new FaultyOperations
        {
            FailWrite = path =>
            {
                if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    atWrite ??= _box.Journal.Read();
                }

                return false;
            },
        };

        _box.Customizer(ops).Apply(folder, Sandbox.BlueStar);

        var entry = Assert.Single(atWrite!.Entries);
        Assert.Equal(original, entry.Change.OriginalDesktopIni);
        Assert.Equal(JournalEntryState.Pending, entry.State);
    }

    [Fact]
    public void An_unwritable_journal_stops_the_change_before_it_starts()
    {
        var folder = _box.NewFolder();
        var blockedPath = Path.Combine(_box.Root, "journal-is-a-directory");
        Directory.CreateDirectory(blockedPath);

        var customizer = new FolderCustomizer(_box.Safety, new ChangeJournal(blockedPath), _box.Icons, _box.Shell);
        var result = customizer.Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.False(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void A_batch_reports_each_folder_and_one_failure_stops_nothing()
    {
        var first = _box.NewFolder("first");
        var last = _box.NewFolder("last");
        var missing = Path.Combine(_box.Root, "folders", "missing");
        var iniIsDirectory = _box.NewFolder("ini-is-directory");
        Directory.CreateDirectory(Sandbox.IniPath(iniIsDirectory));

        var binaryIni = _box.NewFolder("binary-ini");
        byte[] garbage = [0x00, 0x01, 0x02];
        Sandbox.PlantIni(binaryIni, garbage);

        var result = _box.Customizer().Apply(
            [first, missing, _box.Paths.DataDirectory, iniIsDirectory, binaryIni, last],
            Sandbox.BlueStar);

        Assert.Equal(
            [
                ApplyOutcome.Applied, ApplyOutcome.Refused, ApplyOutcome.Refused,
                ApplyOutcome.Failed, ApplyOutcome.Failed, ApplyOutcome.Applied,
            ],
            result.Results.Select(r => r.Outcome));

        Assert.Equal(FolderRefusal.NotFound, result.Results[1].Refusal);
        Assert.Equal(FolderRefusal.FoldcraftData, result.Results[2].Refusal);
        Assert.Equal(2, result.AppliedCount);
        Assert.False(result.AllSucceeded);

        Assert.Equal(garbage, File.ReadAllBytes(Sandbox.IniPath(binaryIni)));
        Assert.False(File.GetAttributes(binaryIni).HasFlag(FileAttributes.ReadOnly));
        Assert.True(Directory.Exists(Sandbox.IniPath(iniIsDirectory)));
    }

    [Fact]
    public void A_mid_batch_write_failure_rolls_back_that_folder_and_the_others_finish()
    {
        var good = _box.NewFolder("good");
        var bad = _box.NewFolder("bad");
        var after = _box.NewFolder("after");
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(bad, original, FileAttributes.Hidden);

        var ops = new FaultyOperations { FailCommit = path => path.StartsWith(bad, StringComparison.OrdinalIgnoreCase) };
        var result = _box.Customizer(ops).Apply([good, bad, after], Sandbox.BlueStar);

        Assert.Equal(
            [ApplyOutcome.Applied, ApplyOutcome.Failed, ApplyOutcome.Applied],
            result.Results.Select(r => r.Outcome));

        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(bad)));
        Assert.Equal(FileAttributes.Hidden, File.GetAttributes(Sandbox.IniPath(bad)));
        Assert.Equal(["desktop.ini"], Directory.GetFiles(bad).Select(Path.GetFileName));

        var states = _box.Journal.Read().Entries.ToDictionary(e => e.FolderPath, e => e.State, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(JournalEntryState.RolledBack, states[bad]);
        Assert.Equal(JournalEntryState.Applied, states[good]);
    }

    [Fact]
    public void A_failure_after_desktop_ini_is_written_rolls_the_folder_back()
    {
        var folder = _box.NewFolder();
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);
        var folderAttributes = File.GetAttributes(folder);

        var failedOnce = false;

        var ops = new FaultyOperations
        {
            FailSetAttributes = (path, attributes) =>
            {
                if (!failedOnce && path.Equals(folder, StringComparison.OrdinalIgnoreCase) && attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    failedOnce = true;
                    return true;
                }

                return false;
            },
        };

        var result = _box.Customizer(ops).Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.True(failedOnce);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.Equal(HiddenSystem, File.GetAttributes(Sandbox.IniPath(folder)));
        Assert.Equal(folderAttributes, File.GetAttributes(folder));
        Assert.Equal(JournalEntryState.RolledBack, Assert.Single(_box.Journal.Read().Entries).State);
    }

    [Fact]
    public void A_failure_on_a_folder_with_no_desktop_ini_removes_the_one_we_wrote()
    {
        var folder = _box.NewFolder();
        var failedOnce = false;

        var ops = new FaultyOperations
        {
            FailSetAttributes = (path, attributes) =>
            {
                if (!failedOnce && path.Equals(folder, StringComparison.OrdinalIgnoreCase))
                {
                    failedOnce = true;
                    return true;
                }

                return false;
            },
        };

        var result = _box.Customizer(ops).Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public void When_rollback_also_fails_the_change_stays_restorable()
    {
        var folder = _box.NewFolder();
        var faulty = true;

        var ops = new FaultyOperations
        {
            FailSetAttributes = (path, _) => faulty && path.Equals(folder, StringComparison.OrdinalIgnoreCase),
        };

        var result = _box.Customizer(ops).Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.FailedRollbackIncomplete, result.Outcome);
        Assert.True(Assert.Single(_box.Journal.Read().Entries).IsActive);

        faulty = false;
        var report = _box.Restorer().RestoreAll();

        Assert.True(report.AllRestored);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public void An_unforeseen_exception_type_is_rolled_back_and_stops_nothing()
    {
        var bad = _box.NewFolder("bad");
        var after = _box.NewFolder("after");
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(bad, original);

        var ops = new UnforeseenFailure(bad);
        var result = _box.Customizer(ops).Apply([bad, after], Sandbox.BlueStar);

        Assert.Equal([ApplyOutcome.Failed, ApplyOutcome.Applied], result.Results.Select(r => r.Outcome));
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(bad)));
    }

    private sealed class UnforeseenFailure(string folder) : Foldcraft.Core.Infrastructure.FileOperations
    {
        public override void CommitReplace(string tempPath, string destinationPath, bool destinationExists)
        {
            if (destinationPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Nobody expected this exception type.");
            }

            base.CommitReplace(tempPath, destinationPath, destinationExists);
        }
    }

    [Fact]
    public void A_desktop_ini_that_would_outgrow_the_backup_limit_is_refused_before_anything_changes()
    {
        var folder = _box.NewFolder();
        var original = Encoding.ASCII.GetBytes("[A]\r\n; " + new string('x', 600_000) + "\r\n");
        Sandbox.PlantIni(folder, original);

        var result = _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.False(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
        Assert.Empty(_box.Journal.Read().Entries);
        Assert.Equal("This folder's settings file is too large to change safely.", PlainEnglish.ApplyResult(result));
    }

    [Fact]
    public void A_desktop_ini_past_the_backup_limit_is_explained_in_plain_words()
    {
        var folder = _box.NewFolder();
        Sandbox.PlantIni(folder, new byte[2 * 1024 * 1024]);

        var result = _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Contains("can't be backed up", PlainEnglish.ApplyResult(result));
    }

    [Fact]
    public void A_late_failure_still_rolls_back_when_desktop_ini_has_outgrown_the_limit()
    {
        var folder = _box.NewFolder();
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);
        var failedOnce = false;

        var ops = new FaultyOperations
        {
            FailSetAttributes = (path, attributes) =>
            {
                if (!failedOnce && path.Equals(folder, StringComparison.OrdinalIgnoreCase) && attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    failedOnce = true;
                    var ini = Sandbox.IniPath(folder);
                    File.SetAttributes(ini, FileAttributes.Normal);
                    File.WriteAllBytes(ini, new byte[2 * 1024 * 1024]);
                    return true;
                }

                return false;
            },
        };

        var result = _box.Customizer(ops).Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
    }

    [Theory]
    [InlineData("per%TEMP%cent", false)]
    [InlineData("plain-data", true)]
    [InlineData("lone%pct", true)]
    [InlineData("with%NOSUCHVAR%x", true)]
    public void An_icon_path_the_shell_would_expand_fails_clearly(string dataFolder, bool works)
    {
        using var box = new Sandbox(dataFolder);
        var a = box.NewFolder("a");
        var b = box.NewFolder("b");

        var batch = box.Customizer().Apply([a, b], Sandbox.BlueStar);

        Assert.All(batch.Results, r => Assert.Equal(works ? ApplyOutcome.Applied : ApplyOutcome.Failed, r.Outcome));

        if (!works)
        {
            Assert.Empty(Directory.GetFileSystemEntries(a));
            Assert.Empty(box.Journal.Read().Entries);
            Assert.Contains("percent", batch.Results[0].Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_shell_that_throws_cannot_fail_an_apply()
    {
        var folder = _box.NewFolder();
        var customizer = new FolderCustomizer(_box.Safety, _box.Journal, _box.Icons, new ThrowingShell());

        Assert.Equal(ApplyOutcome.Applied, customizer.Apply(folder, Sandbox.BlueStar).Outcome);
    }

    private sealed class ThrowingShell : IShellNotifier
    {
        public void FolderChanged(string folderPath) => throw new ExternalException("Explorer is unavailable.");
    }

    [Fact]
    public void A_spec_that_cannot_render_fails_every_folder_and_touches_none()
    {
        var a = _box.NewFolder("a");
        var b = _box.NewFolder("b");

        var bad = new FolderIconSpec
        {
            Fill = FolderFill.Solid(new IconColor(1, 2, 3)),
            CustomImagePath = Path.Combine(_box.Root, "no-such-image.png"),
        };

        var result = _box.Customizer().Apply([a, b], bad);

        Assert.All(result.Results, r => Assert.Equal(ApplyOutcome.Failed, r.Outcome));
        Assert.Empty(Directory.GetFileSystemEntries(a));
        Assert.Empty(_box.Journal.Read().Entries);
    }

    [Fact]
    public void Canceling_stops_before_the_next_folder_and_reports_the_rest()
    {
        var a = _box.NewFolder("a");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var result = _box.Customizer().Apply([a], Sandbox.BlueStar, canceled.Token);

        Assert.Equal(ApplyOutcome.Canceled, Assert.Single(result.Results).Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(a));
    }

    [Fact]
    public void Two_spellings_of_one_folder_in_a_batch_are_one_change()
    {
        var folder = _box.NewFolder();

        var result = _box.Customizer().Apply([folder, folder.ToUpperInvariant() + @"\"], Sandbox.BlueStar);

        Assert.Equal([ApplyOutcome.Applied, ApplyOutcome.Unchanged], result.Results.Select(r => r.Outcome));
        Assert.Single(_box.Journal.Read().Entries);
    }

    [Fact]
    public void Non_ascii_folder_and_data_paths_work_end_to_end()
    {
        using var box = new Sandbox("dätä 日本語 📁");
        var folder = box.NewFolder("Ünï çødé 名前 🎨, with comma");

        var result = box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Applied, result.Outcome);

        var value = IconResource(folder);
        Assert.Contains("dätä 日本語 📁", value);
        Assert.True(File.Exists(value[..value.LastIndexOf(',')]));

        Assert.True(box.Restorer().UndoLast().AllRestored);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public void A_folder_path_longer_than_max_path_works_end_to_end()
    {
        var path = Path.Combine(_box.Root, "folders");

        while (path.Length < 300)
        {
            path = Path.Combine(path, new string('L', 40));
        }

        Directory.CreateDirectory(path);
        var original = Encoding.UTF8.GetBytes("[A]\r\nb=1\r\n");
        Sandbox.PlantIni(path, original);
        var sibling = Path.Combine(path, "sibling.txt");
        File.WriteAllText(sibling, "keep");

        var result = _box.Customizer().Apply(path, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Applied, result.Outcome);
        Assert.True(path.Length >= 300);
        Assert.NotNull(IconResource(path));

        Assert.True(_box.Restorer().UndoLast().AllRestored);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(path)));
        Assert.Equal("keep", File.ReadAllText(sibling));
    }

    [Fact]
    public void The_shell_itself_reads_the_icon_location_from_our_desktop_ini()
    {
        var folder = _box.NewFolder("shell-read");

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var value = IconResource(folder);
        var icon = value[..value.LastIndexOf(',')];

        var info = default(ShFileInfo);
        var ok = SHGetFileInfo(folder, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIconLocation);

        Assert.NotEqual(IntPtr.Zero, ok);
        Assert.Equal(icon, info.DisplayName, ignoreCase: true);
    }

    [Fact]
    public void The_real_shell_notifier_runs_without_error_on_a_sandbox_folder()
    {
        new ShellNotifier().FolderChanged(_box.NewFolder("notify"));
    }

    [Fact]
    public void Default_paths_are_never_used_when_paths_are_injected()
    {
        var folder = _box.NewFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.StartsWith(_box.Root, _box.Paths.DataDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(_box.Paths.JournalFile));
        Assert.NotEqual(FoldcraftPaths.Default().DataDirectory, _box.Paths.DataDirectory);
    }

    private const uint ShgfiIconLocation = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShFileInfo info, uint size, uint flags);
}
