using System.ComponentModel;
using Foldcraft.Core.Infrastructure;

namespace Foldcraft.Core.Safety;

/// <summary>
/// Decides whether a folder may be customized. Every path, however it is spelled, is
/// reduced to its canonical form first, and a refusal is the default for anything that
/// cannot be positively understood.
/// </summary>
public sealed class FolderSafety
{
    /// <summary>Volume-level folders that belong to Windows, matched directly under a drive root.</summary>
    private static readonly HashSet<string> VolumeSystemFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "Recovery", "$WinREAgent", "$Windows.~BT", "$Windows.~WS", "Config.Msi",
    };

    private readonly bool _allowNetworkPaths;
    private readonly List<ProtectedRoot> _roots = [];

    /// <param name="paths">Where Foldcraft keeps its data.</param>
    /// <param name="protectedFolders">The folders to refuse. Tests swap these.</param>
    /// <param name="allowNetworkPaths">Whether a network location can be used.</param>
    /// <param name="elevated">
    /// True when running as administrator. Folders inside Program Files and ProgramData are then
    /// allowed, because only those rights can change them. The two folders themselves stay refused,
    /// and so does everything else. Other accounts' folders are added, because Windows no longer
    /// keeps those rights out of them.
    /// </param>
    public FolderSafety(FoldcraftPaths paths, ProtectedFolders? protectedFolders = null, bool allowNetworkPaths = false, bool elevated = false)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var folders = protectedFolders ?? ProtectedFolders.Detect();
        _allowNetworkPaths = allowNetworkPaths;

        AddRoots(folders.Windows, FolderRefusal.WindowsDirectory, subtree: true, "Windows folder");
        AddRoots(folders.ProgramFiles, FolderRefusal.ProgramFiles, subtree: !elevated, "Program Files");
        AddRoots(folders.ProgramData, FolderRefusal.ProgramData, subtree: !elevated, "ProgramData");
        AddRoots(folders.AppData, FolderRefusal.AppData, subtree: true, "AppData");
        AddRoots(folders.UserProfiles, FolderRefusal.UserProfileRoot, subtree: false, "user profile folder");
        AddRoots([paths.DataDirectory], FolderRefusal.FoldcraftData, subtree: true, "Foldcraft data folder");

        if (elevated)
        {
            AddRoots(folders.OtherProfiles, FolderRefusal.OtherAccount, subtree: true, "folders of another account");
        }
    }

    /// <summary>Finds an existing folder's true path. Tests swap this to reach routes they may not take.</summary>
    internal Func<string, string> ResolveFinalPath { get; init; } = PathResolver.ResolveFinalPath;

    /// <summary>Reads a reparse tag. Tests swap this, because a real sync provider can't be set up in temp.</summary>
    internal Func<string, uint?> ReadReparseTag { get; init; } = ReparseTags.Read;

    /// <summary>Checks a path. This never throws for bad input; the reason is in the result.</summary>
    public FolderSafetyResult Check(string path)
    {
        var normalized = PathResolver.Normalize(path, out var refusal, out var reason);

        if (normalized is null)
        {
            return Refused(path, null, refusal!.Value, reason!);
        }

        if (PathResolver.IsNetworkPath(normalized))
        {
            if (!_allowNetworkPaths)
            {
                return Refused(path, normalized, FolderRefusal.NetworkPath, "Network folders are not customized.");
            }

            if (IsAdministrativeShare(normalized))
            {
                return Refused(path, normalized, FolderRefusal.NetworkPath, "Administrative shares are never customized.");
            }
        }

        if (PathResolver.IsRoot(normalized))
        {
            return Refused(path, normalized, FolderRefusal.DriveRoot, "Drive roots cannot be customized.");
        }

        // A name that loses its ending is a different folder, so a verdict on the trimmed path
        // would answer the wrong question. Protected places still report as themselves.
        if (PathResolver.HasTrimmedName(path))
        {
            return CheckProtected(path, normalized, normalized) ?? RefusedAsTrimmed(path);
        }

        var root = Path.GetPathRoot(normalized)!;

        if (!PathResolver.IsNetworkPath(normalized))
        {
            var driveType = NativeMethods.GetDriveType(root);

            if (driveType == NativeMethods.DRIVE_NO_ROOT_DIR)
            {
                return Refused(path, normalized, FolderRefusal.NotFound, "The drive does not exist.");
            }

            if (driveType == NativeMethods.DRIVE_REMOTE && !_allowNetworkPaths)
            {
                return Refused(path, normalized, FolderRefusal.NetworkPath, "Network drives are not customized.");
            }
        }

        var walk = WalkFromRoot(path, normalized, root);

        if (walk is not null)
        {
            return walk;
        }

        string finalPath;

        try
        {
            finalPath = ResolveFinalPath(normalized);
        }
        catch (Win32Exception ex)
        {
            return Refused(path, normalized, FolderRefusal.Unresolvable, $"Windows could not resolve this folder: {ex.Message}");
        }

        // A short name or a SUBST drive can lead somewhere the typed text never showed.
        if (PathResolver.HasTrimmedName(finalPath))
        {
            return RefusedAsTrimmed(path);
        }

        if (IsVolumeSystemFolder(finalPath))
        {
            return Refused(path, normalized, FolderRefusal.SystemVolumeFolder, "This folder belongs to Windows.");
        }

        if (PathResolver.IsNetworkPath(finalPath) && !_allowNetworkPaths)
        {
            return Refused(path, normalized, FolderRefusal.NetworkPath, "This path leads to a network location.");
        }

        if (PathResolver.IsRoot(finalPath))
        {
            return Refused(path, normalized, FolderRefusal.DriveRoot, "This path leads to a drive root.");
        }

        var blocked = CheckProtected(path, normalized, finalPath);

        return blocked ?? new FolderSafetyResult
        {
            RequestedPath = path,
            NormalizedPath = normalized,
            ResolvedPath = finalPath,
        };
    }

    /// <summary>
    /// Checks a place to make new folders in. This is <see cref="Check"/> with one difference: a
    /// whole drive can't be customized, but a new folder can sit at the top of one, as D:\Games
    /// does. Each new folder is still checked by name once it exists.
    /// </summary>
    public FolderSafetyResult CheckDestination(string path)
    {
        var verdict = Check(path);

        if (verdict.Refusal != FolderRefusal.DriveRoot || verdict.NormalizedPath is not { } root)
        {
            return verdict;
        }

        if (PathResolver.IsNetworkPath(root))
        {
            return Refused(path, root, FolderRefusal.NetworkPath, "Network folders are not customized.");
        }

        var driveType = NativeMethods.GetDriveType(root);

        if (driveType == NativeMethods.DRIVE_NO_ROOT_DIR)
        {
            return Refused(path, root, FolderRefusal.NotFound, "The drive does not exist.");
        }

        if (driveType == NativeMethods.DRIVE_REMOTE && !_allowNetworkPaths)
        {
            return Refused(path, root, FolderRefusal.NetworkPath, "Network drives are not customized.");
        }

        string finalPath;

        try
        {
            finalPath = ResolveFinalPath(root);
        }
        catch (Win32Exception ex)
        {
            return Refused(path, root, FolderRefusal.Unresolvable, $"Windows could not resolve this folder: {ex.Message}");
        }

        // A SUBST drive is a folder wearing a drive letter, so judge the folder.
        if (!PathResolver.IsRoot(finalPath))
        {
            return Check(finalPath);
        }

        return new FolderSafetyResult { RequestedPath = path, NormalizedPath = root, ResolvedPath = finalPath };
    }

    /// <summary>
    /// Inspects every component from the root down, without following anything, so a junction
    /// anywhere in the path is caught rather than only one at the end. A cloud-synced folder is
    /// the one kind of reparse point allowed through, and only once its tag has been read.
    /// </summary>
    private FolderSafetyResult? WalkFromRoot(string requested, string normalized, string root)
    {
        var segments = SegmentsBelowRoot(normalized, root);

        if (IsVolumeSystemFolder(normalized))
        {
            return Refused(requested, normalized, FolderRefusal.SystemVolumeFolder, "This folder belongs to Windows.");
        }

        var current = root;

        for (var i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            FileAttributes attributes;

            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return Refused(requested, normalized, FolderRefusal.NotFound, "The folder does not exist.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Refused(requested, normalized, FolderRefusal.Unresolvable, $"The folder could not be inspected: {ex.Message}");
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0 && !IsCloudFolder(current, attributes))
            {
                return Refused(requested, normalized, FolderRefusal.ReparsePoint,
                    "This path goes through a junction, symbolic link or other reparse point.");
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                return i == segments.Length - 1
                    ? Refused(requested, normalized, FolderRefusal.NotADirectory, "This is a file, not a folder.")
                    : Refused(requested, normalized, FolderRefusal.NotFound, "The folder does not exist.");
            }
        }

        return null;
    }

    private bool IsCloudFolder(string path, FileAttributes attributes) =>
        (attributes & FileAttributes.Directory) != 0 && ReadReparseTag(path) is { } tag && ReparseTags.IsCloud(tag);

    private static string[] SegmentsBelowRoot(string path, string root) =>
        path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsVolumeSystemFolder(string path)
    {
        var segments = SegmentsBelowRoot(path, Path.GetPathRoot(path)!);
        return segments.Length > 0 && VolumeSystemFolders.Contains(segments[0]);
    }

    private FolderSafetyResult? CheckProtected(string requested, string normalized, string finalPath)
    {
        // Direct matches go first, so AppData is reported as AppData and not as a parent of Roaming.
        foreach (var root in _roots)
        {
            foreach (var spelling in root.Spellings)
            {
                var matched = root.Subtree
                    ? PathResolver.IsSameOrUnder(finalPath, spelling) || PathResolver.IsSameOrUnder(normalized, spelling)
                    : finalPath.Equals(spelling, StringComparison.OrdinalIgnoreCase) || normalized.Equals(spelling, StringComparison.OrdinalIgnoreCase);

                if (matched)
                {
                    return Refused(requested, normalized, root.Refusal, $"This is inside the {root.Label}, which Foldcraft never changes.");
                }
            }
        }

        foreach (var root in _roots)
        {
            foreach (var spelling in root.Spellings)
            {
                if (PathResolver.IsStrictlyUnder(spelling, finalPath) || PathResolver.IsStrictlyUnder(spelling, normalized))
                {
                    return Refused(requested, normalized, FolderRefusal.ContainsProtectedFolder,
                        $"This folder contains the {root.Label}, which Foldcraft never changes.");
                }
            }
        }

        return null;
    }

    private void AddRoots(IEnumerable<string> paths, FolderRefusal refusal, bool subtree, string label)
    {
        foreach (var path in paths)
        {
            var normalized = PathResolver.Normalize(path, out _, out _);

            if (normalized is null)
            {
                continue;
            }

            var spellings = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalized };

            try
            {
                spellings.Add(PathResolver.ResolveFinalPath(normalized));
            }
            catch (Win32Exception)
            {
                // A folder that does not exist has only its normalized spelling.
            }

            _roots.Add(new ProtectedRoot(spellings.ToArray(), refusal, subtree, label));
        }
    }

    private static bool IsAdministrativeShare(string uncPath)
    {
        var root = Path.GetPathRoot(uncPath);
        return root is not null && root.TrimEnd('\\').EndsWith('$');
    }

    /// <summary>No normalized path: the trimmed spelling names another folder, so it must not identify this one.</summary>
    private static FolderSafetyResult RefusedAsTrimmed(string requested) => Refused(
        requested,
        null,
        FolderRefusal.UnsupportedPathForm,
        "This name ends in a dot or a space, which Windows would turn into a different folder's name.");

    private static FolderSafetyResult Refused(string requested, string? normalized, FolderRefusal refusal, string reason) => new()
    {
        RequestedPath = requested,
        NormalizedPath = normalized,
        Refusal = refusal,
        Reason = reason,
    };

    private sealed record ProtectedRoot(string[] Spellings, FolderRefusal Refusal, bool Subtree, string Label);
}
