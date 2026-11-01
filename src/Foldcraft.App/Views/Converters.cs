using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Foldcraft.App.Views;

/// <summary>Base for converters that only ever read from the view-model.</summary>
public abstract class OneWayConverter : IValueConverter
{
    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class InverseBoolToVisibilityConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>Collapses anything showing text that isn't there.</summary>
public sealed class TextToVisibilityConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>Shows something only while there's nothing to bind to.</summary>
public sealed class NullToVisibilityConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Shows something only while there's something to bind to.</summary>
public sealed class NotNullToVisibilityConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class CountToVisibilityConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int and > 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class HexToBrushConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || ColorConverter.ConvertFromString(hex) is not Color color)
        {
            return Brushes.Transparent;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Black or white, whichever reads better on a brush. One of the two always reaches at least
/// 4.5:1, so this holds for any accent color a person can pick.
/// </summary>
public sealed class ReadableTextConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SolidColorBrush brush ? Pick(brush.Color) : DependencyProperty.UnsetValue;

    public static Brush Pick(Color background) =>
        ContrastWith(background, Colors.Black) >= ContrastWith(background, Colors.White) ? Brushes.Black : Brushes.White;

    /// <summary>The WCAG contrast ratio of two opaque colors.</summary>
    public static double ContrastWith(Color a, Color b)
    {
        var (first, second) = (Luminance(a), Luminance(b));
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(Color color) =>
        (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    private static double Channel(byte value)
    {
        var scaled = value / 255.0;
        return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
    }
}

/// <summary>Shows bytes the renderer produced. The view-models never touch WPF imaging.</summary>
public sealed class PngToImageConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes, writable: false);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

/// <summary>
/// Loads a library icon for the history list. Icons are shown by file, and a missing or
/// damaged one just leaves the row without a picture.
/// </summary>
public sealed class IconFileToImageConverter : OneWayConverter
{
    private const int Size = 32;

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault(f => f.PixelWidth == Size) ?? decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }
}
