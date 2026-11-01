using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Foldcraft.App.Views;

/// <summary>
/// The app's screens down the left side of the window. It is a tab control with a different look,
/// so keyboard use and screen readers work the way they do for any tabs. On a narrow window it
/// shrinks to its icons, which gives the screen most of the width back.
/// </summary>
public sealed class NavigationRail : TabControl
{
    /// <summary>Below this width the labels go. The Customize screen needs the room for its three columns.</summary>
    private const double CompactBelow = 1040;

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(NavigationRail), new PropertyMetadata(false));

    /// <summary>What sits along the bottom of the content, under every screen.</summary>
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(object), typeof(NavigationRail));

    /// <summary>The symbol beside a tab's name, as a character from the system's icon font.</summary>
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(NavigationRail), new PropertyMetadata(string.Empty));

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public object? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public static string GetGlyph(DependencyObject item) => (string)item.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject item, string value) => item.SetValue(GlyphProperty, value);

    protected override AutomationPeer OnCreateAutomationPeer() => new RailPeer(this);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        IsCompact = sizeInfo.NewSize.Width < CompactBelow;
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);

        if (IsLoaded && GetTemplateChild("PART_SelectedContentHost") is UIElement page)
        {
            Motion.FadeIn(page);
        }
    }

    /// <summary>
    /// Tabs only tell screen readers about their tabs, which would leave the status bar and the
    /// update banner's buttons out of reach, so this adds them to what's reported.
    /// </summary>
    private sealed class RailPeer(NavigationRail rail) : TabControlAutomationPeer(rail)
    {
        protected override List<AutomationPeer> GetChildrenCore()
        {
            var children = base.GetChildrenCore() ?? [];

            if (rail.Status is FrameworkElement status)
            {
                children.Add(new FrameworkElementAutomationPeer(status));
            }

            return children;
        }
    }
}
