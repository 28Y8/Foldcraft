using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Foldcraft.App;
using Foldcraft.App.Services;
using Foldcraft.App.ViewModels;
using Foldcraft.App.Views;

namespace Foldcraft.Tests;

/// <summary>
/// Builds the real window around the real view-models in a sandbox, with no folder outside
/// it. WPF allows one Application per process, so this is one test with several stages.
/// Setting FOLDCRAFT_SCREENSHOT_DIR saves a picture of each stage, and of every screen in light
/// and dark at the smallest window and at the size the window opens at.
/// </summary>
public sealed partial class WindowTests
{
    private const int MinimumWidth = 900;
    private const int MinimumHeight = 600;
    private const int OpeningWidth = 1200;
    private const int OpeningHeight = 780;

    private static readonly (int Tab, string Name)[] Screens =
    [
        (ShellViewModel.CustomizeTab, "customize"), (ShellViewModel.HistoryTab, "history"), (ShellViewModel.AppsTab, "apps"),
        (ShellViewModel.ThemesTab, "themes"), (ShellViewModel.RulesTab, "rules"), (ShellViewModel.SettingsTab, "settings"),
        (ShellViewModel.AboutTab, "about"),
    ];

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Lines { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                Lines.Add(message);
            }
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Desktop)]
    public void The_window_works_in_light_and_dark_at_the_smallest_size()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await RunAsync();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromMinutes(4)), "The window test did not finish.");

        if (failure is not null)
        {
            throw new InvalidOperationException("The window test failed.", failure);
        }
    }

    private static async Task RunAsync()
    {
        using var harness = new AppHarness();

        var errors = new BindingErrors();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        var app = new App.App();
        app.InitializeComponent();

        var window = new MainWindow
        {
            DataContext = harness.Shell,
            Width = MinimumWidth,
            Height = MinimumHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
        };

        window.Show();
        await harness.Shell.InitializeAsync();
        await Settle();

        await CheckEveryScreen(window, app, "empty");
        harness.Shell.SelectedTab = ShellViewModel.CustomizeTab;
        await Settle();

        await CheckHoverComesAndGoes(window);
        await CheckArrowKeysMoveThroughTheRail(window, harness);
        CheckEveryChoiceHasAName(window);
        await CheckArrowKeysMoveThroughThePalette(window, harness);
        await CheckDropAddsFolders(window, harness);
        await CheckPrompts();
        await CheckTheUpdateBanner();

        await CheckAnUnusableImageIsAnnounced(window, harness);

        var folders = new[] { harness.Box.NewFolder("Holiday Photos"), harness.Box.NewFolder("Client Work") };
        await harness.Customize.AddPathsAsync([.. folders, harness.Box.FakeProfile, harness.Box.FakeAppData]);
        harness.Designer.IsGradient = true;
        harness.Designer.SelectedGlyph = harness.Designer.Glyphs[1];
        await harness.Designer.PreviewTask;
        await Settle();

        foreach (var (mode, name) in new[] { (ThemeMode.Light, "light"), (ThemeMode.Dark, "dark") })
        {
            app.ThemeMode = mode;
            await Settle();

            CheckTextContrast(name);
            CheckLayoutFits(window, name);
            Screenshot(window, $"customize-{name}");
        }

        await harness.Customize.ApplyCommand.ExecuteAsync();
        harness.Designer.SelectedSwatch = harness.Designer.Swatches[7];
        await harness.Designer.PreviewTask;
        harness.Designer.SelectedSwatch = harness.Designer.Swatches[9];
        await harness.Designer.PreviewTask;
        await harness.Customize.ApplyCommand.ExecuteAsync();
        await harness.History.RefreshAsync();

        harness.Shell.SelectedTab = 1;
        await Settle();
        harness.History.Entries[0].IsExpanded = true;
        await Settle();

        CheckEveryIconButtonIsNamed(window);
        CheckNoJargon(window);
        Screenshot(window, "history-dark");

        var announced = new List<object?>();
        LiveRegion.Announced += (sender, _) => announced.Add(sender);

        var undone = harness.History.Entries[1].BatchId;
        await harness.History.Entries[1].UndoCommand.ExecuteAsync();
        await Settle();
        CheckNoJargon(window);
        CheckFocusLandedOnTheEntry(window, undone);
        CheckStatusLineIsReadable(window, harness);
        Assert.Contains(StatusLine(window), announced);
        Screenshot(window, "history-results-dark");

        foreach (var (tab, name) in new[]
                 {
                     (ShellViewModel.ThemesTab, "themes"), (ShellViewModel.RulesTab, "rules"),
                     (ShellViewModel.SettingsTab, "settings"), (ShellViewModel.AboutTab, "about"),
                 })
        {
            harness.Shell.SelectedTab = tab;
            await Settle();

            CheckEveryIconButtonIsNamed(window);
            CheckNoJargon(window);
            CheckNothingRunsOffTheScreen(window, name);
            Screenshot(window, name + "-dark");
        }

        await CheckAppsScreen(window, harness);

        app.ThemeMode = ThemeMode.Light;
        await Settle();
        Screenshot(window, "apps-light");

        harness.Shell.SelectedTab = ShellViewModel.SettingsTab;
        await Settle();
        Screenshot(window, "settings-light");

        harness.Shell.SelectedTab = ShellViewModel.ThemesTab;
        await Settle();
        Screenshot(window, "themes-light");

        await CheckResultsAreAnnounced(window, harness);

        harness.Shell.Apps.SelectedTile = harness.Shell.Apps.Tiles[3];
        await CheckEveryScreen(window, app, "full");

        window.Width = OpeningWidth;
        window.Height = OpeningHeight;
        await Settle();

        var rail = Find<NavigationRail>(window).Single();
        Assert.False(rail.IsCompact, "The rail should show its labels at the size the window opens at.");

        await CheckEveryScreen(window, app, "wide");

        window.Width = MinimumWidth;
        window.Height = MinimumHeight;
        await Settle();
        Assert.True(rail.IsCompact, "The rail should give its width back on the smallest window.");

        CheckTheRailKeepsItsNames(window);

        await TakeReadmePictures(app);

        window.Close();

        Assert.True(errors.Lines.Count == 0, "Binding errors:\n" + string.Join("\n", errors.Lines));
    }

    /// <summary>Each screen in both themes, with the contrast of its text measured against what's really behind it.</summary>
    private static async Task CheckEveryScreen(Window window, App.App app, string stage)
    {
        var shell = (ShellViewModel)window.DataContext;

        foreach (var (mode, theme) in new[] { (ThemeMode.Light, "light"), (ThemeMode.Dark, "dark") })
        {
            app.ThemeMode = mode;
            await Settle();

            foreach (var (tab, name) in Screens)
            {
                shell.SelectedTab = tab;
                await Settle();

                var label = $"{stage}-{name}-{theme}";

                CheckTextOnScreenIsReadable(window, label);
                CheckNothingRunsOffTheScreen(window, label);
                Screenshot(window, label);
            }
        }
    }

    /// <summary>
    /// Every piece of text showing, against the layers actually behind it, from the window up.
    /// Disabled controls are left out: they don't have to meet this, and they're meant to recede.
    /// </summary>
    private static void CheckTextOnScreenIsReadable(Window window, string name)
    {
        foreach (var text in Find<TextBlock>(window).Where(t => IsShown(t) && !string.IsNullOrWhiteSpace(t.Text)))
        {
            var chain = Ancestors(text).OfType<FrameworkElement>().ToArray();

            if (chain.Any(a => !a.IsEnabled) || text.Foreground is not SolidColorBrush foreground)
            {
                continue;
            }

            var behind = (Color)Application.Current.FindResource("SolidBackgroundFillColorBase");

            foreach (var layer in chain.Reverse().Select(BackgroundOf).OfType<SolidColorBrush>())
            {
                behind = Over(layer.Color, behind);
            }

            var ink = Over(foreground.Color, behind);
            var ratio = ReadableTextConverter.ContrastWith(ink, behind);

            // A symbol from the icon font is a picture, not words, and pictures are held to 3:1.
            var required = text.FontFamily.Source.Contains("Icons", StringComparison.Ordinal) ? 3.0 : 4.5;

            Assert.True(ratio >= required, $"{name}: \"{text.Text}\" has contrast {ratio:0.00} ({ink} on {behind}).");
        }
    }

    private static Brush? BackgroundOf(FrameworkElement element) => element switch
    {
        Border border => border.Background,
        Panel panel => panel.Background,
        Control control and not Window => control.Background,
        _ => null,
    };

    private static async Task CheckHoverComesAndGoes(Window window)
    {
        var item = Find<TabItem>(window).First(t => !t.IsSelected);
        var highlight = Find<FrameworkElement>(item).Single(Motion.GetShowsOnHover);

        Assert.Equal(0, highlight.Opacity);

        item.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        await Settle();
        Assert.Equal(1, highlight.Opacity);

        item.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        await Settle();
        Assert.Equal(0, highlight.Opacity);
    }

    /// <summary>The rail is a tab control underneath, so the arrow keys have to walk it like one.</summary>
    private static async Task CheckArrowKeysMoveThroughTheRail(Window window, AppHarness harness)
    {
        var first = Find<TabItem>(window).First();

        window.Activate();
        first.Focus();
        await Settle();

        // A background test run may not be allowed to take focus, and then there is nothing to check.
        if (first.IsKeyboardFocused)
        {
            var source = PresentationSource.FromVisual(first)!;

            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Down)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            });

            await Settle();

            // Another window can take the focus at any moment, and then the key went nowhere.
            if (window.IsActive)
            {
                Assert.Equal(ShellViewModel.HistoryTab, harness.Shell.SelectedTab);
            }

            harness.Shell.SelectedTab = ShellViewModel.CustomizeTab;
            await Settle();
        }
    }

    /// <summary>With the labels gone from the rail, the names still have to reach a screen reader and a tooltip.</summary>
    private static void CheckTheRailKeepsItsNames(Window window)
    {
        var items = Find<TabItem>(window).ToArray();

        Assert.Equal(Screens.Length, items.Length);

        foreach (var item in items)
        {
            Assert.Equal(item.Header, AutomationProperties.GetName(item));
            Assert.Equal(item.Header, UIElementAutomationPeer.CreatePeerForElement(item)?.GetName() ?? item.Header);
            Assert.Equal(item.Header, item.ToolTip);
            Assert.False(string.IsNullOrEmpty(NavigationRail.GetGlyph(item)));
        }
    }

    /// <summary>These messages only show on their own screen, so a screen reader has to be told about them there.</summary>
    private static async Task CheckResultsAreAnnounced(Window window, AppHarness harness)
    {
        var announced = new List<object?>();

        void Listen(object? sender, EventArgs e) => announced.Add(sender);

        LiveRegion.Announced += Listen;

        var themes = Path.Combine(harness.Box.Root, "incoming.json");
        File.WriteAllText(themes, """{ "format": "foldcraft-themes", "version": 1, "themes": [ { "name": "Ocean", "fill": { "start": "#0EA5E9" } } ] }""");
        harness.Dialogs.ThemeFileToPick = themes;

        await harness.Themes.ImportCommand.ExecuteAsync();
        await Settle();

        Assert.Contains(Find<TextBlock>(window).Single(t => t.Text == harness.Themes.Notice), announced);

        harness.Shell.SelectedTab = ShellViewModel.SettingsTab;
        await Settle();

        harness.Registry.FailWrites = true;
        harness.Settings.ToggleRightClickCommand.Execute(null);
        await Settle();

        Assert.Contains(Find<TextBlock>(window).Single(t => t.Text == harness.Settings.RightClickMessage), announced);

        harness.Registry.FailWrites = false;

        await harness.Settings.RemoveAllCommand.ExecuteAsync();
        await Settle();

        Assert.False(string.IsNullOrEmpty(harness.Settings.WhatRemainsText));
        Assert.Contains(Find<TextBlock>(window).Single(t => t.Text == harness.Settings.WhatRemainsText), announced);

        CheckNoJargon(window);
        CheckNothingRunsOffTheScreen(window, "settings-after-remove-all");
        LiveRegion.Announced -= Listen;
    }

    /// <summary>Every state the screen can be in, at the smallest window, with drawn icons on a pretend PC.</summary>
    private static async Task CheckAppsScreen(Window window, AppHarness harness)
    {
        var apps = harness.Shell.Apps;

        harness.Shell.SelectedTab = ShellViewModel.AppsTab;
        await Settle();

        CheckEveryIconButtonIsNamed(window);
        CheckNoJargon(window);
        CheckNothingRunsOffTheScreen(window, "apps-before-scan");
        Screenshot(window, "apps-empty-dark");

        var colors = new[]
        {
            System.Drawing.Color.Teal, System.Drawing.Color.OrangeRed, System.Drawing.Color.MediumPurple, System.Drawing.Color.SeaGreen,
            System.Drawing.Color.Goldenrod, System.Drawing.Color.Crimson, System.Drawing.Color.SteelBlue, System.Drawing.Color.Black,
        };

        for (var i = 0; i < colors.Length; i++)
        {
            harness.Pc.AddApp($"Sample Program {i + 1}", colors[i], i == 3 ? 32 : 256);
        }

        await apps.ScanCommand.ExecuteAsync();
        apps.SelectAllCommand.Execute(null);
        apps.SelectedTile = apps.Tiles[3];
        await Settle();

        CheckEveryIconButtonIsNamed(window);
        CheckNoJargon(window);
        CheckNothingRunsOffTheScreen(window, "apps-scanned");
        Screenshot(window, "apps-dark");

        apps.ThemeInstallFolders = true;
        await Settle();
        CheckNothingRunsOffTheScreen(window, "apps-install-explanation");
        apps.ThemeInstallFolders = false;

        harness.Dialogs.FolderToPick = harness.Box.NewFolder("Generated");
        apps.PickDestinationCommand.Execute(null);
        await apps.CreateCommand.ExecuteAsync();
        await Settle();

        CheckEveryIconButtonIsNamed(window);
        CheckNoJargon(window);
        CheckNothingRunsOffTheScreen(window, "apps-results");
        Screenshot(window, "apps-results-dark");
    }

    /// <summary>The rows are rebuilt by a refresh, so the button that was pressed no longer exists.</summary>
    private static void CheckFocusLandedOnTheEntry(Window window, Guid batchId)
    {
        var focused = FocusManager.GetFocusedElement(window) as FrameworkElement;

        Assert.NotNull(focused);
        Assert.True(focused.IsVisible, "Focus is on something no longer on screen.");

        var entry = Ancestors(focused).Append(focused).OfType<FrameworkElement>()
            .Select(e => e.DataContext).OfType<HistoryEntryViewModel>().FirstOrDefault();

        Assert.Equal(batchId, entry?.BatchId);
    }

    /// <summary>The update banner announces itself the same way, but stays out of the way until there's an update.</summary>
    private static TextBlock StatusLine(Window window) =>
        Find<TextBlock>(window).Single(t => t.IsVisible && AutomationProperties.GetLiveSetting(t) == AutomationLiveSetting.Polite);

    /// <summary>A screen reader reads the peer's name, which must be the message and not a label.</summary>
    private static void CheckStatusLineIsReadable(Window window, AppHarness harness)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(StatusLine(window));

        Assert.Equal(harness.Shell.Activity.Message, peer.GetName());
    }

    private static async Task CheckAnUnusableImageIsAnnounced(Window window, AppHarness harness)
    {
        var error = Find<TextBlock>(window).Single(t => AutomationProperties.GetLiveSetting(t) == AutomationLiveSetting.Assertive);
        var announced = new List<object?>();

        void Listen(object? sender, EventArgs e) => announced.Add(sender);

        var image = Path.Combine(harness.Box.Root, "not-a-picture.png");
        File.WriteAllText(image, "this is not a picture");
        harness.Dialogs.ImageToPick = image;

        LiveRegion.Announced += Listen;
        harness.Designer.PickImageCommand.Execute(null);
        await harness.Designer.PreviewTask;
        await Settle();
        LiveRegion.Announced -= Listen;

        Assert.False(string.IsNullOrEmpty(error.Text));
        Assert.Contains(error, announced);
        Assert.Equal(error.Text, UIElementAutomationPeer.CreatePeerForElement(error).GetName());

        harness.Designer.ClearImageCommand.Execute(null);
        await harness.Designer.PreviewTask;
        await Settle();
    }

    private static async Task Settle()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        // Longer than the app's own short animations, so nothing is measured or pictured halfway through one.
        await Task.Delay(220);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<T> Find<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in Find<T>(child))
            {
                yield return inner;
            }
        }
    }

    private static bool IsShown(FrameworkElement element) => element.IsVisible && element.ActualWidth > 0;

    private static void CheckEveryChoiceHasAName(Window window)
    {
        var lists = Find<ListBox>(window).Where(l => AutomationProperties.GetName(l) is "Color palette" or "Folder symbols").ToArray();

        Assert.Equal(2, lists.Length);

        foreach (var item in lists.SelectMany(Find<ListBoxItem>))
        {
            Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(item)));
        }
    }

    /// <summary>The choice lists have no ScrollViewer, which is what the keyboard code expects to find.</summary>
    private static async Task CheckArrowKeysMoveThroughThePalette(Window window, AppHarness harness)
    {
        var palette = Find<ListBox>(window).Single(l => AutomationProperties.GetName(l) == "Color palette");
        var first = (ListBoxItem)palette.ItemContainerGenerator.ContainerFromIndex(0);

        window.Activate();
        first.Focus();
        await Settle();

        // A background test run may not be allowed to take focus, and then there is nothing to check.
        if (first.IsKeyboardFocused)
        {
            var source = PresentationSource.FromVisual(first)!;

            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Right)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            });

            await Settle();

            // Another window can take the focus at any moment, and then the key went nowhere.
            if (window.IsActive)
            {
                Assert.Equal(harness.Designer.Swatches[1], harness.Designer.SelectedSwatch);
            }

            harness.Designer.SelectedSwatch = harness.Designer.Swatches[0];
        }
    }

    private static void CheckLayoutFits(Window window, string theme)
    {
        // The view, not the window: the tab's content area is what actually clips a screen.
        var view = Find<CustomizeView>(window).Single();
        Assert.True(view.ActualWidth > 0);

        foreach (var element in Find<ButtonBase>(view).Cast<FrameworkElement>().Where(IsShown))
        {
            // Anything in a scrolling list or pane is allowed past the edge. Everything else must fit.
            if (Ancestors(element).Any(a => a is ScrollViewer))
            {
                continue;
            }

            var bounds = element.TransformToAncestor(view).TransformBounds(new Rect(element.RenderSize));

            Assert.True(
                bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= view.ActualWidth + 1 && bounds.Bottom <= view.ActualHeight + 1,
                $"{theme}: '{(element as ContentControl)?.Content}' is cut off at {bounds} in {view.ActualWidth}x{view.ActualHeight}.");
        }

        var cards = Find<Border>(view).Where(b => b.Style == (Style)Application.Current.FindResource("Card")).ToArray();
        Assert.NotEmpty(cards);

        foreach (var card in cards)
        {
            var bounds = card.TransformToAncestor(view).TransformBounds(new Rect(card.RenderSize));
            Assert.True(bounds.Right <= view.ActualWidth + 1, $"{theme}: a card runs past the edge at {bounds}.");
        }

        var apply = Find<Button>(view).Single(b => (b.Content as string)?.StartsWith("Apply") == true);

        if (apply.IsEnabled)
        {
            var behind = ((SolidColorBrush)apply.Background).Color;
            var text = ((SolidColorBrush)apply.Foreground).Color;
            var ratio = ReadableTextConverter.ContrastWith(behind, text);

            Assert.True(ratio >= 4.5, $"{theme}: the Apply button's text has contrast {ratio:0.00}.");
        }
    }

    /// <summary>The prompts are shown for real, and pressed the way a person would.</summary>
    private static async Task CheckPrompts()
    {
        var dialogs = new WpfDialogs();

        Task Answer(string label)
        {
            return Dispatcher.CurrentDispatcher.InvokeAsync(
                () =>
                {
                    var prompt = Application.Current.Windows.OfType<PromptWindow>().Single();
                    var button = Find<Button>(prompt).Single(b => (string)b.Content == label);

                    Screenshot(prompt, "prompt-" + label.Replace(" ", string.Empty, StringComparison.Ordinal));
                    Assert.True(button.IsVisible);
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                },
                DispatcherPriority.ApplicationIdle).Task;
        }

        var folders = new[] { @"C:\Example\One", @"C:\Example\Two" };

        var keep = Answer("Keep Current Version");
        Assert.Equal(ConflictChoice.KeepCurrent, dialogs.AskAboutConflicts(folders));
        await keep;

        var restore = Answer("Restore Anyway");
        Assert.Equal(ConflictChoice.RestoreAnyway, dialogs.AskAboutConflicts(folders));
        await restore;

        var later = Answer("Decide Later");
        Assert.Equal(ConflictChoice.DecideLater, dialogs.AskAboutConflicts(folders));
        await later;

        var forget = Answer("Stop Tracking");
        Assert.Equal(MissingChoice.StopTracking, dialogs.AskAboutMissing(folders));
        await forget;

        var keepTracking = Answer("Keep in History");
        Assert.Equal(MissingChoice.KeepInHistory, dialogs.AskAboutMissing(folders));
        await keepTracking;

        var find = Answer("Find Folder...");
        Assert.Equal(MissingChoice.FindFolder, dialogs.AskAboutMissing(folders));
        await find;

        var confirm = Answer("Restore everything");
        Assert.True(dialogs.Confirm("Restore everything?", "Sure?", "Restore everything"));
        await confirm;

        var cancel = Answer("Cancel");
        Assert.False(dialogs.Confirm("Restore everything?", "Sure?", "Restore everything"));
        await cancel;

        var closed = Dispatcher.CurrentDispatcher.InvokeAsync(
            () => Application.Current.Windows.OfType<PromptWindow>().Single().Close(),
            DispatcherPriority.ApplicationIdle).Task;

        Assert.False(dialogs.Confirm("Restore everything?", "Sure?", "Restore everything"));
        await closed;

        var ok = Answer("OK");
        dialogs.ShowError("Something went wrong.");
        await ok;
    }

    /// <summary>A real drag can't be scripted, so this delivers the events a drop raises.</summary>
    private static async Task CheckDropAddsFolders(Window window, AppHarness harness)
    {
        var target = Find<Grid>(Find<CustomizeView>(window).Single()).Single(g => FolderDrop.GetCommand(g) is not null);
        var folder = harness.Box.NewFolder("Dropped");
        var before = harness.Customize.Folders.Count;

        Assert.True(target.AllowDrop);

        var data = new DataObject(DataFormats.FileDrop, new[] { folder });
        var drag = NewDragEvent(UIElement.DragOverEvent, data, target);
        target.RaiseEvent(drag);
        Assert.Equal(DragDropEffects.Copy, drag.Effects);

        var text = NewDragEvent(UIElement.DragOverEvent, new DataObject(DataFormats.UnicodeText, "not a file"), target);
        target.RaiseEvent(text);
        Assert.Equal(DragDropEffects.None, text.Effects);

        target.RaiseEvent(NewDragEvent(UIElement.DropEvent, data, target));

        for (var i = 0; i < 200 && harness.Customize.Folders.Count == before; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(before + 1, harness.Customize.Folders.Count);
        harness.Customize.ClearCommand.Execute(null);
    }

    /// <summary>DragEventArgs has no public constructor.</summary>
    private static DragEventArgs NewDragEvent(RoutedEvent routedEvent, IDataObject data, UIElement target)
    {
        var args = (DragEventArgs)Activator.CreateInstance(
            typeof(DragEventArgs),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [data, DragDropKeyStates.None, DragDropEffects.All, target, new Point(1, 1)],
            null)!;

        args.RoutedEvent = routedEvent;
        return args;
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            yield return parent;
        }
    }

    /// <summary>Every button on the screen in front has to be reachable at the smallest window size.</summary>
    private static void CheckNothingRunsOffTheScreen(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;

        foreach (var button in Find<ButtonBase>(window).Cast<FrameworkElement>().Where(IsShown))
        {
            if (Ancestors(button).Any(a => a is ScrollViewer))
            {
                continue;
            }

            var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));

            Assert.True(
                bounds.Right <= content.ActualWidth + 1 && bounds.Bottom <= content.ActualHeight + 1,
                $"{name}: '{(button as ContentControl)?.Content}' is cut off at {bounds}.");
        }
    }

    private static void CheckEveryIconButtonIsNamed(Window window)
    {
        foreach (var button in Find<Button>(window).Where(IsShown))
        {
            if (button.Content is not string)
            {
                Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(button)), "A button with no text has no name.");
            }
        }
    }

    /// <summary>The main UI never mentions the machinery.</summary>
    private static void CheckNoJargon(Window window)
    {
        foreach (var text in Find<TextBlock>(window).Where(IsShown).Select(t => t.Text))
        {
            Assert.DoesNotContain("desktop.ini", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("journal", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void CheckTextContrast(string theme)
    {
        var background = (Color)Application.Current.FindResource("SolidBackgroundFillColorBase");
        var card = Over(ColorOf("CardBackgroundFillColorDefaultBrush"), background);

        foreach (var key in new[]
                 {
                     "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "SystemFillColorSuccessBrush",
                     "SystemFillColorCautionBrush", "SystemFillColorCriticalBrush",
                 })
        {
            var text = Over(ColorOf(key), card);
            var ratio = ReadableTextConverter.ContrastWith(text, card);

            Assert.True(ratio >= 4.5, $"{theme}: {key} has contrast {ratio:0.00} on a card.");
        }
    }

    private static Color ColorOf(string brushKey) =>
        ((SolidColorBrush)Application.Current.FindResource(brushKey)).Color;

    private static Color Over(Color top, Color bottom)
    {
        var alpha = top.A / 255.0;
        byte Mix(byte a, byte b) => (byte)Math.Round((a * alpha) + (b * (1 - alpha)));

        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    [Theory]
    [InlineData(1200, 1920, 900, 1200)]
    [InlineData(1200, 1280, 900, 1178)]
    [InlineData(780, 680, 600, 626)]
    [InlineData(780, 500, 600, 600)]
    public void The_window_opens_small_enough_to_fit_but_never_below_its_minimum(double wanted, double screen, double minimum, double expected)
    {
        Assert.Equal(expected, Math.Round(OpeningSize.Fit(wanted, screen, minimum)));
    }

    private static void Screenshot(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOLDCRAFT_SCREENSHOT_DIR");

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var content = (FrameworkElement)window.Content;
        var (width, height) = ((int)content.ActualWidth, (int)content.ActualHeight);

        var drawing = new DrawingVisual();

        using (var context = drawing.RenderOpen())
        {
            var background = new SolidColorBrush((Color)Application.Current.FindResource("SolidBackgroundFillColorBase"));
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));

            // The fixed view keeps this at one to one. Left alone, the brush shrinks to fit anything that overflows.
            var picture = new VisualBrush(content)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, width, height),
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };

            context.DrawRectangle(picture, null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
