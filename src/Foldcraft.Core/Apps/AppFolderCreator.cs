using System.Text;
using System.Text.RegularExpressions;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Apps;

/// <summary>What to do when a new folder's name is already taken.</summary>
public enum NameClashChoice
{
    /// <summary>Leave what's there and skip this one.</summary>
    Skip,

    /// <summary>Make the folder anyway, as "Name (2)".</summary>
    AddNumber,
}

public enum AppFolderOutcome
{
    /// <summary>The folder and its shortcut were made, and can be undone.</summary>
    Created,

    /// <summary>An existing folder got the look, and can be undone.</summary>
    Themed,

    /// <summary>Something already has this name, and the user chose to skip such names.</summary>
    NameTaken,

    /// <summary>The destination or the folder isn't safe to use. Nothing was touched.</summary>
    Refused,

    /// <summary>Something failed, and anything partly made was taken away.</summary>
    Failed,

    /// <summary>Something failed and taking it back failed too. History can finish the job.</summary>
    LeftUnfinished,

    /// <summary>The run was canceled before this one.</summary>
    Canceled,
}

/// <summary>One folder to make.</summary>
/// <param name="App">The app it's for.</param>
/// <param name="FolderName">The name to use. It's made safe before use.</param>
/// <param name="Spec">The look, with its logo still in memory.</param>
/// <param name="ArtPng">The logo, or null for a look that has no picture.</param>
public sealed record AppFolderRequest(DetectedApp App, string FolderName, FolderIconSpec Spec, byte[]? ArtPng);

/// <summary>How to make a set of folders.</summary>
/// <param name="Destination">Where the new folders go.</param>
/// <param name="Clash">What to do when a name is taken.</param>
/// <param name="ThemeInstallFolders">
/// Also give each app's own install folder the look. Off unless the user turns it on.
/// </param>
public sealed record AppFolderOptions(string Destination, NameClashChoice Clash, bool ThemeInstallFolders = false);

public sealed record AppFolderResult
{
    public required string AppId { get; init; }
    public required string AppName { get; init; }
    public required AppFolderOutcome Outcome { get; init; }
    public string? FolderPath { get; init; }
    public FolderRefusal? Refusal { get; init; }
    public string? Message { get; init; }

    /// <summary>True for an existing folder that already had the look, so nothing changed and there's nothing to undo.</summary>
    public bool AlreadyHadLook { get; init; }

    /// <summary>What happened to the app's own install folder, if the user asked for that.</summary>
    public FolderApplyResult? InstallFolder { get; init; }
}

/// <summary>Everything one run made, which undoes as a single step.</summary>
public sealed record AppFolderBatch(Guid BatchId, IReadOnlyList<AppFolderResult> Results);

/// <summary>
/// Makes app folders. Protected places stay protected: an app's own folder is usually in Program
/// Files or AppData, so the normal path makes a new folder somewhere the user chose, holding a
/// shortcut that starts the app. Nothing that exists is ever overwritten, every folder made is
/// recorded before it's made and a whole run undoes as one step.
/// </summary>
public sealed partial class AppFolderCreator
{
    /// <summary>"Name (2)" to "Name (99)". Past that something else is wrong.</summary>
    private const int MaxNumber = 99;

    private readonly FoldcraftPaths _paths;
    private readonly FolderSafety _safety;
    private readonly FolderCustomizer _customizer;
    private readonly IconLibrary _icons;
    private readonly IShortcuts _shortcuts;
    private readonly AppArtStore _art;

