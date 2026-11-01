using System.Globalization;
using System.Text.RegularExpressions;
using Foldcraft.App.Services.Updates;
using Foldcraft.Core.Apps;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Rules;
using Foldcraft.Core.Safety;
using Foldcraft.Core.Shell;

namespace Foldcraft.App.Services;

/// <summary>
/// Turns Core's precise but technical results into friendly sentences. Everything the user
/// reads about a folder's fate comes through here, so nothing from Core reaches the UI
/// unfiltered. Raw details go to the log instead.
/// </summary>
public static partial class PlainEnglish
{
    /// <summary>Shown wherever the app works with other companies' software.</summary>
    public const string AppsDisclaimer =
        "Foldcraft is not affiliated with these companies. Icons are read from software on your PC and are never uploaded or redistributed.";

    /// <summary>What putting things back means for a folder Foldcraft made.</summary>
    public const string CreatedFoldersGo =
        "Folders Foldcraft made go too, unless something was added to them. Those stay, without their look.";

    /// <summary>Shown when the right-click entry starts a copy of Foldcraft that isn't this one.</summary>
    public const string RightClickMoved =
        "Foldcraft has moved, so the right-click entry still points to where it used to be. Update it to make it work again.";

    /// <summary>Shown when the right-click entry was on but isn't there any more, as after an uninstall.</summary>
    public const string RightClickGone =
        "The right-click entry isn't there any more, so this switch is off. Turn it on again if you'd like it back.";

    /// <summary>What an update check does and doesn't do. This has to stay exactly true.</summary>
    public const string UpdatePrivacy =
        "Foldcraft contacts GitHub only to check for and download updates. It sends no personal data and nothing " +
        "about your folders. GitHub sees your IP address, as any website does. You can turn the check off in " +
        "Settings. Nothing downloads or installs until you choose to update.";

    /// <summary>Shown in place of the update controls in a copy that can't update itself.</summary>
    public const string UpdatesNeedInstaller =
        "Updates aren't available in this copy of Foldcraft, because the installer didn't put it here. " +
        "You can get new versions from the Releases page.";

    public const string UpdateDownloadCanceled = "The download was canceled. " + StillOnYourVersion;

    public const string UpdateWaitsForWork = "Foldcraft is busy, so it didn't restart. Try again when the current work finishes.";

    /// <summary>The updater stops every copy running from the install folder, so another one has to close first.</summary>
    public const string UpdateWaitsForOtherCopy = "Another Foldcraft window is open, so this one didn't restart. Close that window and try again.";

    public const string UpdateInstallFailed = "The update couldn't be started, so Foldcraft kept running on the version you have.";

    /// <summary>Shown in Settings when a bad settings file means the opt-out can't be known.</summary>
    public const string UpdateCheckSkipped =
        "Foldcraft couldn't read its settings file, so it didn't check for updates by itself. Changing a setting starts a new file and keeps the old one.";

    /// <summary>
    /// What's safe to say after a download stops. Not "nothing has changed": the updater clears its
    /// packages folder even then, which can take an earlier download or the package deltas start from.
    /// </summary>
    private const string StillOnYourVersion = "Foldcraft is still on the version you have.";

    /// <summary>What stays after an uninstall, on the Settings screen.</summary>
    public const string WhatUninstallLeaves =
        "Uninstalling Foldcraft from Windows removes the app and its right-click entry. Your customized folders " +
        "keep their look and Foldcraft's data folder stays, because those folders use the icons kept there. To " +
        "put every folder back as well, press Remove All Changes before you uninstall.";

    /// <summary>Explorer and the older Windows APIs it relies on stop at this many characters.</summary>
    private const int ShellPathLimit = 260;

    private const string CouldNotChange =
        "Foldcraft couldn't change this folder. It may be read-only or in use.";

    private const string CouldNotRestore =
        "Foldcraft couldn't put this folder back. It may be read-only or in use. You can try again.";

    public static string Folders(int count) => count == 1 ? "1 folder" : $"{count} folders";

