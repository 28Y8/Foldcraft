# Changelog

Notable changes to Foldcraft are listed here. Versions follow [semantic versioning](https://semver.org).

## 0.1.0 (not released yet)

The first version.

### Added

- Customize one folder or many at once, with a live preview at large, 32 px and 16 px sizes
- 16 ready-made colors or any color you type, solid or as a two-color gradient
- Four folder styles: Windows 11 classic, flat, outline and soft 3D
- More than 100 built-in symbols with search and categories, an emoji or your own picture
- 12 corner badges, in any corner and any color
- 24 built-in themes in 8 moods, saved themes and theme files you can import and export
- Rules that match a folder's name or what it mostly holds, with a preview before anything changes
- The Apps and Games screen, which scans Steam, Roblox, Epic Games and the Start Menu and makes a matching folder for each item
- An undo history, with undo for any change, restore for one folder and restore for everything
- Repair Icons, which draws a missing, damaged or outdated icon again
- An optional right-click entry for your account
- Remove All Changes, which puts everything back before you remove Foldcraft
- Light, dark and system color modes
- Safety checks that refuse system folders, whole drives, network locations and links
- An installer for your account only, which needs no administrator rights and adds a Start Menu shortcut and an uninstall entry. The app goes in `%LOCALAPPDATA%\FoldcraftApp`, apart from the data folder, which installing, updating and uninstalling leave alone
- Update checks from GitHub Releases, at most once a day, with a **Check for updates automatically** setting that's on by default and a **Check for Updates** button
- A quiet banner for a new version, with its release notes. Nothing downloads until you press **Update**. A finished download refreshes the updater (`Update.exe`) right away, and the app itself changes only after you agree to restart

### Changed

- Foldcraft now contacts GitHub to check for and download updates, which is the only reason it goes online. It sends no personal data and no information about your folders, GitHub sees your IP address as with any website and the check can be turned off
- Screen readers can now reach the status bar and its Cancel button

### Fixed

- A folder with files in it now shows its new look at Medium icons and larger, and undo brings its real preview back. Foldcraft leaves the folder's date 100 nanoseconds later while it has a look and puts the exact date back on undo. If the folder changed in the meantime, the date stays and moves on to one no look used, so Windows draws a new preview
- If a window asks for a preview too soon after undo and Windows saves a failure, Foldcraft moves the folder's date one more step about 75 seconds later, or at its next start if it was closed, so the folder doesn't stay plain
- An undo that was cut short by a crash now finishes with the date it chose, not today's date
