using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Foldcraft.App;
using Foldcraft.App.Services;
using Foldcraft.App.Services.Updates;
using Foldcraft.App.ViewModels;
using Foldcraft.Core;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Settings;
using Foldcraft.Core.Shell;
using Foldcraft.Core.Themes;
using Foldcraft.Core.Updates;
using Velopack.Exceptions;
using Velopack.Sources;

namespace Foldcraft.Tests;

/// <summary>Finds the repository, for tests that read what ships.</summary>
internal static class UpdateRepository
{
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Foldcraft.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository wasn't found.");
    }
}

/// <summary>A clock a test can move.</summary>
internal sealed class MovableTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>An update source with no network behind it. It remembers what it was asked.</summary>
internal sealed class FakeUpdateService : IUpdateService
{
    public bool IsInstalled { get; set; } = true;

    public string CurrentVersion { get; set; } = "0.1.0";

    /// <summary>What the next check finds.</summary>
    public AvailableUpdate? Found { get; set; }

    public AvailableUpdate? Downloaded { get; set; }

    public Exception? CheckFailure { get; set; }

    public Exception? DownloadFailure { get; set; }

    public Exception? InstallFailure { get; set; }

    /// <summary>What the question about other copies answers.</summary>
    public bool AnotherCopy { get; set; }

    /// <summary>Set to hold a check open until the test lets it finish.</summary>
    public TaskCompletionSource? CheckGate { get; set; }

    /// <summary>Set to hold a download open, partway through, until the test lets it finish.</summary>
    public TaskCompletionSource? DownloadGate { get; set; }

    public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Checks { get; private set; }

    public int Downloads { get; private set; }

    public List<IReadOnlyList<string>> Installs { get; } = [];

    public AvailableUpdate? FindDownloaded() => Downloaded;

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellation)
    {
        Checks++;

        if (CheckGate is { } gate)
        {
            await gate.Task;
        }

        return CheckFailure is { } failure ? throw failure : Found;
    }

    public async Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken cancellation)
    {
        Downloads++;
        progress(40);
        DownloadStarted.TrySetResult();

        if (DownloadGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellation);
        }

        cancellation.ThrowIfCancellationRequested();

        if (DownloadFailure is { } failure)
        {
            throw failure;
        }

        progress(100);
    }

    public bool AnotherCopyIsRunning() => AnotherCopy;

    public void InstallWhenThisCloses(AvailableUpdate update, IReadOnlyList<string> restartArguments)
    {
        if (InstallFailure is { } failure)
        {
            throw failure;
        }

        Installs.Add(restartArguments);
    }
}

public sealed class UpdateScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_first_check_is_due_straight_away() =>
        Assert.True(UpdateSchedule.IsDue(new AppSettings(), Now));

    [Fact]
    public void A_check_inside_a_day_is_not_due() =>
        Assert.False(UpdateSchedule.IsDue(new AppSettings { LastUpdateAttempt = Now.AddHours(-23.9) }, Now));

    [Fact]
    public void A_check_a_day_later_is_due() =>
        Assert.True(UpdateSchedule.IsDue(new AppSettings { LastUpdateAttempt = Now.AddHours(-24) }, Now));

    [Fact]
    public void Turning_checks_off_stops_them_whatever_the_date() =>
        Assert.False(UpdateSchedule.IsDue(new AppSettings { CheckForUpdatesAutomatically = false }, Now));

    [Fact]
    public void A_date_in_the_future_cannot_stop_checks_for_good() =>
        Assert.True(UpdateSchedule.IsDue(new AppSettings { LastUpdateAttempt = Now.AddYears(5) }, Now));
}

public sealed class UpdateSettingsTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private SettingsStore Store => new(_box.Paths);

    [Fact]
    public void Checking_is_on_by_default() => Assert.True(new AppSettings().CheckForUpdatesAutomatically);

    [Fact]
    public void The_update_settings_are_kept()
    {
        var moment = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(-5));

        Store.Save(new AppSettings { CheckForUpdatesAutomatically = false, LastUpdateAttempt = moment, LastUpdateCheck = moment });

        var loaded = Store.Load();

        Assert.False(loaded.CheckForUpdatesAutomatically);
        Assert.Equal(moment, loaded.LastUpdateAttempt);
        Assert.Equal(moment, loaded.LastUpdateCheck);
    }

    /// <summary>Written out by hand, as 0.1.0 wrote it before these settings existed. It must load forever.</summary>
    [Fact]
    public void A_settings_file_from_before_updates_still_loads()
    {
        File.WriteAllText(_box.Paths.SettingsFile, """
            {
              "ThemeMode": "Dark",
              "RightClick": {
                "Command": "\u0022C:\\Apps\\Foldcraft.exe\u0022 --customize \u0022%1\u0022",
                "ExePath": "C:\\Apps\\Foldcraft.exe"
              }
            }
            """);

        var loaded = Store.Load();

        Assert.Equal(AppThemeMode.Dark, loaded.ThemeMode);
        Assert.Equal(@"C:\Apps\Foldcraft.exe", loaded.RightClick?.ExePath);
        Assert.True(loaded.CheckForUpdatesAutomatically);
        Assert.Null(loaded.LastUpdateCheck);
    }

    [Fact]
    public void A_setting_from_a_later_version_is_ignored_and_not_a_failure()
    {
        File.WriteAllText(_box.Paths.SettingsFile, """{ "ThemeMode": "Light", "SomethingNew": { "a": 1 } }""");

        Assert.Equal(AppThemeMode.Light, Store.Load().ThemeMode);
        Assert.False(Store.Read().Unreadable);
    }

    [Fact]
    public void No_file_yet_is_not_a_problem() => Assert.False(Store.Read().Unreadable);

    [Fact]
    public void A_saved_file_reads_back_as_usable()
    {
        Store.Save(new AppSettings { ThemeMode = AppThemeMode.Dark });

        Assert.Equal(new SettingsRead(new AppSettings { ThemeMode = AppThemeMode.Dark }, Unreadable: false), Store.Read());
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("null")]
    [InlineData("""{ "ThemeMode": "Purple" }""")]
    public void A_bad_file_is_reported_and_not_passed_off_as_a_first_start(string text)
    {
        File.WriteAllText(_box.Paths.SettingsFile, text);

        Assert.Equal(new SettingsRead(new AppSettings(), Unreadable: true), Store.Read());
    }

    [Fact]
    public void A_file_too_big_to_be_settings_is_reported()
    {
        File.WriteAllText(_box.Paths.SettingsFile, new string(' ', 65 * 1024));

        Assert.True(Store.Read().Unreadable);
    }

    [Fact]
    public void A_file_another_program_holds_is_reported()
    {
        File.WriteAllText(_box.Paths.SettingsFile, "{}");

        using var held = new FileStream(_box.Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.True(Store.Read().Unreadable);
    }

    [Fact]
    public void Setting_a_bad_file_aside_keeps_what_was_in_it()
    {
        File.WriteAllText(_box.Paths.SettingsFile, "{ broken");

        Store.SetAsideUnreadable();

        Assert.False(File.Exists(_box.Paths.SettingsFile));
        Assert.Equal("{ broken", File.ReadAllText(_box.Paths.UnreadableSettingsFile));
    }

    [Fact]
    public void Setting_aside_a_file_that_has_gone_is_not_a_failure()
    {
        Store.SetAsideUnreadable();

        Assert.False(File.Exists(_box.Paths.UnreadableSettingsFile));
    }
}