    /// <summary>Why a folder can't be used. Every <see cref="FolderRefusal"/> has its own wording.</summary>
    public static string Refusal(FolderRefusal refusal) => refusal switch
    {
        FolderRefusal.InvalidPath => "This isn't a valid folder location.",
        FolderRefusal.UnsupportedPathForm => "Foldcraft can't work with this kind of location.",
        FolderRefusal.NotFound => "This folder can't be found. It may have been moved or deleted.",
        FolderRefusal.NotADirectory => "This is a file, not a folder.",
        FolderRefusal.DriveRoot => "A whole drive can't be customized. Choose a folder on it instead.",
        FolderRefusal.NetworkPath => "Folders on a network aren't supported.",
        FolderRefusal.ReparsePoint => "This is a link to another location, so Foldcraft leaves it alone.",
        FolderRefusal.WindowsDirectory => "This is part of Windows, so Foldcraft leaves it alone.",
        FolderRefusal.ProgramFiles => "This is where apps are installed, so Foldcraft leaves it alone.",
        FolderRefusal.ProgramData => "Apps keep shared data here, so Foldcraft leaves it alone.",
        FolderRefusal.UserProfileRoot =>
            "This is your main user folder, so Foldcraft leaves it alone. Folders inside it, like Documents, are fine.",
        FolderRefusal.AppData => "Apps keep their private files here, so Foldcraft leaves it alone.",
        FolderRefusal.FoldcraftData => "Foldcraft keeps its own files here, so it leaves this folder alone.",
        FolderRefusal.ContainsProtectedFolder =>
            "This folder contains important Windows or app folders, so Foldcraft leaves it alone.",
        FolderRefusal.SystemVolumeFolder => "This folder belongs to Windows, so Foldcraft leaves it alone.",
        FolderRefusal.Unresolvable => "Windows couldn't confirm where this folder really is, so Foldcraft left it alone.",
        _ => "Foldcraft can't safely change this folder.",
    };

