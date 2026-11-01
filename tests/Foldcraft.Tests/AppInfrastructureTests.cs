using System.Globalization;
using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.App.ViewModels;
using Foldcraft.App.Views;
using Foldcraft.Core.Icons;

namespace Foldcraft.Tests;

public sealed class CommandTests
{
    [Fact]
    public async Task An_async_command_cannot_run_twice_at_once()
    {
        var release = new TaskCompletionSource();
        var runs = 0;
        var command = new AsyncRelayCommand(async () => { runs++; await release.Task; }, _ => { });

        var first = command.ExecuteAsync();

        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, runs);

        release.SetResult();
        await first;

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task An_async_command_hands_failures_to_its_error_handler()
    {
        Exception? seen = null;
        var failure = new InvalidOperationException("boom");
        var command = new AsyncRelayCommand(() => Task.FromException(failure), ex => seen = ex);

        await command.ExecuteAsync();

        Assert.Same(failure, seen);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task An_async_command_catches_a_synchronous_throw()
    {
        Exception? seen = null;
        var command = new AsyncRelayCommand(new Func<Task>(() => throw new InvalidOperationException()), ex => seen = ex);

        await command.ExecuteAsync();

        Assert.IsType<InvalidOperationException>(seen);
    }

    [Fact]
    public void A_disabled_command_does_nothing()
    {
        var runs = 0;
        var command = new RelayCommand(() => runs++, () => false);

        command.Execute(null);

        Assert.Equal(0, runs);
    }

    [Fact]
    public void Changing_a_command_notifies_listeners()
    {
        var command = new RelayCommand(() => { });
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        command.RaiseCanExecuteChanged();

        Assert.Equal(1, raised);
    }
}

public sealed class ActivityViewModelTests
{
    [Fact]
    public void Only_one_piece_of_work_can_run()
    {
        var activity = new ActivityViewModel();
        activity.Begin("first");

        Assert.Throws<InvalidOperationException>(() => activity.Begin("second"));
    }

    [Fact]
    public void Progress_counts_up_and_finishing_shows_the_summary()
    {
        var activity = new ActivityViewModel();
        activity.Begin("Working", 3);

        Assert.False(activity.IsIndeterminate);
        activity.Advance();
        Assert.Equal("1 of 3", activity.ProgressText);

        activity.Finish("All done.");

        Assert.False(activity.IsBusy);
        Assert.Equal("All done.", activity.Message);
        Assert.Equal(string.Empty, activity.ProgressText);
    }

    [Fact]
    public void Work_with_no_known_size_is_indeterminate()
    {
        var activity = new ActivityViewModel();
        activity.Begin("Working");

        Assert.True(activity.IsIndeterminate);
    }

    [Fact]
    public void Canceling_fires_the_token_once_and_says_so()
    {
        var activity = new ActivityViewModel();
        var token = activity.Begin("Working", 2, cancellable: true);

        Assert.True(activity.CancelCommand.CanExecute(null));
        activity.CancelCommand.Execute(null);

        Assert.True(token.IsCancellationRequested);
        Assert.False(activity.CanCancel);
        Assert.Contains("Stopping", activity.Message);
    }