public sealed class UpdateLimitTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    private sealed class LateFailure : Exception;

    /// <summary>Lets the failed task go out of reach in this frame, so the collector can finalize it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task TimeOutThenFailAsync(Func<Task<int>, Task<int>> wait, Exception failure)
    {
        var inner = new TaskCompletionSource<int>();

        await Assert.ThrowsAsync<TimeoutException>(() => wait(inner.Task));

        inner.SetException(failure);
    }

    private static async Task<int> CountUnobservedAsync(Func<Task<int>, Task<int>> wait)
    {
        var failure = new LateFailure();
        var seen = 0;

        void Watch(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.InnerExceptions.Contains(failure))
            {
                Interlocked.Increment(ref seen);
                e.SetObserved();
            }
        }

        TaskScheduler.UnobservedTaskException += Watch;

        try
        {
            await TimeOutThenFailAsync(wait, failure);

            for (var attempt = 0; attempt < 5 && Volatile.Read(ref seen) == 0; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(10);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Watch;
        }

        return seen;
    }

    /// <summary>The plain wait is what used to run. It shows this test can see the failure at all.</summary>
    [Fact]
    public async Task A_plain_wait_leaves_a_late_failure_for_the_crash_dialog() =>
        Assert.Equal(1, await CountUnobservedAsync(task => task.WaitAsync(Short)));

    [Fact]
    public async Task A_check_that_fails_after_its_limit_stays_out_of_the_crash_dialog() =>
        Assert.Equal(0, await CountUnobservedAsync(task => UpdateLimits.WaitWithinAsync(task, Short, CancellationToken.None)));

    [Fact]
    public async Task A_check_that_answers_in_time_gives_its_answer() =>
        Assert.Equal(7, await UpdateLimits.WaitWithinAsync(Task.FromResult(7), TimeSpan.FromMinutes(1), CancellationToken.None));

    [Fact]
    public async Task A_check_that_fails_in_time_still_fails()
    {
        var failed = Task.FromException<int>(new LateFailure());

        await Assert.ThrowsAsync<LateFailure>(() => UpdateLimits.WaitWithinAsync(failed, TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task A_download_that_goes_silent_counts_as_a_lost_connection()
    {
        var reached = new List<int>();

        var failure = await Assert.ThrowsAsync<TimeoutException>(() => UpdateLimits.DownloadAsync(
            async (report, token) =>
            {
                report(40);
                await Task.Delay(Timeout.Infinite, token);
            },
            reached.Add,
            Short,
            CancellationToken.None));

        Assert.Equal([40], reached);
        Assert.Equal(UpdateFailure.CouldNotConnect, UpdateFailures.Classify(failure));
    }

    [Fact]
    public async Task A_download_that_never_starts_moving_is_stopped_too()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => UpdateLimits.DownloadAsync(
            (_, token) => Task.Delay(Timeout.Infinite, token),
            _ => { },
            Short,
            CancellationToken.None));
    }

    /// <summary>It lasts longer than the limit, but each step lands inside it.</summary>
    [Fact]
    public async Task A_slow_download_that_keeps_moving_is_left_alone()
    {
        var reached = new List<int>();

        await UpdateLimits.DownloadAsync(
            async (report, token) =>
            {
                foreach (var percent in new[] { 20, 40, 60, 80, 100 })
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400), token);
                    report(percent);
                }
            },
            reached.Add,
            TimeSpan.FromSeconds(1.5),
            CancellationToken.None);

        Assert.Equal([20, 40, 60, 80, 100], reached);
    }

    [Fact]
    public async Task Canceling_is_still_a_cancel_and_not_a_lost_connection()
    {
        using var cancel = new CancellationTokenSource();

        var download = UpdateLimits.DownloadAsync(
            (_, token) => Task.Delay(Timeout.Infinite, token),
            _ => { },
            TimeSpan.FromMinutes(1),
            cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
    }

    [Fact]
    public async Task A_download_failure_comes_through_unchanged()
    {
        await Assert.ThrowsAsync<ChecksumFailedException>(() => UpdateLimits.DownloadAsync(
            (_, _) => throw new ChecksumFailedException("x"),
            _ => { },
            TimeSpan.FromMinutes(1),
            CancellationToken.None));
    }
}

/// <summary>Files exactly as 0.1.0 writes them. If one of these fails, an update would orphan someone's history.</summary>
public sealed class DataAcrossVersionsTests : IDisposable
{
    private const string Journal0_1_0 =
        "ba5acc735ec10722b9ba1b911d1de12e\t{\"k\":0,\"id\":\"c15df473-2383-4ef3-9f77-c67617e9bd00\",\"ts\":\"2026-03-04T05:06:07+00:00\"," +
        "\"batch\":\"11111111-2222-3333-4444-555555555555\",\"folder\":\"D:\\\\Practice\\\\Sample\",\"iniExisted\":false,\"folderAttrs\":16," +
        "\"icon\":\"C:\\\\data\\\\Icons\\\\look.ico\",\"written\":\"abc\",\"folderTime\":\"2026-03-04T05:06:07Z\"}\n" +
        "daf3e12e0b1d421bca5c8e16b3042a1b\t{\"k\":1,\"id\":\"c15df473-2383-4ef3-9f77-c67617e9bd00\",\"ts\":\"2026-03-04T05:06:07+00:00\"}\n";

    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    [Fact]
    public void A_journal_from_0_1_0_still_reads_and_can_be_undone()
    {
        File.WriteAllText(_box.Paths.JournalFile, Journal0_1_0, new UTF8Encoding(false));

        var snapshot = _box.Journal.Read();
        var entry = Assert.Single(snapshot.Entries);

        Assert.Equal(0, snapshot.CorruptRecords);
        Assert.Equal(@"D:\Practice\Sample", entry.Change.FolderPath);
        Assert.Equal(JournalEntryState.Applied, entry.State);
    }

    [Fact]
    public void Reading_a_journal_never_rewrites_it()
    {
        File.WriteAllText(_box.Paths.JournalFile, Journal0_1_0, new UTF8Encoding(false));

        _box.Journal.Read();

        Assert.Equal(Journal0_1_0, File.ReadAllText(_box.Paths.JournalFile));
    }

    [Fact]
    public void Saved_themes_from_0_1_0_still_load()
    {
        File.WriteAllText(_box.Paths.ThemesFile, """
            {
              "format": "foldcraft-themes",
              "version": 1,
              "themes": [
                {
                  "name": "Ocean",
                  "mood": "Cool",
                  "fill": { "start": "#0EA5E9", "end": "#1E3A8A", "angle": 135 },
                  "shape": "soft3d",
                  "glyph": "fish",
                  "badge": { "kind": "done", "corner": "bottomRight", "color": "#16A34A" }
                }
              ]
            }
            """);

        var theme = Assert.Single(ThemeFile.Read(File.ReadAllBytes(_box.Paths.ThemesFile)));

        Assert.Equal("Ocean", theme.Name);
    }
}