    /// <summary>What a folder row should say after an apply.</summary>
    public static string ApplyResult(FolderApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            ApplyOutcome.Applied => TooLongToShow(result)
                ? "Saved, but Windows can't show this look at a location this long."
                : "Done.",
            ApplyOutcome.Unchanged => TooLongToShow(result)
                ? "Already saved, but Windows can't show this look at a location this long."
                : "Already has this look.",
            ApplyOutcome.Refused => result.Refusal is { } refusal ? Refusal(refusal) : CouldNotChange,
            ApplyOutcome.FailedRollbackIncomplete =>
                "Something went wrong and this folder may be half finished. Restore it from the History screen.",
            ApplyOutcome.Canceled => "Skipped because you canceled.",
            _ => Failure(result.Message, CouldNotChange),
        };
    }

    /// <summary>What a folder should say after an undo or restore.</summary>
    public static string RestoreResult(FolderRestoreResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            RestoreOutcome.Restored => Failure(result.Message, "Put back."),
            RestoreOutcome.AlreadyRestored => Failure(result.Message, "Already back as it was."),
            RestoreOutcome.NothingToRestore => "Nothing to put back.",
            RestoreOutcome.Conflict => "Something else changed this folder after Foldcraft did, so it was left alone.",
            RestoreOutcome.FolderMissing => "This folder can't be found. You can try again if it comes back.",
            RestoreOutcome.Refused => result.Refusal is { } refusal ? Refusal(refusal) : CouldNotRestore,
            RestoreOutcome.Forgotten => "Kept as it is. Foldcraft no longer tracks this change.",
            RestoreOutcome.DifferentFolder => Failure(result.Message, "A different folder is at this location now, so it was left alone."),
            RestoreOutcome.Relocated => "Found at its new location.",
            _ => Failure(result.Message, CouldNotRestore),
        };
    }

    /// <summary>What a folder should say after a repair.</summary>
    public static string RepairResult(FolderRepairResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            RepairOutcome.Repaired => "Icon repaired.",
            RepairOutcome.NotRepairable => Failure(result.Message, "Foldcraft can't repair this folder's icon."),
            _ => Failure(result.Message, "Foldcraft couldn't repair this folder's icon. It was left as it was."),
        };
    }

    /// <summary>"All icons look fine" is only ever said of every folder, each one looked at and found fine.</summary>
    public static string RepairSummary(RepairReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var repaired = report.Count(r => r.Outcome == RepairOutcome.Repaired);
        var others = report.Count - repaired;
        var parts = new List<string>();

        if (repaired > 0)
        {
            parts.Add(repaired == 1 ? "Repaired 1 icon." : $"Repaired {repaired} icons.");
        }

        if (others > 0)
        {
            parts.Add($"{Folders(others)} couldn't be repaired.");
        }

        if (report.LeftAlone > 0)
        {
            parts.Add(report.LeftAlone == 1
                ? "1 folder was skipped, because it's missing or no longer as Foldcraft left it."
                : $"{report.LeftAlone} folders were skipped, because they're missing or no longer as Foldcraft left them.");
        }

        if (report.WasCanceled)
        {
            parts.Add(report.NotLookedAt == 1
                ? "You stopped the check, so 1 folder wasn't looked at."
                : $"You stopped the check, so {report.NotLookedAt} folders weren't looked at.");
        }

        if (parts.Count > 0)
        {
            return string.Join(' ', parts);
        }

        return report.Healthy > 0 ? "All icons look fine." : "There are no folders to check.";
    }

    /// <summary>What a line in a rules preview should say.</summary>
    public static string RulePreview(RulePreviewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Status switch
        {
            RulePreviewStatus.WillChange => $"Will get the {row.Theme?.Name} look.",
            RulePreviewStatus.AlreadyHasLook => $"Already has the {row.Theme?.Name} look.",
            RulePreviewStatus.Refused => row.Refusal is { } refusal ? Refusal(refusal) : "Foldcraft can't safely change this folder.",
            RulePreviewStatus.Hidden => "Skipped, because hidden and system folders are left alone.",
            RulePreviewStatus.Unreadable => "Skipped, because Windows wouldn't let Foldcraft look inside.",
            RulePreviewStatus.ThemeMissing => "Skipped, because the theme this rule uses no longer exists.",
            _ => "Skipped.",
        };
    }

    /// <summary>What a finished preview found, in a sentence or two.</summary>
    public static string RulePreviewSummary(RulePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var changes = preview.Changes.Count();
        var skipped = preview.Rows.Count(row => row.Status is not (RulePreviewStatus.WillChange or RulePreviewStatus.AlreadyHasLook));
        var parts = new List<string>
        {
            $"Looked at {Folders(preview.FoldersScanned)}.",
            changes == 0 ? "Nothing would change." : $"{Folders(changes)} would change.",
        };

        if (skipped > 0)
        {
            parts.Add($"{skipped} skipped.");
        }

        if (preview.HitLimit)
        {
            parts.Add($"Foldcraft stopped at {RuleScanner.MaxFolders:N0} folders, so some weren't looked at.");
        }

        if (preview.WasCanceled)
        {
            parts.Add("You stopped early, so some folders weren't looked at.");
        }

        return string.Join(' ', parts);
    }

    /// <summary>A rule in words, the way someone would say it.</summary>
    public static string RuleSummary(FolderRule rule, string themeName)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var when = rule.Condition switch
        {
            RuleCondition.NameContains => $"If a folder's name contains \"{rule.Pattern}\"",
            RuleCondition.NameEquals => $"If a folder's name is \"{rule.Pattern}\"",
            RuleCondition.NameStartsWith => $"If a folder's name starts with \"{rule.Pattern}\"",
            RuleCondition.NameWildcard => $"If a folder's name fits \"{rule.Pattern}\"",
            _ => rule.Pattern.StartsWith('.') ? $"If a folder mostly holds {rule.Pattern} files" : $"If a folder mostly holds {rule.Pattern}",
        };

        return $"{when}, use {themeName}";
    }

    /// <summary>What's left after everything Foldcraft did has been taken back, one point per line.</summary>
    /// <param name="restored">What became of each folder and how many history records couldn't be read.</param>
    /// <param name="keptFolders">Folders no longer tracked that still use one of Foldcraft's icons.</param>
    /// <param name="rightClick">What became of the right-click entry. Null when none was on record.</param>
    /// <param name="dataDirectory">Where Foldcraft keeps its own files.</param>
    public static IReadOnlyList<string> WhatRemains(
        RestoreReport restored,
        int keptFolders,
        RightClickOutcome? rightClick,
        string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(restored);

        var lines = new List<string>();
        var results = restored.Results;
        var unreadable = restored.CorruptJournalRecords;
        var stuck = results.Count(r => r.Outcome is
            RestoreOutcome.Conflict or RestoreOutcome.FolderMissing or RestoreOutcome.DifferentFolder or
            RestoreOutcome.Refused or RestoreOutcome.Failed);

        lines.Add(results.Count == 0 && unreadable > 0 ? "Foldcraft found nothing it could read to put back." : RestoreSummary(results));

        if (stuck > 0)
        {
            lines.Add(stuck == 1
                ? "1 folder still has its Foldcraft look. History lists it, so you can try again."
                : $"{stuck} folders still have their Foldcraft look. History lists them, so you can try again.");
        }

        if (keptFolders > 0)
        {
            lines.Add(keptFolders == 1
                ? "1 folder you chose to keep as it is still uses a Foldcraft icon."
                : $"{keptFolders} folders you chose to keep as they are still use Foldcraft icons.");
        }

        if (unreadable > 0)
        {
            lines.Add(unreadable == 1
                ? "1 history record couldn't be read, so a change it describes may still be in place."
                : $"{unreadable} history records couldn't be read, so changes they describe may still be in place.");
        }

        lines.Add(rightClick switch
        {
            RightClickOutcome.Removed => "The right-click entry was removed.",
            RightClickOutcome.LeftAlone => "The right-click entry was changed by something else, so it was left in place.",
            RightClickOutcome.Failed => "The right-click entry couldn't be removed. You can try again from this screen.",
            RightClickOutcome.NothingThere => "There was no right-click entry to remove.",
            _ => "Foldcraft has no right-click entry on record, so it removed nothing.",
        });

        lines.Add($"Foldcraft's own files are still in {dataDirectory}: the history, your saved themes, your rules, any icons still in use and pictures read from your apps.");

        // An unreadable record comes first: it can't be fixed by putting folders back.
        lines.Add(
            unreadable > 0 ? "Keep that folder, because it may hold the only record of how a folder looked before."
            : stuck > 0 ? "Keep that folder until those folders are put back, because they use its icons."
            : keptFolders > 0 ? "Keep that folder for as long as you want those folders to keep their look, because they use its icons."
            : "Nothing on record depends on that folder now, so you can delete it, along with the app itself.");

        return lines;
    }

    public static string NoUpdateFound(string currentVersion) =>
        $"No newer version was found. You have version {currentVersion}.";

    public static string UpdateCheckFailed(UpdateFailure failure) => failure switch
    {
        UpdateFailure.CouldNotConnect => "Foldcraft couldn't reach GitHub. Check your internet connection and try again.",
        UpdateFailure.ServiceRefused => "GitHub didn't answer as expected. It may be limiting requests, so try again later.",
        _ => "Foldcraft couldn't check for updates. Try again later.",
    };

    public static string UpdateDownloadFailed(UpdateFailure failure) => failure switch
    {
        UpdateFailure.CouldNotConnect => $"The download stopped because Foldcraft couldn't reach GitHub. {StillOnYourVersion}",
        UpdateFailure.ServiceRefused => $"GitHub didn't provide the update. {StillOnYourVersion} Try again later.",
        UpdateFailure.DamagedDownload => $"The download didn't match what the release recorded, so it wasn't used. {StillOnYourVersion}",
        UpdateFailure.CouldNotSave => $"Foldcraft couldn't save the download. {StillOnYourVersion} Check that your drive has free space.",
        _ => $"The update couldn't be downloaded. {StillOnYourVersion}",
    };

    public static string UpdateRestartMessage(string version) =>
        $"Foldcraft will close, install version {version} and open again. Your customized folders, history, " +
        "themes and settings stay as they are.";

    /// <summary>What the right-click setting should say after a change.</summary>
    public static string? RightClickMessage(RightClickOutcome outcome) => outcome switch
    {
        RightClickOutcome.LeftAlone =>
            "Something else is using that spot in the right-click menu, or changed it, so Foldcraft left it alone.",
        RightClickOutcome.CannotQuote =>
            "Foldcraft is in a location that can't be added to the menu safely. Move it to another folder and try again.",
        RightClickOutcome.Failed => "Windows didn't allow that change. Nothing else was touched.",
        _ => null,
    };

    /// <summary>Where an app was found, as shown on its tile and in the filter.</summary>
    public static string AppSourceName(AppSource source) => source switch
    {
        AppSource.Steam => "Steam",
        AppSource.Roblox => "Roblox",
        AppSource.EpicGames => "Epic Games",
        _ => "Start Menu",
    };

    /// <summary>What an item should say after the look was made.</summary>
    public static string AppFolderResult(AppFolderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var main = result.Outcome switch
        {
            AppFolderOutcome.Created => MayNotShowInExplorer(result.FolderPath ?? string.Empty)
                ? "Created, but Windows can't show this look at a location this long."
                : "Created.",
            AppFolderOutcome.Themed => "Done.",
            AppFolderOutcome.NameTaken => "Skipped, because something already has this name.",
            AppFolderOutcome.Refused => result.Refusal is { } refusal ? Refusal(refusal) : CouldNotChange,
            AppFolderOutcome.Canceled => "Skipped because you canceled.",
            AppFolderOutcome.LeftUnfinished => "Something went wrong and this folder may be half made. Undo it from the History screen.",
            _ => Failure(result.Message, "Foldcraft couldn't make this folder. Nothing was left behind."),
        };

        return result.InstallFolder is { } install ? $"{main} Install folder: {ApplyResult(install)}" : main;
    }

    public static string AppFolderSummary(IReadOnlyCollection<AppFolderResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var created = results.Count(r => r.Outcome == AppFolderOutcome.Created);
        var taken = results.Count(r => r.Outcome == AppFolderOutcome.NameTaken);
        var canceled = results.Count(r => r.Outcome == AppFolderOutcome.Canceled);
        var problems = results.Count(r => r.Outcome is AppFolderOutcome.Refused or AppFolderOutcome.Failed or AppFolderOutcome.LeftUnfinished);
        var parts = new List<string>();

        if (created > 0)
        {
            parts.Add($"Created {Folders(created)}. You can undo this from History.");
        }

        if (taken > 0)
        {
            parts.Add($"{taken} skipped because the name was taken.");
        }

        if (problems > 0)
        {
            parts.Add($"{problems} couldn't be made.");
        }

        if (canceled > 0)
        {
            parts.Add($"{canceled} skipped.");
        }

        return parts.Count == 0 ? "Nothing to do." : string.Join(' ', parts);
    }

    /// <summary>What a finished scan found, in a sentence or two.</summary>
    /// <param name="report">What the scan found.</param>
    /// <param name="ready">How many items had their icon read and can be used.</param>
    /// <param name="unreadable">How many icons were tried and couldn't be read.</param>
    /// <param name="canceled">True when the user stopped the scan, at any stage.</param>
    public static string AppScanSummary(AppScanReport report, int ready, int unreadable = 0, bool canceled = false)
    {
        ArgumentNullException.ThrowIfNull(report);

        var parts = new List<string>
        {
            report.Apps.Count switch
            {
                0 => "No apps or games found.",
                1 => "Found 1 app or game.",
                var count => $"Found {count} apps and games.",
            },
        };

        var notRead = report.Apps.Count - ready - unreadable;

        if (report.WasCanceled)
        {
            parts.Add("You stopped the scan, so some may be missing.");
        }
        else if (canceled && notRead > 0)
        {
            parts.Add(notRead == 1
                ? "You stopped the scan before 1 icon was read. Scan again to use it."
                : $"You stopped the scan before {notRead} icons were read. Scan again to use them.");
        }

        if (unreadable > 0)
        {
            parts.Add(unreadable == 1 ? "1 icon couldn't be read." : $"{unreadable} icons couldn't be read.");
        }

        if (report.HitLimit)
        {
            parts.Add($"Foldcraft shows the first {AppDiscovery.MaxApps:N0}.");
        }

        if (report.Problems.Count > 0)
        {
            parts.Add(report.Problems.Count == 1
                ? $"{report.Problems[0].Provider} couldn't be read, but everything else was."
                : $"{report.Problems.Count} sources couldn't be read, but everything else was.");
        }

        return string.Join(' ', parts);
    }

    public static string ApplySummary(IReadOnlyCollection<FolderApplyResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var changed = results.Count(r => r.Outcome == ApplyOutcome.Applied);
        var same = results.Count(r => r.Outcome == ApplyOutcome.Unchanged);
        var canceled = results.Count(r => r.Outcome == ApplyOutcome.Canceled);
        var problems = results.Count - changed - same - canceled;
        var tooLong = results.Count(r => r.Outcome == ApplyOutcome.Applied && TooLongToShow(r));

        var parts = new List<string>();

        if (changed > 0)
        {
            parts.Add($"Changed {Folders(changed)}.");
        }

        if (same > 0)
        {
            parts.Add($"{same} already had this look.");
        }

        if (problems > 0)
        {
            parts.Add($"{problems} couldn't be changed.");
        }

        if (canceled > 0)
        {
            parts.Add($"{canceled} skipped.");
        }

        if (tooLong > 0)
        {
            parts.Add(tooLong == 1
                ? "1 is in a location too long for Windows to show the new look."
                : $"{tooLong} are in locations too long for Windows to show the new look.");
        }

        if (changed > tooLong)
        {
            parts.Add("A File Explorer window that was already open can keep the old look until you close and reopen it.");
        }

        return parts.Count == 0 ? "Nothing to do." : string.Join(' ', parts);
    }

    public static string RestoreSummary(IReadOnlyCollection<FolderRestoreResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var restored = results.Count(r => r.Outcome is RestoreOutcome.Restored or RestoreOutcome.AlreadyRestored);
        var kept = results.Count(r => r.Outcome == RestoreOutcome.Forgotten);
        var stayed = results.Count(r => r.FolderKept);
        var untouched = results.Count(r => r.Outcome is
            RestoreOutcome.Conflict or RestoreOutcome.FolderMissing or RestoreOutcome.DifferentFolder or
            RestoreOutcome.Refused or RestoreOutcome.Failed);

        var parts = new List<string>();

        if (restored > 0)
        {
            parts.Add($"Put back {Folders(restored)}.");
        }

        if (stayed > 0)
        {
            parts.Add(stayed == 1
                ? "1 folder Foldcraft made stayed, because it no longer holds only what Foldcraft put there."
                : $"{stayed} folders Foldcraft made stayed, because they no longer hold only what Foldcraft put there.");
        }

        if (kept > 0)
        {
            parts.Add($"Stopped tracking {Folders(kept)}.");
        }

        if (untouched > 0)
        {
            parts.Add($"{Folders(untouched)} couldn't be put back.");
        }

        return parts.Count == 0 ? "Nothing to put back." : string.Join(' ', parts);
    }

    /// <summary>
    /// True when Windows may not show a new look even though the change succeeds: the path of
    /// the hidden settings file inside the folder would be too long for Explorer.
    /// </summary>
    public static bool MayNotShowInExplorer(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        return folderPath.Length + "\\desktop.ini".Length >= ShellPathLimit;
    }

    private static bool TooLongToShow(FolderApplyResult result) =>
        MayNotShowInExplorer(result.ResolvedPath ?? result.RequestedPath);

    /// <summary>"Today at 14:05", "Yesterday at 09:30" or a date.</summary>
    public static string When(DateTimeOffset moment, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;

        var local = TimeZoneInfo.ConvertTime(moment, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var time = local.ToString("t", CultureInfo.CurrentCulture);

        if (local.Date == today)
        {
            return $"Today at {time}";
        }

        return local.Date == today.AddDays(-1)
            ? $"Yesterday at {time}"
            : $"{local.ToString("d MMM yyyy", CultureInfo.CurrentCulture)} at {time}";
    }

    /// <summary>Core's message when it is readable, the fallback when it talks about internals.</summary>
    private static string Failure(string? message, string fallback) =>
        string.IsNullOrWhiteSpace(message) || Technical().IsMatch(message) ? fallback : message;

    [GeneratedRegex(@"desktop\.ini|journal|reparse|junction|access to the path|0x[0-9a-f]+|exception", RegexOptions.IgnoreCase)]
    private static partial Regex Technical();
}
