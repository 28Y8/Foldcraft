using System.Windows;
using System.Windows.Media.Animation;

namespace Foldcraft.App.Views;

/// <summary>
/// The app's small amount of movement, in one place. All of it is switched off when someone has
/// turned off animation effects in Windows, and then things simply change at once.
/// </summary>
public static class Motion
{
    private static readonly TimeSpan HoverIn = TimeSpan.FromMilliseconds(83);
    private static readonly TimeSpan HoverOut = TimeSpan.FromMilliseconds(167);
    private static readonly TimeSpan PageIn = TimeSpan.FromMilliseconds(167);

    /// <summary>Set on a part of a control's template that should fade in while the mouse is over the control.</summary>
    public static readonly DependencyProperty ShowsOnHoverProperty = DependencyProperty.RegisterAttached(
        "ShowsOnHover", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnShowsOnHoverChanged));

    /// <summary>A short duration for changes of state, or none at all when animation is off.</summary>
    public static Duration Quick { get; } = new(IsEnabled ? TimeSpan.FromMilliseconds(167) : TimeSpan.Zero);

    private static bool IsEnabled => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    public static bool GetShowsOnHover(DependencyObject element) => (bool)element.GetValue(ShowsOnHoverProperty);

    public static void SetShowsOnHover(DependencyObject element, bool value) => element.SetValue(ShowsOnHoverProperty, value);

    /// <summary>Brings a page in gently. Only opacity moves, so nothing shifts under the pointer.</summary>
    public static void FadeIn(UIElement element)
    {
        if (IsEnabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, PageIn) { EasingFunction = new QuadraticEase() });
        }
    }

    private static void OnShowsOnHoverChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement part || e.NewValue is not true)
        {
            return;
        }

        part.Opacity = 0;
        part.Loaded += ConnectToControl;
    }

    private static void ConnectToControl(object sender, RoutedEventArgs e)
    {
        var part = (FrameworkElement)sender;
        part.Loaded -= ConnectToControl;

        if (part.TemplatedParent is UIElement control)
        {
            control.MouseEnter += (_, _) => Fade(part, show: true);
            control.MouseLeave += (_, _) => Fade(part, show: false);
        }
    }

    private static void Fade(UIElement part, bool show)
    {
        var target = show ? 1d : 0d;

        if (IsEnabled)
        {
            part.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(target, show ? HoverIn : HoverOut));
        }
        else
        {
            part.Opacity = target;
        }
    }
}
