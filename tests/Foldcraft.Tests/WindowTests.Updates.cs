using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Foldcraft.App;
using Foldcraft.App.Services.Updates;
using Foldcraft.App.ViewModels;
using Foldcraft.App.Views;

namespace Foldcraft.Tests;

/// <summary>The update banner and the Settings card, at the smallest window, in a copy the installer put in place.</summary>
public sealed partial class WindowTests
{
    private static async Task CheckTheUpdateBanner()
    {
        var service = new FakeUpdateService { Found = new AvailableUpdate("0.2.0"), DownloadGate = new TaskCompletionSource() };

        using var harness = new AppHarness(updates: service);

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

        harness.Shell.SelectedTab = ShellViewModel.SettingsTab;
        await harness.Updates.CheckCommand.ExecuteAsync();
        await Settle();

        Screenshot(window, "update-available");
        CheckBannerButton(window, "Update");
        CheckBannerButton(window, "Later");

        var download = harness.Updates.UpdateCommand.ExecuteAsync();

        await service.DownloadStarted.Task;
        await Settle();

        Screenshot(window, "update-downloading");
        CheckBannerButton(window, "Cancel");

        service.DownloadGate.SetResult();
        await download;
        await Settle();

        Screenshot(window, "update-ready");
        CheckBannerButton(window, "Restart Now");

        await PictureTheSettingsCards(window, "update-settings-installed");
        await CheckADismissedBannerStaysQuiet(window, harness);
        window.Close();

        using var plain = new AppHarness();

        var plainWindow = new MainWindow
        {
            DataContext = plain.Shell,
            Width = MinimumWidth,
            Height = MinimumHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
        };

        plainWindow.Show();
        plain.Shell.SelectedTab = ShellViewModel.SettingsTab;
        await Settle();
        await PictureTheSettingsCards(plainWindow, "update-settings-not-installed");
        plainWindow.Close();
    }

    /// <summary>The line changes with the work going on, so a hidden banner has to hold its tongue.</summary>
    private static async Task CheckADismissedBannerStaysQuiet(Window window, AppHarness harness)
    {
        var line = Find<TextBlock>(window).Single(text => text.Text == harness.Updates.BannerText);
        var announced = new List<object?>();

        void Listen(object? sender, EventArgs e) => announced.Add(sender);

        async Task WorkComesAndGoes()
        {
            harness.Shell.Activity.Begin("Working");
            await Settle();
            harness.Shell.Activity.Finish("Done");
            await Settle();
        }

        LiveRegion.Announced += Listen;

        await WorkComesAndGoes();
        Assert.Contains(line, announced);

        announced.Clear();
        harness.Updates.LaterCommand.Execute(null);
        await Settle();
        Assert.False(line.IsVisible);

        await WorkComesAndGoes();
        Assert.DoesNotContain(line, announced);

        LiveRegion.Announced -= Listen;
    }

    private static async Task PictureTheSettingsCards(Window window, string name)
    {
        Find<ScrollViewer>(window).First(viewer => viewer.IsVisible).ScrollToBottom();
        await Settle();
        Screenshot(window, name);
    }

    /// <summary>A screen reader has to find it, and it has to fit in the smallest window.</summary>
    private static void CheckBannerButton(Window window, string name)
    {
        var button = Find<Button>(window).Single(item => item.IsVisible && Equals(item.Content, name) && item.Margin.Left >= 8);
        var right = button.TranslatePoint(new Point(button.ActualWidth, 0), window).X;

        Assert.True(right <= ((FrameworkElement)window.Content).ActualWidth, $"{name} runs past the edge of the window.");

        var rail = UIElementAutomationPeer.CreatePeerForElement(Find<NavigationRail>(window).Single());
        var peers = Flatten(rail).Select(peer => peer.GetName());

        Assert.Contains(name, peers);
    }

    private static IEnumerable<AutomationPeer> Flatten(AutomationPeer peer)
    {
        yield return peer;

        foreach (var child in peer.GetChildren() ?? [])
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}
