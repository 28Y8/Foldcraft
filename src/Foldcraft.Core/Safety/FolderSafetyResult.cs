namespace Foldcraft.Core.Safety;

/// <summary>Why a folder may not be customized.</summary>
public enum FolderRefusal
{
    InvalidPath,
    UnsupportedPathForm,
    NotFound,
    NotADirectory,
    DriveRoot,
    NetworkPath,
    ReparsePoint,
    WindowsDirectory,
    ProgramFiles,
    ProgramData,
    UserProfileRoot,
    AppData,
    FoldcraftData,
    ContainsProtectedFolder,
    SystemVolumeFolder,
    Unresolvable,
}

/// <summary>
/// The verdict on a path. Callers must act on <see cref="ResolvedPath"/>, never on the
/// text they passed in: the resolved path is the one that was actually checked.
/// </summary>
public sealed record FolderSafetyResult
{
    public required string RequestedPath { get; init; }

    /// <summary>True when the folder may be customized.</summary>
    public bool IsAllowed => Refusal is null;

    /// <summary>The canonical on-disk path. Set only when allowed.</summary>
    public string? ResolvedPath { get; init; }

    /// <summary>
    /// The lexically normalized path, set whenever the text could be parsed. This identifies
    /// a folder that no longer exists, so its journal entries can still be found.
    /// </summary>
    public string? NormalizedPath { get; init; }

    public FolderRefusal? Refusal { get; init; }

    /// <summary>A readable explanation, suitable for showing to the user.</summary>
    public string? Reason { get; init; }
}
