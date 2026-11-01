using System.ComponentModel;
using System.Windows;
using Foldcraft.App.Services;
using Foldcraft.App.ViewModels;

namespace Foldcraft.App;

/// <summary>The window. It only connects closing to the view-model that decides when that's safe.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        FitOnScreen();
        Views.AccentInk.Follow(this);

        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ShellViewModel shell)
            {
                shell.CloseRequested += (_, _) => Close();
                shell.ActivationRequested += (_, _) => ComeForward();
            }
        };
    }

    private void FitOnScreen()
    {
        var screen = SystemParameters.WorkArea;

        Width = OpeningSize.Fit(Width, screen.Width, MinWidth);
        Height = OpeningSize.Fit(Height, screen.Height, MinHeight);
    }

    private void ComeForward()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (DataContext is ShellViewModel shell && !shell.TryClose())
        {
            e.Cancel = true;
        }
    }
}
