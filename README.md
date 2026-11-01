# Foldcraft

**Give your Windows 11 folders any look you like, and undo all of it whenever you want.**

[![CI](https://github.com/28Y8/Foldcraft/actions/workflows/ci.yml/badge.svg)](https://github.com/28Y8/Foldcraft/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)
![Windows 11](https://img.shields.io/badge/Windows-11-0078D4.svg)

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/customize-dark.png">
  <img src="docs/images/customize-light.png" alt="The Customize screen: four folders from D:\Projects on the left, the color, badge and symbol choices in the middle and a live preview of a purple and blue folder on the right.">
</picture>

Foldcraft changes how folders look in File Explorer. Pick some folders, design a look and apply it. It uses the customization built into Windows, keeps a record of every change and never needs administrator rights.

> **Status: early.** Foldcraft has only been run on one Windows 11 PC so far. Expect rough edges, and please [report what you find](https://github.com/28Y8/Foldcraft/issues).

## Features

- **Colors:** 16 ready-made colors or any color you type, solid or as a two-color gradient
- **Folder styles:** Windows 11 classic, flat, outline or soft 3D
- **Symbols:** more than 100 built-in symbols with search and categories, an emoji or your own picture (PNG, JPEG, BMP or GIF)
- **Badges:** 12 corner badges such as Done, Important and Waiting, in any corner and any color
- **Themes:** 24 built-in looks in 8 moods. Save your own, and share them as small files
- **Rules:** "folders that mostly hold pictures get this look". Rules run only when you press Preview and then Apply
- **Apps and games:** scan your PC and make a matching folder for each app and game
- **Live preview:** see the folder large, at 32 px and at 16 px before you change anything
- **Many at once:** change one folder or many, adding them with a button, by dragging them in or from the right-click menu
- **Undo:** undo any change, restore a single folder or restore everything
- **Repair:** draw an icon again if it goes missing or gets damaged
- **Updates:** an installer for your account only and a quiet offer when there's a new version. You choose if and when to update

![Twelve folders drawn by Foldcraft in different styles and colors, with symbols and badges.](docs/images/folder-gallery.png)

![The Themes screen with 24 built-in looks grouped by mood.](docs/images/themes.png)

![The Rules screen: three rules on the left and, on the right, a preview of the four folders they would change.](docs/images/rules.png)

## App and game folders

Press **Scan** on the Apps and Games screen and Foldcraft looks for what's installed. Scanning only reads. It looks at Steam's library files, Roblox's per-user install, the Epic Games launcher's manifests and your Start Menu, so most apps turn up without any list of known products.

Foldcraft reads each icon from the software on your PC, picks the folder's colors from it and centers the icon on the front. You can change the colors, position, size, style and name of any item. Choose a location, check what you want and press **Create**. Each new folder is named after the app and holds a shortcut that starts it.

![The Apps and Games screen with eight sample apps, each with a folder in matching colors. The icons in this picture were drawn for it.](docs/images/apps-and-games.png)

Protected places stay protected. Games often live under Program Files or AppData, which Foldcraft won't touch, so it makes a new folder somewhere you chose. Your existing files are never overwritten, and pressing **Undo** removes what was created, as long as you haven't added anything to it. App icons are read from your own PC and stay there.

## How safety works

Foldcraft is built on one promise: it changes how folders look, and it can't damage anything.

- **It uses what Windows already has.** A small hidden `desktop.ini` file inside the folder points to an icon, and the folder gets the mark that tells Windows to read it. No shell extensions, no patched system files, no restarting Explorer.
- **Your files are left alone.** Foldcraft never moves, renames, opens or edits anything else in a folder. A folder's date moves 100 nanoseconds while it has a look, which is what makes the look appear, and undo puts the exact date back unless the folder changed or needs a new preview.
- **It records before it acts.** Each change goes into an undo history first, including an exact copy of any existing `desktop.ini`. If a step fails, the folder is put back. After a crash, Foldcraft tells you and **Undo** finishes the cleanup.
- **It respects other changes.** If another app changed a folder after Foldcraft did, you decide what happens. Nothing is overwritten without asking.
- **It refuses places that matter.** The Windows folder, Program Files, ProgramData, AppData, your main user folder, whole drives, network locations and anything reached through a link or junction are all off limits.
- **It needs no administrator rights.** It runs with your normal account and asks for nothing more.
- **Nothing runs in the background.** There's no service and nothing starts with Windows.
- **It contacts GitHub only to check for and download updates.** It sends no personal data and no information about your folders. GitHub sees your IP address, as any website does. You can turn the check off in Settings, and nothing downloads or installs until you press **Update**.

![The History screen: two changes that can each be undone, with the folders of the first one listed and a Restore Original button next to each.](docs/images/history.png)

The full story is in [how safety works](docs/safety.md) and [how Foldcraft works](docs/how-it-works.md).

## Limitations

- Only tested on one Windows 11 PC. It hasn't been tried on Windows 10.
- The installer and the app aren't signed, so Windows SmartScreen may warn you the first time you run them. Update packages are checked against the hash recorded in their release, but they aren't code signed.
- The installer, a real update from GitHub and a real uninstall haven't been run on a PC yet. Updating was tested from start to finish with a portable copy and a local folder standing in for GitHub.
- A File Explorer window that was already open can keep the old look until you close and reopen it. Foldcraft tells Explorer about each change, but it never restarts Explorer. In testing with a shell view hosted in a test program, a first look showed up within seconds and undo brought the preview straight back, but a second custom look stayed hidden until the view was reopened. None of this has been confirmed in a real Explorer window yet.
- Undo brings a saved preview straight back. A folder with no saved preview for its date, such as one whose files changed while it had its look, needs a new one, and for up to a minute after a window showed the look Windows can't draw it. If a window asks in that time, the folder shows as a plain folder until Foldcraft moves its date one more step, about 75 seconds after the undo. If you close Foldcraft first, that happens the next time you open it. This was tested with a test program standing in for Explorer, not in a real Explorer window.
- Customized folders use icons stored in `%LOCALAPPDATA%\Foldcraft`. Use **Remove All Changes** in Settings before deleting that folder. Uninstalling leaves it in place, and your folders keep their look.
- A customized folder shows the look only on PCs that have its icon, so another PC syncing the same folder shows a plain folder.
- Folders in OneDrive or another cloud service can be customized, but this hasn't been tried on a real synced folder yet. They're treated like any other folder: Foldcraft reads and changes the settings file and the folder's date and lists names when it cleans up after itself. It never opens your other files, so it can't cause them to download, though a settings file that's only online may download when it's read. Windows itself may still read files when it draws a preview, as it does for any folder.
- If you rename or move a customized folder, Foldcraft can't follow it on its own. **Find Folder** lets you show it the new location.
- Windows doesn't show a custom look for a folder whose path is about 248 characters or longer. Foldcraft warns you.
- Folder names that end in a dot or a space are refused, because Windows would treat them as a different folder's name.
- On Windows 11 the right-click entry appears under **Show more options**, not in the first short menu.
- Apps from the Microsoft Store usually don't have a Start Menu shortcut Foldcraft can read, so a scan misses most of them.
- Epic Games support has only been tested against made-up manifests.
- Emoji are drawn by the Segoe UI Emoji font on your own PC, in color where Windows allows it and in one color otherwise.

## Install

1. Download `FoldcraftApp-win-Setup.exe` from the [Releases page](https://github.com/28Y8/Foldcraft/releases). If there's nothing there yet, [build it from source](#build-from-source).
2. Run it. It installs for your account only, needs no administrator rights and adds Foldcraft to the Start Menu. .NET comes included.
3. If SmartScreen says it protected your PC, choose **More info** and then **Run anyway**. It shows up because the installer isn't signed.

Prefer not to install anything? The `Foldcraft-...-win-x64.zip` on the same page runs from any folder of its own. That copy can't update itself, so Settings points you to the Releases page for new versions.

### Updating

When there's a new version, Foldcraft shows a quiet banner with a link to the release notes. Nothing downloads until you press **Update**. A finished download refreshes the updater (`Update.exe`) right away, and the app itself changes only after you agree to restart. Foldcraft won't restart while it's changing folders or while another copy is open from the same install. Your customized folders, history, themes and settings stay as they are.

Foldcraft checks once when it starts, at most once a day, without slowing the start. Turn off **Check for updates automatically** in Settings to stop that, or press **Check for Updates** there whenever you like.

### Uninstalling

Remove Foldcraft from **Installed apps** in Windows Settings. That removes the app and its right-click entry. **Your customized folders keep their look and `%LOCALAPPDATA%\Foldcraft` stays**, because those folders use the icons kept there.

For a full clean-up, press **Remove All Changes** in Settings first and read the list of what's left. Then uninstall, and delete `%LOCALAPPDATA%\Foldcraft` once that list says it can go. For a copy from the zip, delete its folder instead of uninstalling.

## Build from source

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/28Y8/Foldcraft.git
cd Foldcraft
dotnet build
dotnet test
dotnet run --project src/Foldcraft.App
```

To try the app without touching your real data folder, give it another one:

```powershell
dotnet run --project src/Foldcraft.App -- --data-dir "$env:TEMP\foldcraft-trial"
```

The tests only ever touch throwaway folders in your temp directory and use a stand-in for the registry, so they can't reach your folders, your apps or your settings.

## Roadmap

These are ideas, not promises.

- Try Foldcraft on more PCs, on real synced folders and on a real Epic Games install
- A way to find Microsoft Store apps in a scan
- Follow a renamed or moved folder more easily
- More symbols, badges and themes

Have a better idea? [Open an issue](https://github.com/28Y8/Foldcraft/issues/new/choose).

## Contributing

Bug reports and pull requests are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) explains how to build, test and keep to the project's safety rules. Please read the [code of conduct](CODE_OF_CONDUCT.md), and report security problems the way [SECURITY.md](SECURITY.md) describes.

## License

[MIT](LICENSE). Copyright (c) 2026 28Y8. What Foldcraft is built on is listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

All symbols, badges and folder art are plain vector geometry written for this project. Nothing in the repository comes from a third-party icon set, and it holds no logos.

## Trademarks

Windows, Steam, Roblox, Epic Games, OneDrive and all other product names belong to their owners. They're mentioned only to say what Foldcraft works with. Foldcraft is an independent project and is not affiliated with, endorsed by or sponsored by any of them.
