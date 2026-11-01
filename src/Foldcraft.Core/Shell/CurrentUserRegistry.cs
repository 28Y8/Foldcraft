using Microsoft.Win32;

namespace Foldcraft.Core.Shell;

/// <summary>
/// The real registry, limited to HKEY_CURRENT_USER so it can never reach past this user. Only
/// the app creates one, and nothing here runs until someone uses the right-click setting.
/// </summary>
public sealed class CurrentUserRegistry : IUserRegistry
{
    public string? GetString(string subKey, string? name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void SetString(string subKey, string? name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string subKey, string? name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name ?? string.Empty, throwOnMissingValue: false);
    }

    public bool KeyExists(string subKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return key is not null;
    }

    public (IReadOnlyList<string> Values, IReadOnlyList<string> SubKeys) Contents(string subKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return key is null ? ([], []) : (key.GetValueNames(), key.GetSubKeyNames());
    }

    public void DeleteEmptyKey(string subKey) => Registry.CurrentUser.DeleteSubKey(subKey, throwOnMissingSubKey: false);
}
