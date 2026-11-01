namespace Foldcraft.App.Services.Updates;

/// <summary>How to tell what kind of start this is, before anything else has run.</summary>
public static class UpdateHooks
{
    private static readonly string[] HookPrefixes = ["--veloapp-", "--squirrel-"];

    /// <summary>
    /// True when the installer started this process to do one small job. Such a run must end
    /// without showing anything. Only the first argument counts, so a right-click start, which
    /// always leads with --customize, can never be mistaken for one.
    /// </summary>
    public static bool IsHookRun(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Count > 0 && HookPrefixes.Any(prefix => args[0].StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when this app sits under the updater, which is the layout the installer makes.</summary>
    /// <param name="appDirectory">The folder holding Foldcraft.exe.</param>
    public static bool IsPackaged(string appDirectory)
    {
        ArgumentNullException.ThrowIfNull(appDirectory);

        return File.Exists(Path.Combine(appDirectory, "..", "Update.exe"));
    }

    /// <summary>The folder the installer owns, one above the app, which the updater works on as a whole.</summary>
    /// <param name="appDirectory">The folder holding Foldcraft.exe.</param>
    public static string InstallFolder(string appDirectory)
    {
        ArgumentNullException.ThrowIfNull(appDirectory);

        return Path.GetFullPath(Path.Combine(appDirectory, ".."));
    }

    /// <summary>True when a file sits anywhere inside a folder. A path that isn't known counts as outside.</summary>
    public static bool IsInside(string folder, string? path)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return path is not null && path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
