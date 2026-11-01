using System.Windows;
using Foldcraft.Core.Settings;

namespace Foldcraft.App.Services;

/// <summary>Changes the app's own colors. It's an interface so view-models don't need a running application.</summary>
public interface IAppearance
{
    void Apply(AppThemeMode mode);
}

/// <summary>Sets the Fluent theme for the whole app.</summary>
public sealed class WpfAppearance : IAppearance
{
    public void Apply(AppThemeMode mode)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

#pragma warning disable WPF0001 // ThemeMode is still marked experimental.
        application.ThemeMode = mode switch
        {
            AppThemeMode.Light => ThemeMode.Light,
            AppThemeMode.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
#pragma warning restore WPF0001
    }
}
