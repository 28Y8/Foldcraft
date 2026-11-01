using System.Reflection;

namespace Foldcraft.App.Services;

/// <summary>The version this build says it is.</summary>
public static class AppVersion
{
    public static string Current { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
