using Foldcraft.Core.Settings;
using Foldcraft.Core.Shell;

namespace Foldcraft.Core.Updates;

/// <summary>
/// What Foldcraft itself does while Windows uninstalls it: it takes the right-click entry off,
/// because that entry would otherwise point at a program that's gone. It writes nothing else.
/// Customized folders and the data folder stay exactly as they are, and that includes the
/// settings file, which is only read here. An entry that starts some other copy of Foldcraft
/// keeps working for that copy, so it's left alone.
/// </summary>
public static class UninstallCleanup
{
    /// <param name="registry">The registry holding the entry.</param>
    /// <param name="settings">Read for the keys that were made for the entry. Never written.</param>
    /// <param name="exePath">The Foldcraft.exe being uninstalled.</param>
    public static RightClickResult RemoveRightClick(IUserRegistry registry, SettingsStore settings, string exePath)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);

        return new RightClickMenu(registry).DisableFor(exePath, settings.Load().RightClick?.AddedParents);
    }
}
