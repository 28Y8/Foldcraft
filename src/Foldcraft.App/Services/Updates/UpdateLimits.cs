namespace Foldcraft.App.Services.Updates;

/// <summary>Time limits around the updater's calls, whose own limits are far too long for someone waiting.</summary>
public static class UpdateLimits
{
    /// <summary>
    /// Waits for a task, up to a limit. A task that outlasts the limit can still fail later, when
    /// nothing is waiting for it, and that failure would then surface at some random moment as an
    /// unexpected problem. So the task is observed here, before the wait starts.
    /// </summary>
    public static Task<T> WaitWithinAsync<T>(Task<T> task, TimeSpan limit, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(task);

        _ = task.ContinueWith(
            static finished => _ = finished.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task.WaitAsync(limit, cancellation);
    }

    /// <summary>
    /// Runs a download that gets stopped when no progress arrives for the whole of the limit,
    /// which then fails with a <see cref="TimeoutException"/>. That keeps it apart from a cancel,
    /// which still comes out as a cancel.
    /// </summary>
    public static async Task DownloadAsync(
        Func<Action<int>, CancellationToken, Task> download,
        Action<int> progress,
        TimeSpan stallLimit,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(progress);

        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellation);

        watchdog.CancelAfter(stallLimit);

        try
        {
            await download(
                value =>
                {
                    watchdog.CancelAfter(stallLimit);
                    progress(value);
                },
                watchdog.Token);
        }
        catch (OperationCanceledException) when (watchdog.IsCancellationRequested && !cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("The download stopped making progress.");
        }
    }
}
