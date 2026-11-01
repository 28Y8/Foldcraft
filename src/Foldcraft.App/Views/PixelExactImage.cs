using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Foldcraft.App.Views;

/// <summary>
/// Shows a bitmap at one screen pixel per image pixel, whatever the display scaling. Previews
/// are drawn at the display's real size, so a 16 px preview at 150% is a 24 px frame, which is
/// what Explorer draws in that slot, rather than a stretched copy of a smaller one.
/// </summary>
public sealed class PixelExactImage : Image
{
    public PixelExactImage()
    {
        Stretch = Stretch.Fill;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Source is not BitmapSource bitmap)
        {
            return default;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        return new Size(bitmap.PixelWidth / dpi.DpiScaleX, bitmap.PixelHeight / dpi.DpiScaleY);
    }

    protected override Size ArrangeOverride(Size arrangeSize) => MeasureOverride(arrangeSize);
}