    [Fact]
    public void Work_that_cannot_be_canceled_ignores_cancel()
    {
        var activity = new ActivityViewModel();
        var token = activity.Begin("Working", 2);

        activity.Cancel();

        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public void Busy_changes_are_announced()
    {
        var activity = new ActivityViewModel();
        var seen = new List<bool>();
        activity.BusyChanged += (_, _) => seen.Add(activity.IsBusy);

        activity.Begin("Working");
        activity.Finish("Done");

        Assert.Equal([true, false], seen);
    }
}

public sealed class AppOptionsTests
{
    [Fact]
    public void No_arguments_means_the_default_data_directory()
    {
        var paths = AppOptions.Parse([]).ResolvePaths();

        Assert.EndsWith(@"\Foldcraft", paths.DataDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_data_directory_can_be_redirected()
    {
        var options = AppOptions.Parse(["--other", "--DATA-DIR", @"C:\Temp\x"]);

        Assert.Equal(@"C:\Temp\x", options.DataDirectory);
        Assert.Equal(@"C:\Temp\x", options.ResolvePaths().DataDirectory);
    }

    [Theory]
    [InlineData("--data-dir")]
    [InlineData("--data-dir", " ")]
    [InlineData("--unknown", "x")]
    public void Unusable_arguments_are_ignored(params string[] args) =>
        Assert.Null(AppOptions.Parse(args).DataDirectory);

    [Fact]
    public void A_relative_data_directory_is_pinned_down_before_the_working_folder_moves()
    {
        var options = AppOptions.Parse(["--data-dir", "trial", "--customize", "here"]).WithFullPaths();

        Assert.Equal(Path.GetFullPath("trial"), options.DataDirectory);
        Assert.Equal("here", options.CustomizePath);
    }

    [Fact]
    public void Pinning_down_nothing_leaves_nothing()
    {
        Assert.Null(AppOptions.Parse([]).WithFullPaths().DataDirectory);
    }
}

public sealed class FileLogTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    [Fact]
    public void Writes_the_message_and_the_exception_creating_the_directory_if_needed()
    {
        var path = Path.Combine(_box.Root, "new", "foldcraft.log");

        new FileLog(path).Write("Something happened", new InvalidOperationException("the detail"));

        var text = File.ReadAllText(path);
        Assert.Contains("Something happened", text);
        Assert.Contains("the detail", text);
    }

    [Fact]
    public void A_full_log_rolls_over_rather_than_growing()
    {
        var path = Path.Combine(_box.Root, "foldcraft.log");
        var log = new FileLog(path, maxBytes: 64);

        log.Write(new string('a', 100));
        log.Write("second");

        Assert.True(File.Exists(path + ".old"));
        Assert.DoesNotContain("aaaa", File.ReadAllText(path));
    }

    [Fact]
    public void An_unwritable_log_never_throws()
    {
        var path = Path.Combine(_box.Root, "file-not-folder");
        File.WriteAllText(path, string.Empty);

        new FileLog(Path.Combine(path, "foldcraft.log")).Write("nowhere to go");
    }
}

public sealed class CrashGuardTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private string LogText => File.ReadAllText(_box.Paths.LogFile);

    [Fact]
    public void A_failure_is_logged_in_full_and_shown_kindly()
    {
        var shown = new List<string>();
        var guard = new CrashGuard(new FileLog(_box.Paths.LogFile), shown.Add);

        guard.Report("Testing", new InvalidOperationException("secret detail"));

        Assert.Contains("secret detail", LogText);
        var message = Assert.Single(shown);
        Assert.DoesNotContain("secret detail", message);
        Assert.Contains(_box.Paths.LogFile, message);
    }

    [Fact]
    public void A_second_failure_while_the_message_is_up_is_logged_but_not_shown()
    {
        var shown = 0;
        CrashGuard? guard = null;

        guard = new CrashGuard(new FileLog(_box.Paths.LogFile), _ =>
        {
            shown++;
            guard!.Report("Nested", new InvalidOperationException("second"));
        });

        guard.Report("Outer", new InvalidOperationException("first"));

        Assert.Equal(1, shown);
        Assert.Contains("second", LogText);
    }

    [Fact]
    public void A_message_that_cannot_be_shown_does_not_become_a_new_crash()
    {
        var guard = new CrashGuard(new FileLog(_box.Paths.LogFile), _ => throw new InvalidOperationException("no window"));

        guard.Report("Testing", new InvalidOperationException("first"));

        Assert.Contains("no window", LogText);
    }
}

public sealed class AboutViewModelTests
{
    [Fact]
    public void Shows_the_version_license_and_what_it_does_not_touch()
    {
        var about = new AboutViewModel(new FakeDialogs(), @"C:\data");

        Assert.Equal("Version 0.1.0", about.Version);
        Assert.Contains("MIT", about.License);
        Assert.Contains("never", about.Explanation);
        Assert.Contains("your files", about.Explanation);
        Assert.DoesNotContain("desktop.ini", about.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(@"C:\data", about.DataDirectory);
    }

    [Fact]
    public void Says_what_it_adds_as_well_as_what_it_does_not_touch()
    {
        var about = new AboutViewModel(new FakeDialogs(), @"C:\data");

        Assert.DoesNotContain("nothing else", about.Explanation);
        Assert.Contains("Apps and Games", about.Explanation);
        Assert.Contains("shortcut", about.Explanation);
    }

    [Fact]
    public void Links_open_through_the_dialog_service()
    {
        var dialogs = new FakeDialogs();
        var about = new AboutViewModel(dialogs, @"C:\data");

        about.OpenProjectCommand.Execute(null);
        about.OpenLicenseCommand.Execute(null);

        Assert.Equal([AboutViewModel.ProjectUrl, AboutViewModel.ProjectUrl + "/blob/main/LICENSE"], dialogs.Links);
    }
}

public sealed class ConverterTests
{
    public static TheoryData<byte, byte, byte> Accents() => new()
    {
        { 255, 185, 0 },    // Windows gold
        { 0, 120, 212 },    // Windows blue
        { 255, 255, 0 },
        { 0, 0, 0 },
        { 255, 255, 255 },
        { 118, 118, 118 },  // The mid gray where black and white are closest
        { 136, 136, 136 },
        { 232, 17, 35 },
    };

