namespace Foldcraft.App.Services.Updates;

/// <summary>A newer version, found on the update source or already downloaded.</summary>
public sealed record AvailableUpdate(string Version);

/// <summary>
/// Everything the app needs from an update source, so tests can stand in and nothing else in the
/// app knows where updates come from. Only the real app is given one that reaches the network.
/// </summary>
public interface IUpdateService
{
    /// <summary>False for a build the installer didn't put in place, such as a source build. It can't update itself.</summary>
    bool IsInstalled { get; }

    string CurrentVersion { get; }

    /// <summary>An update that finished downloading earlier and hasn't been installed, if there is one. This stays on this PC.</summary>
    AvailableUpdate? FindDownloaded();

    /// <summary>Asks whether a newer stable version exists. Null means no. A failure throws.</summary>
    Task<AvailableUpdate?> CheckAsync(CancellationToken cancellation);

    /// <summary>
    /// Downloads an update and checks each package it fetches, delta or full, against the release.
    /// A full package rebuilt from deltas isn't checked again as a whole. The app itself doesn't
    /// change, but a finished download also replaces the updater (Update.exe) with the new
    /// version's straight away. Progress runs from 0 to 100, on any thread.
    /// </summary>
    Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellation);

    /// <summary>
    /// True when something other than this process is running Foldcraft from the same install, as a
    /// copy with its own data folder can. The updater stops all of those without asking.
    /// </summary>
    bool AnotherCopyIsRunning();

    /// <summary>Starts the updater, which waits for this process to end, installs the update and starts Foldcraft again.</summary>
    /// <param name="update">What to install.</param>
    /// <param name="restartArguments">The command line for the new version.</param>
    void InstallWhenThisCloses(AvailableUpdate update, IReadOnlyList<string> restartArguments);
}

/// <summary>What a build has when it can't update itself. It never touches the network.</summary>
public sealed class NoUpdateService : IUpdateService
{
    public bool IsInstalled => false;

    public string CurrentVersion => AppVersion.Current;

    public AvailableUpdate? FindDownloaded() => null;

    public Task<AvailableUpdate?> CheckAsync(CancellationToken cancellation) => Task.FromResult<AvailableUpdate?>(null);

    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellation) =>
        throw new InvalidOperationException("This copy of Foldcraft wasn't installed, so it can't update itself.");

    public bool AnotherCopyIsRunning() => false;

    public void InstallWhenThisCloses(AvailableUpdate update, IReadOnlyList<string> restartArguments) =>
        throw new InvalidOperationException("This copy of Foldcraft wasn't installed, so it can't update itself.");
}