    public AppFolderCreator(
        FoldcraftPaths paths,
        FolderSafety safety,
        FolderCustomizer customizer,
        IconLibrary icons,
        IShortcuts shortcuts,
        AppArtStore art)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
        _customizer = customizer ?? throw new ArgumentNullException(nameof(customizer));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _art = art ?? throw new ArgumentNullException(nameof(art));
    }

    /// <summary>Makes every folder and reports each result. One failing never stops the others.</summary>
    /// <param name="requests">What to make.</param>
    /// <param name="options">Where, and how to handle a clash.</param>
    /// <param name="cancellationToken">Stops before the next one. One already started finishes.</param>
    /// <param name="progress">Told about each result as it happens.</param>
    public AppFolderBatch Create(
        IReadOnlyList<AppFolderRequest> requests,
        AppFolderOptions options,
        CancellationToken cancellationToken = default,
        IProgress<AppFolderResult>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(options);

        var batchId = Guid.NewGuid();
        var results = new List<AppFolderResult>(requests.Count);
        var destination = _safety.CheckDestination(options.Destination);

        foreach (var request in requests)
        {
            var result =
                !destination.IsAllowed ? Refused(request, null, destination)
                : cancellationToken.IsCancellationRequested ? Outcome(request, AppFolderOutcome.Canceled)
                : CreateOne(request, destination.ResolvedPath!, options, batchId);

            results.Add(result);
            progress?.Report(result);
        }

        return new AppFolderBatch(batchId, results);
    }

    /// <summary>
    /// Gives a folder that already exists an app's look, such as a Steam library on another drive.
    /// It goes through the ordinary apply, so every safety check and the undo history apply.
    /// </summary>
    public AppFolderResult ThemeExisting(AppFolderRequest request, string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return ThemeExisting(request, [folder])[0];
    }

    /// <summary>
    /// Gives several folders that already exist the same look, such as every version of one app.
    /// The whole run undoes as one step, and the results are in the order of the folders.
    /// </summary>
    /// <param name="request">The look to use.</param>
    /// <param name="folders">The folders to change.</param>
    /// <param name="cancellationToken">Stops before the next folder. One already started finishes.</param>
    /// <param name="progress">Told about each folder as it finishes.</param>
    public IReadOnlyList<AppFolderResult> ThemeExisting(
        AppFolderRequest request,
        IReadOnlyList<string> folders,
        CancellationToken cancellationToken = default,
        IProgress<FolderApplyResult>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(folders);

        FolderIconSpec spec;

        try
        {
            spec = WithSavedArt(request);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return folders.Select(_ => Outcome(request, AppFolderOutcome.Failed, null, ex.Message)).ToArray();
        }

        var looks = folders.Select(folder => new FolderLook(folder, spec)).ToArray();

        return _customizer.ApplyEach(looks, cancellationToken, progress).Results.Select(applied => applied.Outcome switch
        {
            ApplyOutcome.Applied => Outcome(request, AppFolderOutcome.Themed, applied.ResolvedPath),
            ApplyOutcome.Unchanged => Outcome(request, AppFolderOutcome.Themed, applied.ResolvedPath) with { AlreadyHadLook = true },
            ApplyOutcome.Refused => Outcome(request, AppFolderOutcome.Refused, null, applied.Message) with { Refusal = applied.Refusal },
            ApplyOutcome.Canceled => Outcome(request, AppFolderOutcome.Canceled),
            ApplyOutcome.FailedRollbackIncomplete => Outcome(request, AppFolderOutcome.LeftUnfinished, applied.ResolvedPath, applied.Message),
            _ => Outcome(request, AppFolderOutcome.Failed, applied.ResolvedPath, applied.Message),
        }).ToArray();
    }

    /// <summary>
    /// Lists the folders directly inside one, in name order, leaving out hidden folders, system
    /// folders and links. This only reads. A folder that can't be read has nothing to list.
    /// </summary>
    public static IReadOnlyList<string> FoldersInside(string parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
        };

        try
        {
            return Directory.EnumerateDirectories(parent, "*", options).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private AppFolderResult CreateOne(AppFolderRequest request, string destination, AppFolderOptions options, Guid batchId)
    {
        FolderIconSpec spec;
        NewFolderFile shortcut;
        var name = FolderNames.Sanitize(request.FolderName);

        try
        {
            spec = WithSavedArt(request);
            shortcut = BuildShortcut(request.App, name, spec);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IconRenderException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return Outcome(request, AppFolderOutcome.Failed, null, ex.Message);
        }

        var result = MakeUnderFreeName(request, destination, name, shortcut, spec, options.Clash, batchId);

        // An item that was skipped or failed stays that way: its install folder is left alone too.
        if (result.Outcome != AppFolderOutcome.Created || !options.ThemeInstallFolders || request.App.InstallFolder is not { } install)
        {
            return result;
        }

        return result with { InstallFolder = _customizer.Run([new FolderLook(install, spec)], batchId, isRepair: false).Results[0] };
    }

    private AppFolderResult MakeUnderFreeName(
        AppFolderRequest request,
        string destination,
        string name,
        NewFolderFile shortcut,
        FolderIconSpec spec,
        NameClashChoice clash,
        Guid batchId)
    {
        for (var number = 1; number <= MaxNumber; number++)
        {
            var candidate = number == 1 ? name : FolderNames.WithNumber(name, number);
            var path = Path.Combine(destination, candidate);

            var applied = IsTaken(path)
                ? new FolderApplyResult { RequestedPath = path, Outcome = ApplyOutcome.Unchanged, NameTaken = true }
                : _customizer.CreateFolder(path, [shortcut], spec, batchId);

            if (!applied.NameTaken)
            {
                return applied.Outcome switch
                {
                    ApplyOutcome.Applied => Outcome(request, AppFolderOutcome.Created, path),
                    ApplyOutcome.Refused => Outcome(request, AppFolderOutcome.Refused, null, applied.Message) with { Refusal = applied.Refusal },
                    ApplyOutcome.FailedRollbackIncomplete => Outcome(request, AppFolderOutcome.LeftUnfinished, path, applied.Message),
                    _ => Outcome(request, AppFolderOutcome.Failed, null, applied.Message),
                };
            }

            if (clash == NameClashChoice.Skip)
            {
                return Outcome(request, AppFolderOutcome.NameTaken, path);
            }
        }

        return Outcome(request, AppFolderOutcome.Failed, null, "Too many folders already have this name.");
    }

    /// <summary>Anything at all at that path counts, including a file or a link with nothing behind it.</summary>
    private static bool IsTaken(string path) => (int)new FileInfo(path).Attributes != -1;

    private FolderIconSpec WithSavedArt(AppFolderRequest request) =>
        request.ArtPng is null ? request.Spec : request.Spec with { CustomImagePath = _art.Save(request.ArtPng) };

    /// <summary>The shortcut's bytes, built ahead of time so the journal can hold their hash.</summary>
    private NewFolderFile BuildShortcut(DetectedApp app, string name, FolderIconSpec spec)
    {
        var launch = app.Launch;

        return launch.Kind switch
        {
            LaunchKind.Address => new NewFolderFile($"{name}.url", AddressFile(launch.Target, app)),
            LaunchKind.OtherShortcut => new NewFolderFile($"{name}.lnk", _shortcuts.Build(
                new ShortcutInfo(ShellOpener, $"\"{launch.Target}\"", null, _icons.Ensure(spec)),
                _paths.ScratchDirectory)),
            _ => new NewFolderFile($"{name}.lnk", _shortcuts.Build(
                new ShortcutInfo(launch.Target, launch.Arguments, launch.WorkingDirectory, IconOf(app)?.Path, IconOf(app)?.Index ?? 0),
                _paths.ScratchDirectory)),
        };
    }

    /// <summary>Explorer starts a shortcut it's handed. Setting the path of one shortcut to another would be flattened on save.</summary>
    private static string ShellOpener => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static IconCandidate? IconOf(DetectedApp app) => app.Icons.FirstOrDefault();

    /// <summary>
    /// An internet shortcut is a text file Windows follows blindly, so nothing goes in unless
    /// it's exactly the address form a launcher uses. The icon is left out unless it's plain
    /// ASCII, because the format doesn't reliably carry anything else.
    /// </summary>
    private static byte[] AddressFile(string address, DetectedApp app)
    {
        if (!LauncherAddress().IsMatch(address))
        {
            throw new InvalidOperationException("This shortcut's address isn't one Foldcraft will write.");
        }

        var text = new StringBuilder("[InternetShortcut]\r\n").Append("URL=").Append(address).Append("\r\n");

        if (app.Icons.FirstOrDefault(icon => icon.Path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) is { } icon &&
            icon.Path.All(c => char.IsAscii(c) && !char.IsControl(c)))
        {
            text.Append("IconFile=").Append(icon.Path).Append("\r\nIconIndex=0\r\n");
        }

        return Encoding.ASCII.GetBytes(text.ToString());
    }

    private static AppFolderResult Outcome(AppFolderRequest request, AppFolderOutcome outcome, string? folder = null, string? message = null) => new()
    {
        AppId = request.App.Id,
        AppName = request.App.Name,
        Outcome = outcome,
        FolderPath = folder,
        Message = message,
    };

    private static AppFolderResult Refused(AppFolderRequest request, string? folder, FolderSafetyResult verdict) =>
        Outcome(request, AppFolderOutcome.Refused, folder, verdict.Reason) with { Refusal = verdict.Refusal };

    [GeneratedRegex(@"^(steam://rungameid/[0-9]{1,12}|com\.epicgames\.launcher://apps/[A-Za-z0-9._%-]{1,400}\?action=launch&silent=true)$")]
    private static partial Regex LauncherAddress();
}
