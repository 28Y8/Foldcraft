using System.Windows;
using System.Windows.Media;

namespace Foldcraft.App.Views;

/// <summary>
/// Keeps marks on the accent color readable. The theme always draws them white in light mode,
/// which vanishes on a yellow or amber accent. This follows the accent through every change of
/// theme or Windows color, and picks black or white for what is really behind the mark.
/// </summary>
public static class AccentInk
{
    /// <summary>What the theme draws directly on the accent color, outside of buttons.</summary>
    private static readonly string[] Marks =
    [
        "CheckBoxCheckGlyphForeground", "CheckBoxCheckGlyphForegroundPointerOver", "CheckBoxCheckGlyphForegroundPressed",
        "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed",
    ];

    private static readonly DependencyProperty AccentProperty = DependencyProperty.RegisterAttached(
        "Accent", typeof(Brush), typeof(AccentInk), new PropertyMetadata(null, OnAccentChanged));

    public static void Follow(FrameworkElement element) => element.SetResourceReference(AccentProperty, "AccentFillColorDefaultBrush");

    private static void OnAccentChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not SolidColorBrush accent || Application.Current is not { } application)
        {
            return;
        }

        var ink = ReadableTextConverter.Pick(accent.Color);

        foreach (var mark in Marks)
        {
            application.Resources[mark] = ink;
        }
    }
}
