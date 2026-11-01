using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Settings;
using Foldcraft.Core;
using Microsoft.Win32;

namespace Foldcraft.Tests;

/// <summary>Runs only under tools\Test-Update.ps1, which builds the two real versions this needs.</summary>
internal sealed class UpdateEndToEndFactAttribute : FactAttribute
{
    public UpdateEndToEndFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(UpdateEndToEndTests.AppVariable) is null)
        {
            Skip = @"tools\Test-Update.ps1 runs this.";
        }
    }
}

/// <summary>
/// The whole update path through the real app and the real updater, with no GitHub behind it:
/// an older portable build in a temp folder finds a newer one in a local folder, then downloads,
/// installs and restarts into it, all by pressing its own buttons. A portable package leaves the
/// registry and the Start Menu alone, and this checks that it did.
/// </summary>
[Trait("Category", TestCategories.Desktop)]
public sealed class UpdateEndToEndTests
{
    public const string AppVariable = "FOLDCRAFT_UPDATE_TEST_APP";
    public const string DataVariable = "FOLDCRAFT_UPDATE_TEST_DATA";
    public const string SampleVariable = "FOLDCRAFT_UPDATE_TEST_SAMPLE";
    public const string OldVersionVariable = "FOLDCRAFT_UPDATE_TEST_OLD";
    public const string NewVersionVariable = "FOLDCRAFT_UPDATE_TEST_NEW";
    public const string PicturesVariable = "FOLDCRAFT_UPDATE_TEST_PICTURES";

    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Slowly = TimeSpan.FromMinutes(3);

    private static string Required(string variable) =>
        Environment.GetEnvironmentVariable(variable) ?? throw new InvalidOperationException($"{variable} isn't set.");

