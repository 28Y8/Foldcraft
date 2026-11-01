using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Foldcraft.App.ViewModels;

namespace Foldcraft.App.Views;

public partial class CustomizeView : UserControl
{
    public CustomizeView()
    {
        InitializeComponent();
        Loaded += (_, _) => ShowAtScale(VisualTreeHelper.GetDpi(this));
    }

    /// <summary>The window moved to a display with different scaling.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ShowAtScale(newDpi);
    }

    private void ShowAtScale(DpiScale dpi)
    {
        if (DataContext is CustomizeViewModel customize)
        {
            customize.Designer.DisplayScale = dpi.DpiScaleX;
        }
    }
}