public sealed class UpdateCheckTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUpdateService _service = new();
    private readonly MovableTime _time = new(Start);
    private readonly AppHarness _app;

    public UpdateCheckTests()
    {
        _app = new AppHarness(time: _time, updates: _service);
    }

    public void Dispose() => _app.Dispose();

    private string LogText => File.Exists(_app.Log.Path) ? File.ReadAllText(_app.Log.Path) : string.Empty;

    [Fact]
    public async Task A_start_with_nothing_new_shows_nothing()
    {
        await _app.Updates.CheckAtStartupAsync();

        Assert.Equal(1, _service.Checks);
        Assert.False(_app.Updates.IsBannerVisible);
        Assert.Null(_app.Updates.CheckMessage);
        Assert.Empty(_app.Dialogs.Errors);
    }

    [Fact]
    public async Task A_new_version_shows_the_banner_and_downloads_nothing()
    {
        _service.Found = new AvailableUpdate("0.2.0");

        await _app.Updates.CheckAtStartupAsync();

        Assert.True(_app.Updates.IsBannerVisible);
        Assert.True(_app.Updates.ShowUpdate);
        Assert.Contains("0.2.0", _app.Updates.BannerText);
        Assert.Equal(0, _service.Downloads);
        Assert.Empty(_service.Installs);
    }

    [Fact]
    public async Task The_banner_links_to_the_notes_for_that_version()
    {
        _service.Found = new AvailableUpdate("0.2.0");
        await _app.Updates.CheckAtStartupAsync();

        _app.Updates.ReleaseNotesCommand.Execute(null);

        Assert.Equal("https://github.com/28Y8/Foldcraft/releases/tag/v0.2.0", Assert.Single(_app.Dialogs.Links));
    }

    [Fact]
    public async Task A_strange_version_cannot_choose_where_the_link_goes()
    {
        _service.Found = new AvailableUpdate("0.2.0/../../../evil");
        await _app.Updates.CheckAtStartupAsync();

        _app.Updates.ReleaseNotesCommand.Execute(null);

        Assert.Equal(ReleaseSite.ReleasesPage, Assert.Single(_app.Dialogs.Links));
    }

    [Fact]
    public async Task Turning_the_setting_off_stops_the_startup_check()
    {
        _app.Updates.CheckAutomatically = false;

        await _app.Updates.CheckAtStartupAsync();

        Assert.Equal(0, _service.Checks);
        Assert.False(new SettingsStore(_app.Box.Paths).Load().CheckForUpdatesAutomatically);
    }

    [Fact]
    public async Task A_second_start_the_same_day_does_not_check_again()
    {
        await _app.Updates.CheckAtStartupAsync();

        _time.Now = Start.AddHours(23);
        await _app.Updates.CheckAtStartupAsync();

        Assert.Equal(1, _service.Checks);

        _time.Now = Start.AddHours(25);
        await _app.Updates.CheckAtStartupAsync();

        Assert.Equal(2, _service.Checks);
    }

    [Fact]
    public async Task The_daily_limit_survives_a_restart()
    {
        await _app.Updates.CheckAtStartupAsync();

        var saved = new SettingsStore(_app.Box.Paths).Load();

        Assert.False(UpdateSchedule.IsDue(saved, Start.AddHours(1)));
    }

    [Fact]
    public async Task A_failed_check_still_counts_for_the_day()
    {
        _service.CheckFailure = new HttpRequestException("offline");

        await _app.Updates.CheckAtStartupAsync();
        await _app.Updates.CheckAtStartupAsync();

        Assert.Equal(1, _service.Checks);
    }

    [Fact]
    public async Task The_time_shown_is_the_last_check_that_got_an_answer()
    {
        await _app.Updates.CheckAtStartupAsync();

        var answered = _app.Updates.LastChecked;

        Assert.Contains("Today", answered);

        _time.Now = Start.AddDays(3);
        _service.CheckFailure = new HttpRequestException("offline");
        await _app.Updates.CheckCommand.ExecuteAsync();

        Assert.DoesNotContain("Today", _app.Updates.LastChecked);
    }

    [Fact]
    public void Before_any_check_the_time_says_so() => Assert.Equal("Last checked: not yet", _app.Updates.LastChecked);

    [Fact]
    public void The_current_version_is_shown() => Assert.Equal("Version 0.1.0", _app.Updates.CurrentVersion);

    [Fact]
    public async Task A_slow_check_never_holds_up_the_start()
    {
        _service.CheckGate = new TaskCompletionSource();

        var check = _app.Updates.CheckAtStartupAsync();

        await _app.Shell.InitializeAsync();

        Assert.False(check.IsCompleted);

        _service.CheckGate.SetResult();
        await check;
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task An_automatic_check_stays_silent_whatever_goes_wrong(Exception failure)
    {
        _service.CheckFailure = failure;

        await _app.Updates.CheckAtStartupAsync();

        Assert.Empty(_app.Dialogs.Errors);
        Assert.False(_app.Updates.IsBannerVisible);
        Assert.Null(_app.Updates.CheckMessage);
        Assert.Contains(failure.GetType().Name, LogText);
    }

    public static TheoryData<Exception> Failures =>
    [
        new HttpRequestException("No such host is known."),
        new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden),
        new TimeoutException(),
        new TaskCanceledException(),
        new IOException(),
        new UnauthorizedAccessException(),
        new InvalidOperationException("Anything at all."),
    ];

    [Fact]
    public async Task A_manual_check_says_when_nothing_is_new()
    {
        await _app.Updates.CheckCommand.ExecuteAsync();

        Assert.Contains("No newer version", _app.Updates.CheckMessage);
        Assert.Contains("0.1.0", _app.Updates.CheckMessage);
    }

    [Fact]
    public async Task A_manual_check_says_what_it_found_and_shows_the_banner()
    {
        _service.Found = new AvailableUpdate("0.2.0");

        await _app.Updates.CheckCommand.ExecuteAsync();

        Assert.Contains("0.2.0", _app.Updates.CheckMessage);
        Assert.True(_app.Updates.IsBannerVisible);
    }

    [Fact]
    public async Task A_manual_check_runs_even_with_automatic_checks_off_and_just_after_another()
    {
        _app.Updates.CheckAutomatically = false;

        await _app.Updates.CheckCommand.ExecuteAsync();
        await _app.Updates.CheckCommand.ExecuteAsync();

        Assert.Equal(2, _service.Checks);
    }

    [Fact]
    public async Task A_manual_check_counts_toward_the_daily_limit()
    {
        await _app.Updates.CheckCommand.ExecuteAsync();
        await _app.Updates.CheckAtStartupAsync();

        Assert.Equal(1, _service.Checks);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException), "internet connection")]
    [InlineData(typeof(TimeoutException), "internet connection")]
    [InlineData(typeof(TaskCanceledException), "internet connection")]
    [InlineData(typeof(InvalidOperationException), "couldn't check")]
    public async Task A_manual_check_says_plainly_when_it_fails(Type failure, string wording)
    {
        _service.CheckFailure = (Exception)Activator.CreateInstance(failure)!;

        await _app.Updates.CheckCommand.ExecuteAsync();

        Assert.Contains(wording, _app.Updates.CheckMessage);
        Assert.Empty(_app.Dialogs.Errors);
    }

    [Fact]
    public async Task A_manual_check_says_when_github_turns_it_away()
    {
        _service.CheckFailure = new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden);

        await _app.Updates.CheckCommand.ExecuteAsync();

        Assert.Contains("limiting requests", _app.Updates.CheckMessage);
    }

    [Fact]
    public async Task A_failure_is_logged_with_no_path_in_it()
    {
        const string Secret = @"D:\My Private Folder\file.txt";

        _service.CheckFailure = new IOException($"Could not read {Secret}", new IOException(Secret));

        await _app.Updates.CheckCommand.ExecuteAsync();

        Assert.Contains("IOException", LogText);
        Assert.DoesNotContain("Private", LogText);
        Assert.DoesNotContain(@"D:\", LogText);
    }

    [Fact]
    public async Task A_check_does_not_undo_a_setting_changed_while_it_ran()
    {
        _service.CheckGate = new TaskCompletionSource();

        var check = _app.Updates.CheckCommand.ExecuteAsync();

        _app.Settings.SelectedThemeMode = _app.Settings.ThemeModes.Single(choice => choice.Mode == AppThemeMode.Dark);

        _service.CheckGate.SetResult();
        await check;

        Assert.Equal(AppThemeMode.Dark, new SettingsStore(_app.Box.Paths).Load().ThemeMode);
    }

    [Fact]
    public async Task A_bad_settings_file_stops_the_automatic_check_and_is_left_as_it_is()
    {
        const string Bad = """{ "CheckForUpdatesAutomatically": false, "ThemeMode": """;

        var service = new FakeUpdateService { Found = new AvailableUpdate("0.2.0") };

        using var app = new AppHarness(updates: service, prepare: box => File.WriteAllText(box.Paths.SettingsFile, Bad));

        await app.Updates.CheckAtStartupAsync();

        Assert.Equal(0, service.Checks);
        Assert.Equal(Bad, File.ReadAllText(app.Box.Paths.SettingsFile));
        Assert.Equal(PlainEnglish.UpdateCheckSkipped, app.Updates.CheckMessage);
        Assert.False(app.Updates.IsBannerVisible);
        Assert.Contains("skipped", File.ReadAllText(app.Log.Path));
    }

    [Fact]
    public async Task A_bad_settings_file_still_shows_an_update_that_is_already_here()
    {
        var service = new FakeUpdateService { Downloaded = new AvailableUpdate("0.2.0") };

        using var app = new AppHarness(updates: service, prepare: box => File.WriteAllText(box.Paths.SettingsFile, "{ broken"));

        await app.Updates.CheckAtStartupAsync();

        Assert.Equal(UpdateStage.ReadyToRestart, app.Updates.Stage);
    }

    [Fact]
    public async Task A_check_someone_asks_for_runs_and_writes_no_dates_over_a_bad_file()
    {
        var service = new FakeUpdateService();

        using var app = new AppHarness(updates: service, prepare: box => File.WriteAllText(box.Paths.SettingsFile, "{ broken"));

        await app.Updates.CheckCommand.ExecuteAsync();

        Assert.Equal(1, service.Checks);
        Assert.Equal("{ broken", File.ReadAllText(app.Box.Paths.SettingsFile));
        Assert.False(File.Exists(app.Box.Paths.UnreadableSettingsFile));
    }

    [Fact]
    public async Task Changing_a_setting_keeps_a_bad_file_beside_the_new_one()
    {
        var service = new FakeUpdateService();

        using var app = new AppHarness(updates: service, prepare: box => File.WriteAllText(box.Paths.SettingsFile, "{ broken"));

        app.Settings.SelectedThemeMode = app.Settings.ThemeModes.Single(choice => choice.Mode == AppThemeMode.Dark);

        Assert.Equal("{ broken", File.ReadAllText(app.Box.Paths.UnreadableSettingsFile));
        Assert.Equal(AppThemeMode.Dark, new SettingsStore(app.Box.Paths).Load().ThemeMode);

        // There's a good file again, so dates are saved as usual.
        await app.Updates.CheckCommand.ExecuteAsync();

        Assert.NotNull(new SettingsStore(app.Box.Paths).Load().LastUpdateCheck);
    }

    [Fact]
    public async Task Later_hides_the_banner_until_a_check_finds_it_again()
    {
        _service.Found = new AvailableUpdate("0.2.0");
        await _app.Updates.CheckAtStartupAsync();

        _app.Updates.LaterCommand.Execute(null);

        Assert.False(_app.Updates.IsBannerVisible);

        await _app.Updates.CheckCommand.ExecuteAsync();

        Assert.True(_app.Updates.IsBannerVisible);
    }
}