    [UpdateEndToEndFact]
    public void An_older_build_updates_itself_and_keeps_its_data_and_history()
    {
        var root = Required(AppVariable);
        var data = Required(DataVariable);
        var sample = Required(SampleVariable);
        var oldVersion = Required(OldVersionVariable);
        var newVersion = Required(NewVersionVariable);
        var launcher = Path.Combine(root, "Foldcraft.exe");
        var current = Path.Combine(root, "current", "Foldcraft.exe");
        var updater = Path.Combine(root, "Update.exe");
        var journalFile = Path.Combine(data, "journal.fcj");
        var keptFile = Path.Combine(sample, "keep me.txt");

        var before = PcState.Capture();

        Directory.CreateDirectory(sample);
        File.WriteAllText(keptFile, "This file must never change.");

        Assert.Equal(oldVersion, VersionOf(current));

        // The launcher Windows would start, with the same options as ever.
        using var starter = Process.Start(new ProcessStartInfo(launcher)
        {
            ArgumentList = { "--data-dir", data, "--customize", sample },
            UseShellExecute = false,
        });

        var first = WaitForWindowedProcess(current, excluding: 0);
        var window = WaitForWindow(first);

        // A real change through the old build, so there's real history to keep.
        Invoke(WaitForButton(window, "Apply to 1 folder", Soon));
        WaitUntil(() => JournalHasAppliedChange(journalFile), Soon, "the look to be applied");
        Assert.True(File.Exists(Path.Combine(sample, "desktop.ini")));

        // The startup check finds it, and offering it downloads nothing.
        var update = WaitForButton(window, "Update", Slowly);

        Assert.Contains($"Foldcraft {newVersion} is available.", AllText(window));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "packages"), $"*{newVersion}*"));
        Picture(window, "banner-available");

        var journalBefore = File.ReadAllBytes(journalFile);
        var iconsBefore = Directory.GetFiles(Path.Combine(data, "Icons")).Order(StringComparer.Ordinal).ToArray();

        // Two versions built with the same tools carry the same updater, so this mark is what
        // shows whether the download replaced it. The updater ignores what's after its last section.
        File.AppendAllBytes(updater, [0]);

        var updaterBefore = HashOf(File.ReadAllBytes(updater));

        Invoke(update);

        var restart = WaitForButton(window, "Restart Now", Slowly);

        Assert.Contains($"Foldcraft {newVersion} is ready to install.", AllText(window));
        Assert.Equal(oldVersion, VersionOf(current));
        Picture(window, "banner-ready");

        // The app hasn't changed, but the updater has: it's the one thing a finished download replaces.
        Assert.NotEqual(updaterBefore, HashOf(File.ReadAllBytes(updater)));
        Assert.Equal(PackagedUpdaterHash(root, newVersion), HashOf(File.ReadAllBytes(updater)));

        // The updater stops every copy running from this folder, so another one holds the restart back.
        using (var other = Process.Start(new ProcessStartInfo(current)
        {
            ArgumentList = { "--data-dir", data + "-other" },
            UseShellExecute = false,
        }))
        {
            var otherWindow = WaitForWindow(other!);

            Invoke(restart);
            Invoke(FindButton(WaitForDialog(first, "Restart Foldcraft to update?"), "Restart Now")!);
            WaitUntil(() => AllText(window).Contains("Another Foldcraft window is open"), Soon, "the restart to be held back");

            Assert.False(first.HasExited);
            Assert.False(other!.HasExited);
            Assert.Equal(oldVersion, VersionOf(current));

            ((WindowPattern)otherWindow.GetCurrentPattern(WindowPattern.Pattern)).Close();
            Assert.True(other.WaitForExit((int)Soon.TotalMilliseconds), "The other copy didn't close.");
        }

        // Nothing installs until the question is answered.
        Invoke(restart);

        var question = WaitForDialog(first, "Restart Foldcraft to update?");

        Assert.Equal(oldVersion, VersionOf(current));
        Invoke(FindButton(question, "Restart Now")!);

        Assert.True(first.WaitForExit((int)Slowly.TotalMilliseconds), "The old version didn't close.");

        // The updater installs it and starts the new version with the same data folder.
        var second = WaitForWindowedProcess(current, excluding: first.Id);
        var secondWindow = WaitForWindow(second);

        Assert.Equal(newVersion, VersionOf(current));
        Assert.Equal(current, second.MainModule!.FileName, StringComparer.OrdinalIgnoreCase);

        // Nothing of what the old version wrote has changed.
        var journalAfter = File.ReadAllBytes(journalFile);

        Assert.Equal(journalBefore, journalAfter.AsSpan(0, journalBefore.Length).ToArray());
        Assert.Equal(iconsBefore, Directory.GetFiles(Path.Combine(data, "Icons")).Order(StringComparer.Ordinal).ToArray());
        Assert.NotNull(new SettingsStore(new FoldcraftPaths(data)).Load().LastUpdateCheck);

        // A start from the right-click entry's path reaches the new version, through the handoff.
        using (var rightClick = Process.Start(new ProcessStartInfo(current)
        {
            ArgumentList = { "--customize", sample, "--data-dir", data },
            UseShellExecute = false,
        }))
        {
            Assert.True(rightClick!.WaitForExit((int)Soon.TotalMilliseconds), "The second start didn't hand off.");
            Assert.Equal(0, rightClick.ExitCode);
        }

        // Hook runs end by themselves with no window. The uninstall hook finds no entry for this
        // copy here, so it only reads, and nothing is left for it to remove.
        foreach (var hook in new[] { "--veloapp-install", "--veloapp-updated", "--veloapp-obsolete", "--veloapp-uninstall" })
        {
            using var run = Process.Start(new ProcessStartInfo(current)
            {
                ArgumentList = { hook, newVersion },
                UseShellExecute = false,
            });

            Assert.True(run!.WaitForExit((int)Soon.TotalMilliseconds), $"{hook} didn't end.");
            Assert.Equal(0, run.ExitCode);
        }

        // The history the old version wrote can still be undone by the new one.
        AutomationElement? undo = null;

        // Each try selects the tab again, because the folder just handed over opens Customize when it lands.
        WaitUntil(
            () =>
            {
                SelectTab(secondWindow, "History");
                undo = FindButton(secondWindow, "Undo", exact: false);
                return undo is not null;
            },
            Soon,
            "the Undo button");

        Picture(secondWindow, "history-after-update");
        Invoke(undo!);
        WaitUntil(() => !File.Exists(Path.Combine(sample, "desktop.ini")), Soon, "the undo to finish");

        Assert.Equal("This file must never change.", File.ReadAllText(keptFile));

        ((WindowPattern)secondWindow.GetCurrentPattern(WindowPattern.Pattern)).Close();

        Assert.True(second.WaitForExit((int)Soon.TotalMilliseconds), "The new version didn't close.");

        foreach (var left in Running(current))
        {
            Assert.Fail($"Foldcraft is still running as process {left.Id}.");
        }

        Assert.Equal(before, PcState.Capture());
    }

    private static string? VersionOf(string exe) => FileVersionInfo.GetVersionInfo(exe).ProductVersion;

    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>The updater inside a downloaded package, which keeps its old name in there.</summary>
    private static string PackagedUpdaterHash(string root, string version)
    {
        using var package = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(Path.Combine(root, "packages"), $"*{version}-full.nupkg")));
        using var entry = package.Entries.Single(item => item.FullName.EndsWith("Squirrel.exe", StringComparison.Ordinal)).Open();
        using var bytes = new MemoryStream();

        entry.CopyTo(bytes);

        return HashOf(bytes.ToArray());
    }

    private static bool JournalHasAppliedChange(string journalFile) =>
        File.Exists(journalFile) &&
        new ChangeJournal(journalFile).Read().Entries.Any(entry => entry.State == JournalEntryState.Applied);

    private static IEnumerable<Process> Running(string exe) =>
        Process.GetProcessesByName("Foldcraft").Where(process =>
        {
            try
            {
                return string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        });

    /// <summary>The one with a window. The updater also starts the new version briefly for its hook, which has none.</summary>
    private static Process WaitForWindowedProcess(string exe, int excluding)
    {
        Process? found = null;

        WaitUntil(
            () => (found = Running(exe).FirstOrDefault(process => process.Id != excluding && process.MainWindowHandle != IntPtr.Zero)) is not null,
            Slowly,
            "Foldcraft to open its window");

        return found!;
    }

    private static AutomationElement WaitForWindow(Process process)
    {
        AutomationElement? found = null;

        WaitUntil(
            () =>
            {
                process.Refresh();

                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    found = AutomationElement.FromHandle(process.MainWindowHandle);
                }

                return found is not null;
            },
            Slowly,
            "the window to open");

        return found!;
    }

    private static AutomationElement WaitForDialog(Process process, string title)
    {
        AutomationElement? found = null;

        WaitUntil(
            () =>
            {
                found = AutomationElement.RootElement.FindFirst(
                    TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
                        new PropertyCondition(AutomationElement.NameProperty, title)));

                return found is not null;
            },
            Soon,
            "the question to appear");

        return found!;
    }

    /// <summary>The Undo buttons have longer names, which say what each one undoes.</summary>
    private static AutomationElement? FindButton(AutomationElement within, string name, bool exact = true) =>
        within.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>()
            .FirstOrDefault(button =>
                (exact ? button.Current.Name == name : button.Current.Name.StartsWith(name, StringComparison.Ordinal)) &&
                !button.Current.IsOffscreen &&
                button.Current.IsEnabled);

    private static AutomationElement WaitForButton(AutomationElement within, string name, TimeSpan timeout)
    {
        AutomationElement? found = null;

        WaitUntil(() => (found = FindButton(within, name)) is not null, timeout, $"the \"{name}\" button");

        return found!;
    }

    private static void Invoke(AutomationElement button) =>
        ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    private static void SelectTab(AutomationElement window, string name)
    {
        var tab = window.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
                new PropertyCondition(AutomationElement.NameProperty, name)));

        ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    }

    private static string AllText(AutomationElement window) => string.Join(
        '\n',
        window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>()
            .Select(text => text.Current.Name));

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string what)
    {
        var clock = Stopwatch.StartNew();

        while (!condition())
        {
            Assert.True(clock.Elapsed < timeout, $"Timed out waiting for {what}.");
            Thread.Sleep(200);
        }
    }

    /// <summary>Keeps a picture of the real window, when the script asks for one.</summary>
    private static void Picture(AutomationElement window, string name)
    {
        if (Environment.GetEnvironmentVariable(PicturesVariable) is not { } folder)
        {
            return;
        }

        var box = window.Current.BoundingRectangle;

        ScreenPicture.Save(new Rect(box.X, box.Y, box.Width, box.Height), Path.Combine(folder, name + ".png"));
    }

    /// <summary>What a portable update must leave alone. This only reads.</summary>
    private sealed record PcState(string Uninstall, string StartMenu, bool RightClickEntry, bool InstalledFolder, bool DataFolderFiles)
    {
        public static PcState Capture()
        {
            using var uninstall = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            using var entry = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell\Foldcraft.Customize");

            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return new PcState(
                string.Join('|', (uninstall?.GetSubKeyNames() ?? []).Order(StringComparer.Ordinal)),
                string.Join('|', Directory.EnumerateFiles(startMenu, "*.lnk", SearchOption.AllDirectories).Order(StringComparer.Ordinal)),
                entry is not null,
                Directory.Exists(Path.Combine(local, Core.Updates.ReleaseSite.PackageId)),
                Directory.Exists(Path.Combine(local, "Foldcraft")));
        }
    }
}

/// <summary>Copies part of the screen to a PNG.</summary>
internal static class ScreenPicture
{
    public static void Save(Rect area, string path)
    {
        using var bitmap = new System.Drawing.Bitmap((int)area.Width, (int)area.Height);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);

        graphics.CopyFromScreen((int)area.X, (int)area.Y, 0, 0, bitmap.Size);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
