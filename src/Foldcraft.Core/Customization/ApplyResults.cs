using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Customization;

public enum ApplyOutcome
{
    /// <summary>The folder now shows the icon, and the change is recorded for undo.</summary>
    Applied,

    /// <summary>The folder already showed this icon, so nothing was changed.</summary>
    Unchanged,

    /// <summary>FolderSafety refused. Nothing was touched.</summary>
    Refused,

    /// <summary>Something failed. Any partial work was rolled back; the folder is as it was.</summary>
    Failed,

    /// <summary>
    /// The change failed and rolling it back failed too. It stays active in the journal so a
    /// restore can finish the job later.
    /// </summary>
    FailedRollbackIncomplete,

    /// <summary>The batch was canceled before this folder was reached.</summary>
    Canceled,
}

/// <summary>A folder and the look it should get.</summary>
public sealed record FolderLook(string Folder, Icons.FolderIconSpec Spec);

public sealed record FolderApplyResult
{
    public required string RequestedPath { get; init; }
    public string? ResolvedPath { get; init; }
    public required ApplyOutcome Outcome { get; init; }
    public FolderRefusal? Refusal { get; init; }
    public string? Message { get; init; }

    /// <summary>True when a new folder couldn't be made because something already has its name.</summary>
    public bool NameTaken { get; init; }
}

public sealed record BatchApplyResult(Guid BatchId, IReadOnlyList<FolderApplyResult> Results)
{
    public int AppliedCount => Results.Count(r => r.Outcome == ApplyOutcome.Applied);
    public bool AllSucceeded => Results.All(r => r.Outcome is ApplyOutcome.Applied or ApplyOutcome.Unchanged);
}
