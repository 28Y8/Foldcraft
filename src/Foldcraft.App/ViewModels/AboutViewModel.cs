using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;

namespace Foldcraft.App.ViewModels;

/// <summary>The About screen.</summary>
public sealed class AboutViewModel(IDialogs dialogs, string dataDirectory)
{
    public const string ProjectUrl = "https://github.com/28Y8/Foldcraft";

    public string Version { get; } = $"Version {AppVersion.Current}";

    public string DataDirectory { get; } = dataDirectory;

    public string License => "Copyright (c) 2026 28Y8. Free and open source under the MIT License.";

    public string Explanation =>
        "Foldcraft changes how folders look. It uses the customization built into Windows: " +
        "a small hidden settings file inside each folder you choose, plus a marker on that folder that tells " +
        "Windows to read it. Every change is recorded so it can be undone. Foldcraft never moves, renames, opens " +
        "or edits your files, never changes Windows system files, never needs administrator rights and refuses " +
        "system folders, whole drives and network locations. The one time it adds anything is on the Apps and " +
        "Games screen, and only when you press the button: new folders in the place you chose, each with a " +
        "shortcut that starts the app. The only thing the app itself ever adds to the registry is the right-click " +
        "entry, if you turn that on in Settings. The installer adds the usual uninstall entry for your account. A " +
        "scan on the Apps and Games screen reads where Steam is installed and changes nothing.";

    public string AppsNotice => PlainEnglish.AppsDisclaimer;

    public string UpdatesNotice => PlainEnglish.UpdatePrivacy;

    public RelayCommand OpenProjectCommand { get; } = new(() => dialogs.OpenLink(ProjectUrl));

    public RelayCommand OpenLicenseCommand { get; } = new(() => dialogs.OpenLink($"{ProjectUrl}/blob/main/LICENSE"));
}
