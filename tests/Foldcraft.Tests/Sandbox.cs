using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Foldcraft.Core;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

/// <summary>
/// A unique directory under the system temp path, with all Foldcraft state injected into
/// it. The system temp path sits inside the real AppData, so the protected AppData and
/// profile roots are swapped for fake ones here, or every test folder would be refused.
/// </summary>
internal sealed class Sandbox : IDisposable
{
    public Sandbox(string? dataFolderName = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "foldcraft-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        FakeProfile = Path.Combine(Root, "fake-profile");
        FakeAppData = Path.Combine(FakeProfile, "AppData");
        Directory.CreateDirectory(FakeAppData);

        Paths = new FoldcraftPaths(Path.Combine(Root, dataFolderName ?? "data"));
        Directory.CreateDirectory(Paths.DataDirectory);

        var real = ProtectedFolders.Detect();

        Protected = real with { UserProfiles = [FakeProfile], AppData = [FakeAppData] };
        Safety = new FolderSafety(Paths, Protected);
        Journal = new ChangeJournal(Paths.JournalFile);
        Icons = new IconLibrary(Paths);
        Shell = new RecordingShell();
    }

    public string Root { get; }
    public string FakeProfile { get; }
    public string FakeAppData { get; }
    public FoldcraftPaths Paths { get; }
    public ProtectedFolders Protected { get; }
    public FolderSafety Safety { get; }
    public ChangeJournal Journal { get; }
    public IconLibrary Icons { get; }
    public RecordingShell Shell { get; }

    public static FolderIconSpec BlueStar { get; } = new()
    {
        Fill = FolderFill.Solid(IconColor.Parse("#2F6FED")),
        GlyphId = "star",
    };

    public static FolderIconSpec GreenHeart { get; } = new()
    {
        Fill = FolderFill.Gradient(IconColor.Parse("#16A34A"), IconColor.Parse("#A3E635"), 45f),
        GlyphId = "heart",
    };

    public FolderCustomizer Customizer(FileOperations? ops = null) => new(Safety, Journal, Icons, Shell, ops);

    public FolderRestorer Restorer(FileOperations? ops = null) => new(Safety, Journal, Shell, ops);

    public string NewFolder(string name = "target")
    {
        var path = Path.Combine(Root, "folders", name);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string IniPath(string folder) => Path.Combine(folder, "desktop.ini");

    /// <summary>Writes a desktop.ini with the given attributes.</summary>
    public static void PlantIni(string folder, byte[] bytes, FileAttributes attributes = FileAttributes.Hidden | FileAttributes.System)
    {
        var path = IniPath(folder);
        File.WriteAllBytes(path, bytes);
        File.SetAttributes(path, attributes);
    }

    public static string CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);

        var info = new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(info)!;
        Assert.True(process.WaitForExit(15_000), "mklink timed out");
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return link;
    }

    /// <summary>The 8.3 spelling of a path, or the path itself where 8.3 names are off.</summary>
    public static string ShortPath(string path)
    {
        var buffer = new StringBuilder(1024);
        var length = GetShortPathName(path, buffer, buffer.Capacity);
        return length == 0 ? path : buffer.ToString();
    }

    public void Dispose()
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        Assert.StartsWith(temp, Root, StringComparison.OrdinalIgnoreCase);

        // A superseded preview can still be reading a test image, and Explorer or the indexer
        // can hold a handle briefly, so give them a moment. Temp is the right place for the rest.
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                ForceDelete(new DirectoryInfo(Root));
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>Walks by hand so a junction is unlinked, never entered.</summary>
    private static void ForceDelete(DirectoryInfo directory)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = false };

        foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
        {
            entry.Attributes = FileAttributes.Normal;

            if (entry is DirectoryInfo child && (child.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                ForceDelete(child);
            }
            else if (entry is DirectoryInfo link)
            {
                link.Delete();
            }
            else
            {
                entry.Delete();
            }
        }

        directory.Attributes = FileAttributes.Normal;
        directory.Delete();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    private static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);
}

internal sealed class RecordingShell : IShellNotifier
{
    public List<string> Notified { get; } = [];

    public void FolderChanged(string folderPath) => Notified.Add(folderPath);
}

/// <summary>Throws at a chosen step, for chosen paths, to simulate disk or permission failures.</summary>
internal sealed class FaultyOperations : FileOperations
{
    public Func<string, bool> FailWrite { get; init; } = _ => false;
    public Func<string, bool> FailCommit { get; init; } = _ => false;
    public Func<string, FileAttributes, bool> FailSetAttributes { get; init; } = (_, _) => false;
    public Func<string, bool> FailDelete { get; init; } = _ => false;
    public Func<string, bool> FailSetTime { get; init; } = _ => false;

    public override void SetLastWriteTimeUtc(string path, DateTime lastWriteUtc)
    {
        if (FailSetTime(path))
        {
            throw new UnauthorizedAccessException("Simulated date failure.");
        }

        base.SetLastWriteTimeUtc(path, lastWriteUtc);
    }

    public override void WriteNewFile(string path, byte[] bytes)
    {
        if (FailWrite(path))
        {
            throw new IOException("Simulated write failure.");
        }

        base.WriteNewFile(path, bytes);
    }

    public override void CommitReplace(string tempPath, string destinationPath, bool destinationExists)
    {
        if (FailCommit(destinationPath))
        {
            throw new IOException("Simulated commit failure.");
        }

        base.CommitReplace(tempPath, destinationPath, destinationExists);
    }

    public override void SetAttributes(string path, FileAttributes attributes)
    {
        if (FailSetAttributes(path, attributes))
        {
            throw new UnauthorizedAccessException("Simulated attribute failure.");
        }

        base.SetAttributes(path, attributes);
    }

    public override void DeleteFile(string path)
    {
        if (FailDelete(path))
        {
            throw new IOException("Simulated delete failure.");
        }

        base.DeleteFile(path);
    }
}
