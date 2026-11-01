using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace Foldcraft.App.Views;

/// <summary>
/// Makes a bound TextBlock speak up. AutomationProperties.LiveSetting only says how urgent a
/// live region is: WPF announces nothing until code raises LiveRegionChanged, so this raises
/// it whenever the text changes. The binding needs NotifyOnTargetUpdated=True.
/// </summary>
public static class LiveRegion
{
    public static readonly DependencyProperty AnnounceProperty = DependencyProperty.RegisterAttached(
        "Announce", typeof(bool), typeof(LiveRegion), new PropertyMetadata(false, OnAnnounceChanged));

    /// <summary>Raised after each announcement. There's no other way to watch one from a test.</summary>
    public static event EventHandler? Announced;

    public static bool GetAnnounce(DependencyObject element) => (bool)element.GetValue(AnnounceProperty);

    public static void SetAnnounce(DependencyObject element, bool value) => element.SetValue(AnnounceProperty, value);

    private static void OnAnnounceChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            Binding.AddTargetUpdatedHandler(element, OnTargetUpdated);
        }
        else
        {
            Binding.RemoveTargetUpdatedHandler(element, OnTargetUpdated);
        }
    }

    private static void OnTargetUpdated(object? sender, DataTransferEventArgs e)
    {
        if (sender is TextBlock text && e.Property == TextBlock.TextProperty)
        {
            // Wait for layout: an element that collapses when empty may only now be appearing,
            // and assistive tech reads it at the moment the event arrives.
            text.Dispatcher.InvokeAsync(() => Announce(text), DispatcherPriority.Loaded);
        }
    }

    private static void Announce(TextBlock text)
    {
        // Something hidden, like a banner someone dismissed, keeps its binding but has nothing to say.
        if (string.IsNullOrEmpty(text.Text) || !text.IsVisible)
        {
            return;
        }

        var peer = UIElementAutomationPeer.FromElement(text) ?? UIElementAutomationPeer.CreatePeerForElement(text);

        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        Announced?.Invoke(text, EventArgs.Empty);
    }
}