    [Theory]
    [MemberData(nameof(Accents))]
    public void Text_on_any_accent_color_is_readable(byte r, byte g, byte b)
    {
        var background = System.Windows.Media.Color.FromRgb(r, g, b);
        var text = ((System.Windows.Media.SolidColorBrush)ReadableTextConverter.Pick(background)).Color;

        Assert.True(ReadableTextConverter.ContrastWith(background, text) >= 4.5);
    }

    [Fact]
    public void The_app_picks_dark_text_on_gold()
    {
        var gold = System.Windows.Media.Color.FromRgb(224, 160, 0);

        Assert.Equal(System.Windows.Media.Brushes.Black, ReadableTextConverter.Pick(gold));
    }

    [Fact]
    public void A_hex_color_becomes_a_brush_and_nonsense_does_not_throw()
    {
        var converter = new HexToBrushConverter();

        Assert.Equal(
            System.Windows.Media.Color.FromRgb(0x2F, 0x6F, 0xED),
            ((System.Windows.Media.SolidColorBrush)converter.Convert("#2F6FED", typeof(object), null, CultureInfo.InvariantCulture)!).Color);
        Assert.Equal(System.Windows.Media.Brushes.Transparent, converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Rendered_bytes_become_an_image_at_their_true_size()
    {
        var png = new IconRenderer().RenderPreviewPng(Sandbox.BlueStar, 32);

        var image = (System.Windows.Media.Imaging.BitmapSource)new PngToImageConverter().Convert(png, typeof(object), null, CultureInfo.InvariantCulture)!;

        Assert.Equal(32, image.PixelWidth);
        Assert.True(image.IsFrozen);
        Assert.Null(new PngToImageConverter().Convert(null, typeof(object), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_library_icon_loads_at_32_pixels_and_a_bad_one_shows_nothing()
    {
        using var box = new Sandbox();
        var converter = new IconFileToImageConverter();
        var icon = box.Icons.Ensure(Sandbox.BlueStar);
        var bad = Path.Combine(box.Root, "bad.ico");
        File.WriteAllText(bad, "not an icon");

        var image = (System.Windows.Media.Imaging.BitmapSource)converter.Convert(icon, typeof(object), null, CultureInfo.InvariantCulture)!;

        Assert.Equal(32, image.PixelWidth);
        Assert.Null(converter.Convert(bad, typeof(object), null, CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert(Path.Combine(box.Root, "missing.ico"), typeof(object), null, CultureInfo.InvariantCulture));
    }
}

public sealed class AppIconTests
{
    private static string IconPath([System.Runtime.CompilerServices.CallerFilePath] string testFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", "src", "Foldcraft.App", "Assets", "Foldcraft.ico"));

    [Fact]
    public void The_committed_icon_is_what_our_renderer_draws()
    {
        var expected = new IconRenderer().RenderIco(AppIcon.Spec);

        if (Environment.GetEnvironmentVariable("FOLDCRAFT_WRITE_APP_ICON") == "1")
        {
            File.WriteAllBytes(IconPath(), expected);
        }

        Assert.True(
            expected.AsSpan().SequenceEqual(File.ReadAllBytes(IconPath())),
            "Assets\\Foldcraft.ico is out of date. Run this test with FOLDCRAFT_WRITE_APP_ICON=1 to regenerate it.");
    }
}
