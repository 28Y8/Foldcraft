using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Foldcraft.Core.Updates;
using Microsoft.Win32.SafeHandles;
using Velopack;
using Velopack.Sources;

namespace Foldcraft.App.Services.Updates;

/// <summary>
/// Updates through Velopack, from this project's GitHub Releases. The only requests are the
/// release list and the files of a release, with no sign-in. Stable releases only, over HTTPS.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private const string TestFeedKey = "FoldcraftTestUpdateFeed";

    private const string ProcessName = "Foldcraft";

    /// <summary>Velopack's own limit is half an hour, far too long to leave someone looking at "Checking".</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a download may go without progress before it counts as lost. Progress moves in
    /// steps of a few percent, so this leaves room for a slow link, and it has to outlast the
    /// updater's step that rebuilds a package from deltas, which reports nothing for up to 5 minutes.
    /// </summary>
    private static readonly TimeSpan DownloadStallLimit = TimeSpan.FromMinutes(10);

    private readonly UpdateManager _manager;
    private readonly Lock _gate = new();
    private UpdateInfo? _found;

    private VelopackUpdateService(UpdateManager manager)
    {
        _manager = manager;
    }

    public bool IsInstalled => true;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? AppVersion.Current;

    /// <summary>The service for this copy of the app. Run this only after <see cref="Program.Main"/> has started Velopack.</summary>
    public static IUpdateService ForThisApp(FileLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (!UpdateHooks.IsPackaged(AppContext.BaseDirectory))
        {
            return new NoUpdateService();
        }

        try
        {
            var manager = new UpdateManager(SourceFor(ReadTestFeed()));

            return manager.IsInstalled ? new VelopackUpdateService(manager) : new NoUpdateService();
        }
        catch (Exception ex)
        {
            // Updates are a convenience, so a problem here is no reason not to start.
            log.Write($"Updates could not be set up: {UpdateFailures.ForLog(ex)}");
            return new NoUpdateService();
        }
    }

    /// <summary>GitHub, unless this build was made for the end-to-end test.</summary>
    public static IUpdateSource SourceFor(string? testFeed) => string.IsNullOrEmpty(testFeed)
        ? new GithubSource(ReleaseSite.RepositoryUrl, accessToken: null, prerelease: false)
        : new SimpleFileSource(new DirectoryInfo(testFeed));

    /// <summary>What the build recorded. A shipped build has nothing here.</summary>
    public static string? ReadTestFeed() =>
        typeof(VelopackUpdateService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == TestFeedKey)?.Value;

    public AvailableUpdate? FindDownloaded() =>
        _manager.UpdatePendingRestart is { } package ? new AvailableUpdate(package.Version.ToString()) : null;

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellation)
    {
        var found = await UpdateLimits.WaitWithinAsync(_manager.CheckForUpdatesAsync(), CheckTimeout, cancellation);

        lock (_gate)
        {
            _found = found;
        }

        return found is null ? null : new AvailableUpdate(found.TargetFullRelease.Version.ToString());
    }

    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(update);

        UpdateInfo? found;

        lock (_gate)
        {
            found = _found;
        }

        if (found is null || found.TargetFullRelease.Version.ToString() != update.Version)
        {
            throw new InvalidOperationException("Only an update that a check found can be downloaded.");
        }

        return UpdateLimits.DownloadAsync(
            (report, token) => _manager.DownloadUpdatesAsync(found, report, token),
            progress,
            DownloadStallLimit,
            cancellation);
    }

    public bool AnotherCopyIsRunning()
    {
        var installFolder = UpdateHooks.InstallFolder(AppContext.BaseDirectory);
        var running = Process.GetProcessesByName(ProcessName);

        try
        {
            return running.Any(process => process.Id != Environment.ProcessId && UpdateHooks.IsInside(installFolder, PathOf(process)));
        }
        finally
        {
            foreach (var process in running)
            {
                process.Dispose();
            }
        }
    }

    public void InstallWhenThisCloses(AvailableUpdate update, IReadOnlyList<string> restartArguments)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(restartArguments);

        if (_manager.UpdatePendingRestart is not { } package || package.Version.ToString() != update.Version)
        {
            throw new InvalidOperationException("That update hasn't finished downloading.");
        }

        _manager.WaitExitThenApplyUpdates(package, silent: false, restart: true, restartArguments.ToArray());
    }

    /// <summary>
    /// Null for a process that has ended. This asks for the least Windows offers, because a copy
    /// running as administrator refuses anything more, and it's the copy that most needs seeing.
    /// </summary>
    private static string? PathOf(Process process)
    {
        try
        {
            using var handle = OpenProcess(QueryLimitedInformation, false, process.Id);

            var path = new char[MaxPathLength];
            var length = path.Length;

            return !handle.IsInvalid && QueryFullProcessImageName(handle, 0, path, ref length) ? new string(path, 0, length) : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private const int QueryLimitedInformation = 0x1000;

    private const int MaxPathLength = 32768;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(int access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, [Out] char[] path, ref int length);
}