public sealed class UpdateInstallTests : IDisposable
{
    private readonly FakeUpdateService _service = new() { Found = new AvailableUpdate("0.2.0") };
    private readonly AppHarness _app;

    public UpdateInstallTests()
    {
        _app = new AppHarness(updates: _service);
    }

    public void Dispose() => _app.Dispose();

    private async Task DownloadAsync()
    {
        await _app.Updates.CheckAtStartupAsync();
        await _app.Updates.UpdateCommand.ExecuteAsync();
    }

    [Fact]
    public async Task Pressing_update_downloads_and_installs_nothing()
    {
        await DownloadAsync();

        Assert.Equal(UpdateStage.ReadyToRestart, _app.Updates.Stage);
        Assert.True(_app.Updates.ShowRestart);
        Assert.Equal(1, _service.Downloads);
        Assert.Empty(_service.Installs);
        Assert.Empty(_app.Dialogs.Confirmations);
    }

    [Fact]
    public async Task Progress_is_shown_while_it_downloads()
    {
        _service.DownloadGate = new TaskCompletionSource();
        await _app.Updates.CheckAtStartupAsync();

        var download = _app.Updates.UpdateCommand.ExecuteAsync();

        await _service.DownloadStarted.Task;

        Assert.Equal(UpdateStage.Downloading, _app.Updates.Stage);
        Assert.Equal(40, _app.Updates.Progress);
        Assert.Equal("40%", _app.Updates.ProgressText);
        Assert.False(_app.Updates.ShowLater);

        _service.DownloadGate.SetResult();
        await download;

        Assert.Equal(100, _app.Updates.Progress);
    }

    [Fact]
    public async Task Cancel_stops_the_download_and_keeps_the_offer()
    {
        _service.DownloadGate = new TaskCompletionSource();
        await _app.Updates.CheckAtStartupAsync();

        var download = _app.Updates.UpdateCommand.ExecuteAsync();

        await _service.DownloadStarted.Task;
        _app.Updates.CancelCommand.Execute(null);
        await download;

        Assert.Equal(UpdateStage.Available, _app.Updates.Stage);
        Assert.Contains("canceled", _app.Updates.Note);
        Assert.Empty(_service.Installs);
        Assert.Empty(_app.Dialogs.Errors);
    }

    [Theory]
    [InlineData(UpdateFailure.CouldNotConnect, "reach GitHub")]
    [InlineData(UpdateFailure.DamagedDownload, "wasn't used")]
    [InlineData(UpdateFailure.CouldNotSave, "free space")]
    public async Task A_failed_download_is_shown_plainly_and_can_be_tried_again(UpdateFailure kind, string wording)
    {
        _service.DownloadFailure = kind switch
        {
            UpdateFailure.CouldNotConnect => new HttpRequestException("offline"),
            UpdateFailure.DamagedDownload => new ChecksumFailedException(@"C:\Users\someone\package.nupkg"),
            _ => new IOException("Disk full"),
        };

        await DownloadAsync();

        Assert.Equal(UpdateStage.Available, _app.Updates.Stage);
        Assert.Contains(wording, _app.Updates.Note);
        Assert.Empty(_app.Dialogs.Errors);
        Assert.Empty(_service.Installs);
        Assert.True(_app.Updates.UpdateCommand.CanExecute(null));

        _service.DownloadFailure = null;
        await _app.Updates.UpdateCommand.ExecuteAsync();

        Assert.Equal(UpdateStage.ReadyToRestart, _app.Updates.Stage);
        Assert.Null(_app.Updates.Note);
    }

    [Fact]
    public async Task A_damaged_download_logs_no_path()
    {
        _service.DownloadFailure = new ChecksumFailedException(@"C:\Users\someone\package.nupkg");

        await DownloadAsync();

        Assert.DoesNotContain("someone", File.ReadAllText(_app.Log.Path));
    }

    [Fact]
    public async Task A_check_during_a_download_does_nothing()
    {
        _service.DownloadGate = new TaskCompletionSource();
        await _app.Updates.CheckAtStartupAsync();

        var download = _app.Updates.UpdateCommand.ExecuteAsync();

        await _service.DownloadStarted.Task;

        Assert.False(_app.Updates.CheckCommand.CanExecute(null));

        _service.DownloadGate.SetResult();
        await download;

        Assert.True(_app.Updates.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task Update_waits_for_a_check_that_is_still_running()
    {
        await _app.Updates.CheckAtStartupAsync();

        _service.CheckGate = new TaskCompletionSource();

        var check = _app.Updates.CheckCommand.ExecuteAsync();
        var told = 0;

        _app.Updates.UpdateCommand.CanExecuteChanged += (_, _) => told++;

        Assert.False(_app.Updates.UpdateCommand.CanExecute(null));

        await _app.Updates.UpdateCommand.ExecuteAsync();

        Assert.Equal(0, _service.Downloads);

        _service.CheckGate.SetResult();
        await check;

        Assert.True(told > 0);
        Assert.True(_app.Updates.UpdateCommand.CanExecute(null));
        Assert.Equal(UpdateStage.Available, _app.Updates.Stage);
    }

    [Fact]
    public async Task A_check_that_finds_a_newer_version_moves_the_offer_before_any_download()
    {
        await _app.Updates.CheckAtStartupAsync();

        _service.CheckGate = new TaskCompletionSource();

        var check = _app.Updates.CheckCommand.ExecuteAsync();

        await _app.Updates.UpdateCommand.ExecuteAsync();

        _service.Found = new AvailableUpdate("0.3.0");
        _service.CheckGate.SetResult();
        await check;

        Assert.Equal(0, _service.Downloads);
        Assert.Contains("0.3.0", _app.Updates.BannerText);
    }

    [Fact]
    public async Task Restarting_asks_first()
    {
        await DownloadAsync();

        _app.Dialogs.ConfirmAnswer = false;
        _app.Updates.RestartCommand.Execute(null);

        Assert.Single(_app.Dialogs.Confirmations);
        Assert.Empty(_service.Installs);
        Assert.Equal(UpdateStage.ReadyToRestart, _app.Updates.Stage);
    }

    [Fact]
    public async Task A_confirmed_restart_installs_and_closes_the_window()
    {
        var closed = 0;

        _app.Shell.CloseRequested += (_, _) => closed++;

        await DownloadAsync();
        _app.Updates.RestartCommand.Execute(null);

        Assert.Single(_service.Installs);
        Assert.Equal(1, closed);
        Assert.Contains("0.2.0", Assert.Single(_app.Dialogs.ConfirmationMessages));
    }

    [Fact]
    public async Task A_restart_is_refused_while_work_is_running()
    {
        await DownloadAsync();

        _app.Shell.Activity.Begin("Working");

        Assert.False(_app.Updates.RestartCommand.CanExecute(null));
        Assert.Contains("current work", _app.Updates.BannerText);

        _app.Updates.RestartCommand.Execute(null);

        Assert.Empty(_app.Dialogs.Confirmations);
        Assert.Empty(_service.Installs);

        _app.Shell.Activity.Finish("Done");

        Assert.True(_app.Updates.RestartCommand.CanExecute(null));
        Assert.DoesNotContain("current work", _app.Updates.BannerText);
    }

    [Fact]
    public async Task Work_that_starts_behind_the_question_stops_the_restart()
    {
        var closed = 0;

        _app.Shell.CloseRequested += (_, _) => closed++;

        await DownloadAsync();
        _app.Dialogs.WhileConfirming = () => _app.Shell.Activity.Begin("Working");

        _app.Updates.RestartCommand.Execute(null);

        Assert.Empty(_service.Installs);
        Assert.Equal(0, closed);
        Assert.Contains("didn't restart", _app.Updates.Note);
        Assert.Equal(UpdateStage.ReadyToRestart, _app.Updates.Stage);
    }

    [Fact]
    public async Task Another_copy_from_this_install_stops_the_restart_until_it_closes()
    {
        var closed = 0;

        _app.Shell.CloseRequested += (_, _) => closed++;

        await DownloadAsync();
        _service.AnotherCopy = true;

        _app.Updates.RestartCommand.Execute(null);

        Assert.Empty(_service.Installs);
        Assert.Equal(0, closed);
        Assert.Equal(PlainEnglish.UpdateWaitsForOtherCopy, _app.Updates.Note);
        Assert.Equal(UpdateStage.ReadyToRestart, _app.Updates.Stage);

        _service.AnotherCopy = false;
        _app.Updates.RestartCommand.Execute(null);

        Assert.Single(_service.Installs);
        Assert.Equal(1, closed);
    }

    /// <summary>A copy can start behind the question too, so the check comes after it.</summary>
    [Fact]
    public async Task A_copy_that_starts_behind_the_question_stops_the_restart()
    {
        await DownloadAsync();
        _app.Dialogs.WhileConfirming = () => _service.AnotherCopy = true;

        _app.Updates.RestartCommand.Execute(null);

        Assert.Empty(_service.Installs);
    }

    [Fact]
    public async Task The_banner_line_only_speaks_up_when_work_changes_what_it_says()
    {
        await _app.Updates.CheckAtStartupAsync();

        var raised = 0;

        _app.Updates.PropertyChanged += (_, e) => raised += e.PropertyName == nameof(UpdatesViewModel.BannerText) ? 1 : 0;

        _app.Shell.Activity.Begin("Working");
        _app.Shell.Activity.Finish("Done");

        Assert.Equal(0, raised);

        await _app.Updates.UpdateCommand.ExecuteAsync();
        raised = 0;

        _app.Shell.Activity.Begin("Working");
        _app.Shell.Activity.Finish("Done");

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task A_download_that_goes_silent_says_it_lost_github()
    {
        _service.DownloadFailure = new TimeoutException();

        await DownloadAsync();

        Assert.Equal(UpdateStage.Available, _app.Updates.Stage);
        Assert.Contains("reach GitHub", _app.Updates.Note);
    }

    [Fact]
    public async Task An_update_that_cannot_start_keeps_the_app_open()
    {
        var closed = 0;

        _app.Shell.CloseRequested += (_, _) => closed++;
        _service.InstallFailure = new FileNotFoundException("Update.exe is missing.", @"C:\Users\someone\Update.exe");

        await DownloadAsync();
        _app.Updates.RestartCommand.Execute(null);

        Assert.Equal(0, closed);
        Assert.Contains("couldn't be started", _app.Updates.Note);
        Assert.DoesNotContain("someone", File.ReadAllText(_app.Log.Path));
    }

    [Fact]
    public async Task An_update_downloaded_earlier_waits_for_a_confirmed_restart()
    {
        _service.Found = null;
        _service.Downloaded = new AvailableUpdate("0.2.0");

        await _app.Updates.CheckAtStartupAsync();

        Assert.Equal(UpdateStage.ReadyToRestart, _app.Updates.Stage);
        Assert.True(_app.Updates.IsBannerVisible);
        Assert.Empty(_service.Installs);
        Assert.Empty(_app.Dialogs.Confirmations);
    }

    [Fact]
    public async Task Finding_the_version_already_downloaded_does_not_offer_it_again()
    {
        _service.Downloaded = new AvailableUpdate("0.2.0");

        await _app.Updates.CheckAtStartupAsync();

        Assert.Equal(UpdateStage.ReadyToRestart, _app.Updates.Stage);
    }

    [Fact]
    public async Task A_custom_data_folder_comes_back_after_the_restart()
    {
        using var app = new AppHarness(updates: _service, customDataDirectory: @"D:\Trial data");

        await app.Updates.CheckAtStartupAsync();
        await app.Updates.UpdateCommand.ExecuteAsync();
        app.Updates.RestartCommand.Execute(null);

        Assert.Equal(["--data-dir", @"D:\Trial data"], Assert.Single(_service.Installs));
    }

    [Fact]
    public async Task The_default_data_folder_needs_no_argument()
    {
        await DownloadAsync();
        _app.Updates.RestartCommand.Execute(null);

        Assert.Empty(Assert.Single(_service.Installs));
    }
}

public sealed class NotInstalledTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void The_harness_has_no_way_to_the_network() =>
        Assert.IsType<NoUpdateService>(_app.Services.Updates);

    [Fact]
    public async Task A_build_the_installer_did_not_make_says_so_and_offers_the_releases_page()
    {
        await _app.Updates.CheckAtStartupAsync();

        Assert.False(_app.Updates.CanUpdate);
        Assert.True(_app.Updates.CannotUpdate);
        Assert.False(_app.Updates.CheckCommand.CanExecute(null));
        Assert.False(_app.Updates.IsBannerVisible);
        Assert.Contains("aren't available", _app.Updates.NotInstalledMessage);

        _app.Updates.ReleasesPageCommand.Execute(null);

        Assert.Equal("https://github.com/28Y8/Foldcraft/releases", Assert.Single(_app.Dialogs.Links));
    }

    [Fact]
    public async Task It_never_touches_the_settings_or_the_log()
    {
        await _app.Updates.CheckAtStartupAsync();

        Assert.Null(new SettingsStore(_app.Box.Paths).Load().LastUpdateAttempt);
        Assert.False(File.Exists(_app.Log.Path));
    }

    [Fact]
    public async Task A_check_for_a_copy_with_no_updater_finds_nothing()
    {
        var service = new NoUpdateService();

        Assert.False(service.IsInstalled);
        Assert.Null(service.FindDownloaded());
        Assert.Null(await service.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public void A_folder_with_no_updater_above_it_is_not_packaged()
    {
        Assert.False(UpdateHooks.IsPackaged(AppContext.BaseDirectory));
    }

    [Fact]
    public void A_folder_under_the_updater_is_packaged()
    {
        var current = Path.Combine(_app.Box.Root, "current");

        Directory.CreateDirectory(current);
        File.WriteAllBytes(Path.Combine(_app.Box.Root, "Update.exe"), []);

        Assert.True(UpdateHooks.IsPackaged(current));
    }

    [Theory]
    [InlineData(@"C:\Users\Someone\AppData\Local\FoldcraftApp\current\")]
    [InlineData(@"C:\Users\Someone\AppData\Local\FoldcraftApp\current")]
    public void The_install_folder_is_the_one_above_the_app(string appDirectory) =>
        Assert.Equal(@"C:\Users\Someone\AppData\Local\FoldcraftApp", UpdateHooks.InstallFolder(appDirectory));

    [Theory]
    [InlineData(@"C:\Local\FoldcraftApp\current\Foldcraft.exe", true)]
    [InlineData(@"c:\local\foldcraftapp\CURRENT\foldcraft.exe", true)]
    [InlineData(@"C:\Local\FoldcraftApp\Foldcraft.exe", true)]
    [InlineData(@"C:\Local\FoldcraftAppOther\current\Foldcraft.exe", false)]
    [InlineData(@"D:\Portable\Foldcraft.exe", false)]
    [InlineData(null, false)]
    public void A_copy_counts_only_if_it_runs_from_inside_the_install(string? path, bool expected)
    {
        Assert.Equal(expected, UpdateHooks.IsInside(@"C:\Local\FoldcraftApp", path));
        Assert.Equal(expected, UpdateHooks.IsInside(@"C:\Local\FoldcraftApp\", path));
    }

    [Fact]
    public void A_copy_with_no_updater_never_reports_another_copy() =>
        Assert.False(new NoUpdateService().AnotherCopyIsRunning());

    [Fact]
    public void A_source_build_with_no_updater_gets_the_plain_service()
    {
        Assert.IsType<NoUpdateService>(VelopackUpdateService.ForThisApp(_app.Log));
    }
}

public sealed class InstallerHookTests
{
    [Theory]
    [InlineData("--veloapp-install", "0.1.0")]
    [InlineData("--veloapp-updated", "0.1.1")]
    [InlineData("--veloapp-obsolete", "0.1.0")]
    [InlineData("--veloapp-uninstall", "0.1.0")]
    [InlineData("--VeloApp-Uninstall", "0.1.0")]
    [InlineData("--squirrel-install", "0.1.0")]
    public void A_hook_run_is_recognized_so_it_can_end_without_a_window(params string[] args) =>
        Assert.True(UpdateHooks.IsHookRun(args));

    [Fact]
    public void A_normal_start_is_not_a_hook_run() => Assert.False(UpdateHooks.IsHookRun([]));

    [Fact]
    public void The_command_line_options_are_not_hook_runs()
    {
        Assert.False(UpdateHooks.IsHookRun(["--customize", @"D:\Photos"]));
        Assert.False(UpdateHooks.IsHookRun(["--data-dir", @"D:\Trial"]));
        Assert.False(UpdateHooks.IsHookRun(["--customize", @"D:\Photos", "--data-dir", @"D:\Trial"]));
    }

    /// <summary>Whatever a folder is called, the right-click entry always leads with --customize.</summary>
    [Fact]
    public void A_folder_named_like_a_hook_is_still_only_a_folder()
    {
        string[] args = ["--customize", @"D:\--veloapp-uninstall"];

        Assert.False(UpdateHooks.IsHookRun(args));
        Assert.Equal(@"D:\--veloapp-uninstall", AppOptions.Parse(args).CustomizePath);
    }

    [Fact]
    public void The_options_are_read_the_same_as_ever()
    {
        var options = AppOptions.Parse(["--customize", @"D:\Photos", "--data-dir", @"D:\Trial"]);

        Assert.Equal(@"D:\Photos", options.CustomizePath);
        Assert.Equal(@"D:\Trial", options.DataDirectory);
    }

    /// <summary>The installed app starts from Program, so WPF's own Main must not be the one that runs.</summary>
    [Fact]
    public void The_app_starts_from_program_main_on_a_single_threaded_apartment()
    {
        var entry = typeof(Program).Assembly.EntryPoint;

        Assert.NotNull(entry);
        Assert.Equal(typeof(Program), entry.DeclaringType);
        Assert.NotEmpty(entry.GetCustomAttributes(typeof(STAThreadAttribute), inherit: false));
    }
}

public sealed class UpdateSourceTests
{
    [Fact]
    public void The_real_source_is_github_over_https_and_stable_only()
    {
        var source = Assert.IsType<GithubSource>(VelopackUpdateService.SourceFor(null));

        Assert.Equal("https", source.RepoUri.Scheme);
        Assert.Equal("github.com", source.RepoUri.Host);
        Assert.Equal("/28Y8/Foldcraft", source.RepoUri.AbsolutePath);
        Assert.False(source.Prerelease);
    }

    [Fact]
    public void A_build_with_no_test_feed_cannot_be_pointed_anywhere_else()
    {
        Assert.Null(VelopackUpdateService.ReadTestFeed());
    }

    [Fact]
    public void A_test_build_reads_its_local_folder()
    {
        var source = Assert.IsType<SimpleFileSource>(VelopackUpdateService.SourceFor(@"D:\feed"));

        Assert.Equal(@"D:\feed", source.BaseDirectory.FullName);
    }

    [Theory]
    [InlineData("1.2.3", "https://github.com/28Y8/Foldcraft/releases/tag/v1.2.3")]
    [InlineData("0.10.0", "https://github.com/28Y8/Foldcraft/releases/tag/v0.10.0")]
    public void A_plain_version_gets_its_own_page(string version, string expected) =>
        Assert.Equal(expected, ReleaseSite.ReleaseNotesPage(version));

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3-beta")]
    [InlineData("1.2.3/../../evil")]
    [InlineData("")]
    public void Anything_else_gets_no_page(string version) => Assert.Null(ReleaseSite.ReleaseNotesPage(version));

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Failures_sort_into_what_can_be_explained(Exception failure, UpdateFailure expected) =>
        Assert.Equal(expected, UpdateFailures.Classify(failure));

    public static TheoryData<Exception, UpdateFailure> Kinds => new()
    {
        { new HttpRequestException("offline"), UpdateFailure.CouldNotConnect },
        { new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden), UpdateFailure.ServiceRefused },
        { new HttpRequestException("Missing", null, HttpStatusCode.NotFound), UpdateFailure.ServiceRefused },
        { new TimeoutException(), UpdateFailure.CouldNotConnect },
        { new TaskCanceledException(), UpdateFailure.CouldNotConnect },
        { new ChecksumFailedException("x"), UpdateFailure.DamagedDownload },
        { new HttpIOException(HttpRequestError.ResponseEnded), UpdateFailure.CouldNotConnect },
        { new IOException("reset", new SocketException((int)SocketError.ConnectionReset)), UpdateFailure.CouldNotConnect },
        { new IOException("Received an unexpected EOF") { Source = "System.Net.Security" }, UpdateFailure.CouldNotConnect },
        { new IOException("decryption failed", new Win32Exception()) { Source = "System.Net.Security" }, UpdateFailure.CouldNotConnect },
        { new IOException(), UpdateFailure.CouldNotSave },
        { new IOException("Disk full") { Source = "System.Private.CoreLib" }, UpdateFailure.CouldNotSave },
        { new IOException("wrapped", new InvalidOperationException()), UpdateFailure.CouldNotSave },
        { new UnauthorizedAccessException(), UpdateFailure.CouldNotSave },
        { new InvalidOperationException(), UpdateFailure.Unknown },
    };

    [Fact]
    public void A_folder_or_link_opens_away_from_the_app_folder()
    {
        foreach (var target in new[] { ReleaseSite.ReleasesPage, @"C:\data" })
        {
            var start = WpfDialogs.StartInfoFor(target);

            Assert.Equal(target, start.FileName);
            Assert.True(start.UseShellExecute);
            Assert.True(Directory.Exists(start.WorkingDirectory));
            Assert.False(UpdateHooks.IsInside(UpdateHooks.InstallFolder(AppContext.BaseDirectory), start.WorkingDirectory + Path.DirectorySeparatorChar));
        }
    }

    [Fact]
    public void A_log_line_keeps_the_kind_and_drops_the_message()
    {
        var line = UpdateFailures.ForLog(new HttpRequestException(@"C:\Users\someone", null, HttpStatusCode.Forbidden));

        Assert.Contains("HttpRequestException", line);
        Assert.Contains("403", line);
        Assert.DoesNotContain("someone", line);
    }
}

/// <summary>The data folder holds what customized folders depend on, so the installer must stay clear of it.</summary>
public sealed class InstallRootTests
{
    private static string RepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([UpdateRepository.Root(), .. parts]));

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    [Fact]
    public void The_install_folder_is_never_the_data_folder()
    {
        var install = Path.GetFullPath(ReleaseSite.InstallRoot(LocalAppData));
        var data = FoldcraftPaths.Default().DataDirectory;

        Assert.NotEqual(install, data, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Neither_folder_sits_inside_the_other()
    {
        var install = Path.GetFullPath(ReleaseSite.InstallRoot(LocalAppData)) + Path.DirectorySeparatorChar;
        var data = FoldcraftPaths.Default().DataDirectory + Path.DirectorySeparatorChar;

        Assert.False(data.StartsWith(install, StringComparison.OrdinalIgnoreCase));
        Assert.False(install.StartsWith(data, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Setup renames an existing install to the same path with a random extension, so a dot in
    /// the id would put that rename on a name built from "Foldcraft" alone.
    /// </summary>
    [Fact]
    public void The_id_has_no_dot()
    {
        Assert.DoesNotContain('.', ReleaseSite.PackageId);
    }

    [Fact]
    public void The_build_and_the_app_agree_on_the_id()
    {
        Assert.Contains($"<FoldcraftPackageId>{ReleaseSite.PackageId}</FoldcraftPackageId>", RepositoryFile("Directory.Build.props"));
    }

    [Fact]
    public void The_packaging_script_uses_that_id()
    {
        var script = RepositoryFile("tools", "package.ps1");

        Assert.Contains("Get-ProjectProperty 'FoldcraftPackageId'", script);
        Assert.Contains("'--packId', $packageId", script);
    }

    [Fact]
    public void The_installer_adds_a_start_menu_shortcut_and_nothing_on_the_desktop()
    {
        Assert.Contains("'--shortcuts', 'StartMenuRoot'", RepositoryFile("tools", "package.ps1"));
    }

    [Fact]
    public void The_library_and_the_tool_are_pinned_to_the_same_exact_version()
    {
        var project = RepositoryFile("src", "Foldcraft.App", "Foldcraft.App.csproj");
        var tools = RepositoryFile("dotnet-tools.json");

        const string Pin = "1.2.161";

        Assert.Contains($"<PackageReference Include=\"Velopack\" Version=\"{Pin}\" />", project);
        Assert.Contains($"\"version\": \"{Pin}\"", tools);
    }

    [Fact]
    public void A_release_can_never_be_built_with_a_test_feed()
    {
        Assert.DoesNotContain("TestUpdateFeed", RepositoryFile(".github", "workflows", "release.yml"));
    }

    /// <summary>What runs restore, tests and packaging must never hold a token that can write.</summary>
    [Fact]
    public void Only_the_step_that_publishes_can_write_to_the_repository()
    {
        var workflow = RepositoryFile(".github", "workflows", "release.yml").ReplaceLineEndings("\n");
        var build = workflow[workflow.IndexOf("  build:\n", StringComparison.Ordinal)..workflow.IndexOf("  publish:\n", StringComparison.Ordinal)];
        var publish = workflow[workflow.IndexOf("  publish:\n", StringComparison.Ordinal)..];

        Assert.Equal(2, Regex.Count(workflow, @"uses: actions/checkout@"));
        Assert.Equal(2, Regex.Count(workflow, @"persist-credentials: false"));
        Assert.Equal(1, Regex.Count(workflow, @"contents: write"));
        Assert.Contains("contents: write", publish);
        Assert.Contains("contents: read", workflow[..workflow.IndexOf("jobs:", StringComparison.Ordinal)]);
        Assert.DoesNotContain("permissions:", build);

        foreach (var step in new[] { "dotnet test", "dotnet restore", "package.ps1", "dotnet build" })
        {
            Assert.DoesNotContain(step, publish);
        }
    }

    [Fact]
    public void Restores_are_pinned_by_hash()
    {
        var props = RepositoryFile("Directory.Build.props");

        Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", props);
        Assert.Contains("<RestoreLockedMode Condition=\"'$(CI)' == 'true'\">true</RestoreLockedMode>", props);

        foreach (var project in new[] { @"src\Foldcraft.Core", @"src\Foldcraft.App", @"tests\Foldcraft.Tests" })
        {
            Assert.True(File.Exists(Path.Combine(UpdateRepository.Root(), project, "packages.lock.json")), $"{project} has no lock file.");
        }
    }

    [Fact]
    public void The_release_workflow_keeps_the_tag_check_and_pinned_actions()
    {
        var workflow = RepositoryFile(".github", "workflows", "release.yml");

        Assert.Contains("Check the tag matches the project version", workflow);
        Assert.Contains("vpk", workflow);

        foreach (var line in workflow.Split('\n').Where(line => line.TrimStart().StartsWith("- uses:", StringComparison.Ordinal)))
        {
            Assert.Matches(@"@[0-9a-f]{40} # v", line);
        }
    }
}

public sealed class UninstallCleanupTests : IDisposable
{
    private const string Exe = @"C:\Users\Someone\AppData\Local\FoldcraftApp\current\Foldcraft.exe";
    private const string OtherCopy = @"D:\Portable\Foldcraft.exe";

    private readonly Sandbox _box = new();
    private readonly FakeRegistry _registry = new();
    private readonly RightClickMenu _menu;

    public UninstallCleanupTests()
    {
        _menu = new RightClickMenu(_registry);
    }

    public void Dispose() => _box.Dispose();

    private SettingsStore Store => new(_box.Paths);

    private RightClickResult Uninstall() => UninstallCleanup.RemoveRightClick(_registry, Store, Exe);

    [Fact]
    public void Uninstalling_takes_the_entry_off()
    {
        _menu.Enable(Exe);

        Assert.Equal(RightClickOutcome.Removed, Uninstall().Outcome);
        Assert.Empty(_registry.Keys);
    }

    [Fact]
    public void An_entry_turned_on_with_a_custom_data_folder_goes_too()
    {
        _menu.Enable(Exe, @"D:\Trial");

        Assert.Equal(RightClickOutcome.Removed, Uninstall().Outcome);
        Assert.Empty(_registry.Keys);
    }

    [Fact]
    public void Keys_made_for_the_entry_go_with_it()
    {
        _registry.MakesParentKeys = true;

        var added = _menu.Enable(Exe);

        Store.Save(new AppSettings { RightClick = new RightClickRecord(added.Command!, Exe, added.AddedParents) });

        Uninstall();

        Assert.Empty(_registry.Keys);
    }

    [Fact]
    public void An_entry_for_another_copy_stays()
    {
        _menu.Enable(OtherCopy);

        Assert.Equal(RightClickOutcome.LeftAlone, Uninstall().Outcome);
        Assert.True(_registry.KeyExists(RightClickMenu.VerbKey));
    }

    [Fact]
    public void Something_else_using_that_name_stays()
    {
        _registry.SetString(RightClickMenu.VerbKey + @"\command", null, "other.exe \"%1\"");

        Assert.Equal(RightClickOutcome.LeftAlone, Uninstall().Outcome);
        Assert.Equal("other.exe \"%1\"", _registry.GetString(RightClickMenu.VerbKey + @"\command", null));
    }

    [Fact]
    public void With_no_entry_there_is_nothing_to_do()
    {
        Assert.Equal(RightClickOutcome.NothingThere, Uninstall().Outcome);
        Assert.Equal(0, _registry.Writes);
    }

    [Fact]
    public void A_registry_that_refuses_is_not_a_crash()
    {
        _menu.Enable(Exe);
        _registry.FailWrites = true;

        Assert.NotNull(Uninstall());
    }

    [Fact]
    public void Nothing_in_the_data_folder_changes()
    {
        var added = _menu.Enable(Exe);

        Store.Save(new AppSettings { RightClick = new RightClickRecord(added.Command!, Exe) });
        File.WriteAllText(_box.Paths.JournalFile, "history");

        var before = Snapshot();

        Uninstall();

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_data_folder_that_was_never_made_stays_unmade()
    {
        Directory.Delete(_box.Paths.DataDirectory);

        Uninstall();

        Assert.False(Directory.Exists(_box.Paths.DataDirectory));
    }

    private string[] Snapshot() =>
        Directory.EnumerateFileSystemEntries(_box.Paths.DataDirectory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{(File.Exists(path) ? Convert.ToHexString(File.ReadAllBytes(path)) : string.Empty)}")
            .ToArray();
}

public sealed class RightClickAcrossUpdatesTests : IDisposable
{
    private readonly FakeUpdateService _service = new();

    private AppHarness? _app;

    public void Dispose() => _app?.Dispose();

    private AppHarness Start(RightClickRecord? record, bool installed = true)
    {
        _service.IsInstalled = installed;
        _app = new AppHarness(
            updates: _service,
            prepare: box => new SettingsStore(box.Paths).Save(new AppSettings { RightClick = record }));

        return _app;
    }

    private static RightClickRecord RecordFor(string exePath) => new(RightClickMenu.BuildCommand(exePath)!, exePath);

    [Fact]
    public void An_entry_that_is_in_place_is_left_exactly_as_it_is()
    {
        var app = Start(RecordFor(AppHarness.FakeExecutable));

        new RightClickMenu(app.Registry).Enable(AppHarness.FakeExecutable);

        var writes = app.Registry.Writes;

        app.Settings.RepairRightClick();

        Assert.Equal(writes, app.Registry.Writes);
        Assert.True(app.Settings.IsRightClickEnabled);
        Assert.Null(app.Settings.RightClickMessage);
    }

    [Fact]
    public void An_entry_pointing_at_a_program_that_is_gone_moves_to_this_one()
    {
        const string Gone = @"C:\Nowhere\app-0.1.0\Foldcraft.exe";

        var app = Start(RecordFor(Gone));

        new RightClickMenu(app.Registry).Enable(Gone);

        app.Settings.RepairRightClick();

        Assert.False(app.Settings.RightClickIsOutdated);
        Assert.Equal(
            RightClickMenu.BuildCommand(AppHarness.FakeExecutable),
            app.Registry.GetString(RightClickMenu.VerbKey + @"\command", null));
    }

    [Fact]
    public void An_entry_for_another_copy_that_still_exists_stays_the_persons_choice()
    {
        var other = Path.Combine(AppContext.BaseDirectory, "Foldcraft.Tests.dll");
        var app = Start(RecordFor(other));

        new RightClickMenu(app.Registry).Enable(other);

        app.Settings.RepairRightClick();

        Assert.True(app.Settings.RightClickIsOutdated);
        Assert.Equal(RightClickMenu.BuildCommand(other), app.Registry.GetString(RightClickMenu.VerbKey + @"\command", null));
    }

    [Fact]
    public void After_an_uninstall_and_a_new_install_the_switch_tells_the_truth()
    {
        var app = Start(RecordFor(AppHarness.FakeExecutable));

        app.Settings.RepairRightClick();

        Assert.False(app.Settings.IsRightClickEnabled);
        Assert.Equal(PlainEnglish.RightClickGone, app.Settings.RightClickMessage);
        Assert.Null(new SettingsStore(app.Box.Paths).Load().RightClick);
        Assert.Equal(0, app.Registry.Writes);
    }

    [Fact]
    public void A_copy_that_was_not_installed_does_not_read_the_registry()
    {
        var app = Start(RecordFor(AppHarness.FakeExecutable), installed: false);

        app.Settings.RepairRightClick();

        Assert.Equal(0, app.Registry.Reads + app.Registry.Writes);
    }

    [Fact]
    public void With_the_entry_off_nothing_reads_the_registry()
    {
        var app = Start(record: null);

        app.Settings.RepairRightClick();

        Assert.Equal(0, app.Registry.Reads + app.Registry.Writes);
    }

    [Fact]
    public async Task Starting_up_makes_the_repair()
    {
        var app = Start(RecordFor(AppHarness.FakeExecutable));

        await app.Shell.InitializeAsync();

        Assert.False(app.Settings.IsRightClickEnabled);
    }

    [Fact]
    public void A_registry_that_cannot_be_read_changes_nothing()
    {
        var menu = new RightClickMenu(new UnreadableRegistry());

        Assert.True(menu.IsInPlace("command", AppHarness.FakeExecutable));
        Assert.Equal(RightClickOutcome.Failed, menu.DisableFor(AppHarness.FakeExecutable).Outcome);
    }

    private sealed class UnreadableRegistry : IUserRegistry
    {
        public string? GetString(string subKey, string? name) => throw new UnauthorizedAccessException();

        public void SetString(string subKey, string? name, string value) => throw new UnauthorizedAccessException();

        public void DeleteValue(string subKey, string? name) => throw new UnauthorizedAccessException();

        public bool KeyExists(string subKey) => throw new UnauthorizedAccessException();

        public (IReadOnlyList<string> Values, IReadOnlyList<string> SubKeys) Contents(string subKey) => throw new UnauthorizedAccessException();

        public void DeleteEmptyKey(string subKey) => throw new UnauthorizedAccessException();
    }
}

public sealed class UpdateWordingTests
{
    public static TheoryData<string> Docs =>
    [
        "README.md", "SECURITY.md", "CHANGELOG.md", @"docs\safety.md", @"docs\how-it-works.md",
    ];

    [Theory]
    [MemberData(nameof(Docs))]
    public void No_document_still_says_there_is_no_network_code(string file)
    {
        var text = File.ReadAllText(Path.Combine(UpdateRepository.Root(), file));

        Assert.DoesNotContain("no network code", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("There's no installer", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData(@"docs\safety.md")]
    [InlineData("SECURITY.md")]
    public void The_documents_say_what_the_update_check_does(string file)
    {
        var text = File.ReadAllText(Path.Combine(UpdateRepository.Root(), file));

        Assert.Contains("GitHub", text);
        Assert.Contains("IP address", text);
        Assert.Contains("SmartScreen", text);
    }

    [Fact]
    public void The_privacy_wording_covers_every_promise()
    {
        Assert.Contains("only to check for and download updates", PlainEnglish.UpdatePrivacy);
        Assert.Contains("no personal data", PlainEnglish.UpdatePrivacy);
        Assert.Contains("IP address", PlainEnglish.UpdatePrivacy);
        Assert.Contains("turn the check off", PlainEnglish.UpdatePrivacy);
    }

    [Fact]
    public void Settings_says_what_stays_after_an_uninstall()
    {
        Assert.Contains("keep their look", PlainEnglish.WhatUninstallLeaves);
        Assert.Contains("data folder stays", PlainEnglish.WhatUninstallLeaves);
        Assert.Contains("Remove All Changes before you uninstall", PlainEnglish.WhatUninstallLeaves);
    }

    [Fact]
    public void The_about_screen_says_it_too()
    {
        var about = new AboutViewModel(new FakeDialogs(), @"C:\data");

        Assert.Equal(PlainEnglish.UpdatePrivacy, about.UpdatesNotice);
    }

    [Fact]
    public void Every_failure_has_wording()
    {
        foreach (var failure in Enum.GetValues<UpdateFailure>())
        {
            Assert.False(string.IsNullOrWhiteSpace(PlainEnglish.UpdateCheckFailed(failure)));
            Assert.False(string.IsNullOrWhiteSpace(PlainEnglish.UpdateDownloadFailed(failure)));
        }
    }

    /// <summary>A stopped download can delete packages, and a finished one has already replaced the updater.</summary>
    [Fact]
    public void Nothing_claims_that_nothing_changed()
    {
        var lines = Enum.GetValues<UpdateFailure>().Select(PlainEnglish.UpdateDownloadFailed)
            .Concat([PlainEnglish.UpdateDownloadCanceled, PlainEnglish.UpdateInstallFailed]);

        Assert.All(lines, line => Assert.DoesNotContain("nothing has changed", line, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The updater is replaced by a finished download, ahead of the restart.</summary>
    [Theory]
    [InlineData("README.md")]
    [InlineData("SECURITY.md")]
    [InlineData("CHANGELOG.md")]
    [InlineData(@"docs\safety.md")]
    [InlineData(@"docs\how-it-works.md")]
    public void The_documents_say_when_the_updater_changes(string file)
    {
        var text = File.ReadAllText(Path.Combine(UpdateRepository.Root(), file));

        Assert.Contains("Update.exe", text);
        Assert.DoesNotContain("nothing installs until", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("or installs until you press **Update** and agree", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void How_it_works_does_not_promise_a_check_on_the_rebuilt_package()
    {
        var text = File.ReadAllText(Path.Combine(UpdateRepository.Root(), "docs", "how-it-works.md"));

        Assert.DoesNotContain("Either way the result is checked", text);
        Assert.Contains("isn't checked again as a whole", text);
    }
}
