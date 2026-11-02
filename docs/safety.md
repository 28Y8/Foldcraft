# How safety works

Foldcraft has one promise: it changes how folders look, and it can't damage anything. This page explains what backs that up. For the mechanics, see [how Foldcraft works](how-it-works.md).

## What Foldcraft changes

In an existing folder, only three things:

1. A small hidden file named `desktop.ini`, which it adds or edits.
2. The folder's read-only mark, which tells Windows to read that file. Foldcraft adds this only when the folder has neither the read-only nor the system mark already.
3. The folder's "date modified", by 100 nanoseconds. Undo [puts the exact date back](#dates).

Foldcraft doesn't move, rename, open or edit anything else in the folder. The one time Foldcraft adds a folder of its own is on the Apps and Games screen, and only when you press the button: a new folder in the place you chose, holding a shortcut that starts the app.

After a change, Foldcraft tells Windows that the folder changed. It never opens your files, so it can't cause them to download from a cloud service. It does read the folder's `desktop.ini`, and a copy that's only online may download for that, and it lists names when it cleans up a temp file of its own. Windows may read the files in a folder when it draws a preview, just as it does whenever you view a folder at Medium icons or larger, but nothing in them changes.

Outside your folders, Foldcraft writes only to its own data folder and, if you turn on the right-click menu, to one entry for your account in the registry. Windows updates its own picture store when it redraws a folder. The installer has its own short list, [below](#installing-updating-and-uninstalling).

## Places it refuses

Every path is reduced to its true form first, so a different spelling can't get around a check. Foldcraft refuses:

- The Windows folder, Program Files and ProgramData, including everything inside them
- AppData, including everything inside it
- Your main user folder itself (Documents, Pictures and the like are fine)
- Foldcraft's own data folder
- Whole drives
- Network locations and mapped network drives
- Folders that belong to the system at the top of a drive, such as the Recycle Bin and System Volume Information
- Any folder that contains one of the protected places above
- Anything reached through a junction, symbolic link or other redirect, anywhere along the path
- Paths it can't fully make sense of and names that end in a dot or a space

A cloud-synced folder is the one kind of redirect allowed through, and only once Foldcraft has confirmed that's what it is.

Refused means refused, with one exception. If you press **Restart as Administrator** in Settings and agree to the Windows prompt, Foldcraft allows folders inside Program Files and ProgramData. Program Files and ProgramData themselves stay refused, and so does everything else on this list, including the Windows folder and AppData. The folders of other Windows accounts are refused too, because Windows no longer keeps Foldcraft out of them. Nothing else in the app can turn these checks off. A change made this way can only be undone while running as administrator, and an app update may remove the look by replacing the folder.

The Apps and Games screen goes through the same checks, which is why many games can't be changed where they're installed.

## The undo history

Every change is recorded before it happens.

- The record holds an exact copy of any existing `desktop.ini`, along with its attributes and dates and the folder's own attributes and date, both as it was and as Foldcraft left it.
- Each record is flushed to disk before the folder is touched. If it can't be written, nothing happens to the folder.
- The history file is only ever added to. Each line has a checksum, so a line cut short by a crash is ignored and the lines before it stay readable.

## When something goes wrong

- **A step fails:** the folder is put back, and that folder reports the failure. Other folders in the batch carry on.
- **The app or Windows crashes partway:** Foldcraft says so the next time it starts, and **Undo** on the History screen finishes the cleanup. Undoing is safe to repeat.
- **A write is interrupted:** `desktop.ini` is written to a temporary file and swapped in as one step, so a half-written file is never left in place.

## When something else changed the folder

Foldcraft remembers a fingerprint of what it wrote. If `desktop.ini` is different later, Foldcraft won't overwrite it without asking. You choose to keep the current version, restore anyway or decide later. This holds even if Foldcraft changed that folder again afterward.

Undo takes off only the marks Foldcraft added, so a folder you hid stays hidden. It also checks that the folder is really the same one, so a different folder that now sits at the same path is left alone.

Undoing the Apps and Games screen removes a folder only if it still holds exactly what Foldcraft put there. If you added a file or changed the shortcut, the folder stays and only its look comes off, and Foldcraft tells you.

## Dates

While a folder has a look, its "date modified" is 100 nanoseconds later than it was, plus 100 nanoseconds for each further look. Windows files its saved picture of a folder under the exact date, so this is [what makes the new look appear](how-it-works.md#old-pictures). Explorer shows dates to the minute, and when measured, its sorting treated dates up to 10 milliseconds apart as equal, so sorting by date almost never changes. The one exception found is a date on the last 100 nanoseconds of a second with another folder's date in the next second, and those two can swap while the folder has its look. A tool that reads exact timestamps can also see folders with equal dates or dates a few hundred nanoseconds apart change places. On exFAT or FAT, which can't keep so small a step, the step is 10 milliseconds or 2 seconds, and 2 seconds is large enough to change the order on FAT.

Undo and restore put the exact date back, unless the folder really changed. Foldcraft records the date it left, so it can tell. If the date has moved since, because a file was added or a program set it, that date stays, moved a step or two later so that Windows draws a new preview. The date never lands on one a look of Foldcraft's used on that folder. About 75 seconds after an undo, Foldcraft also checks whether Windows saved a failed preview for the folder, and if it did, it moves the date one more step. A folder with a saved preview, an empty folder or one with its own icon keeps the exact date. Undo also puts back the date of a `desktop.ini` that was already there.

## Files from outside

Things that come from outside the app are treated as untrusted:

- **Theme files** are checked strictly. A file with anything unknown, malformed or too large is refused as a whole. A theme can only name a picture by a plain path on a local drive, and exported files leave your pictures behind.
- **Pictures** have limits on file size and dimensions, which are checked before they're decoded.
- **Launcher files** that a scan reads are limited in size and number, and a file that can't be understood is skipped.
- **A folder from the command line or the right-click menu** only joins the list, after the same safety check as any other folder. Nothing changes until you press Apply.

## Permissions and privacy

- Foldcraft runs as your normal account and doesn't require administrator rights. Its manifest is set to `asInvoker`. It asks for those rights only when you press **Restart as Administrator** in Settings, which closes it and opens it again after a Windows prompt. Use it on an account that's already an administrator. If you sign in to the prompt as a different account, Foldcraft runs as that account, with its data folder and undo history, and refuses your own folders. A look it applies may not show for you, because the icon sits in that account's folder.
- There's no service, no scheduled task and nothing that starts with Windows. Rules run only when you press Preview and then Apply.
- There's no telemetry. Foldcraft contacts GitHub only to check for and download updates, and it sends no personal data and no information about your folders. GitHub sees your IP address, as any website does. [Updates](#updates) has the details, and you can turn the check off in Settings.
- Links on the About screen, in the update banner and on the Settings screen open your browser when you press them.

## Installing, updating and uninstalling

The installer works for your account only and needs no administrator rights. It adds:

- The app, in `%LOCALAPPDATA%\FoldcraftApp`
- A Start Menu shortcut
- The usual uninstall entry for your account, under `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall`

The app's own data is in a different folder, `%LOCALAPPDATA%\Foldcraft`, and that's deliberate: it holds the icons your customized folders depend on and your undo history. The installer's id is `FoldcraftApp`, which sets the only folder it replaces when it updates and empties when it uninstalls, and a test fails if that folder could ever be the data folder or sit inside it. Installing, updating and uninstalling don't touch the data folder. The undo history is only ever added to, and each version reads what the versions before it wrote.

The updater (Velopack) keeps a log of its own at `%LOCALAPPDATA%\velopack\velopack_FoldcraftApp.log`. It holds paths of the app and its options, stays on your PC and is left behind by an uninstall. It's safe to delete.

Uninstalling removes the app, its shortcut, its uninstall entry and the right-click entry, if that entry starts this copy. **It doesn't change your customized folders or your data folder.** Your folders keep their look, because the icons they use are still there, and they lose it only if you delete that folder. To put them all back, press **Remove All Changes** in Settings before you uninstall.

## Updates

Foldcraft checks for a new version once when it starts, at most once a day, in the background. If you turn off **Check for updates automatically** in Settings, it only checks when you press **Check for Updates**. If the settings file can't be read, Foldcraft can't tell whether you turned it off, so it doesn't check by itself, leaves the file alone and says so in Settings. A copy that wasn't installed with the installer never checks, and it points you to the Releases page.

- **What's contacted:** GitHub only, over HTTPS. A check asks `api.github.com` for this project's stable releases and reads the list of files in the newest ones. A download fetches the update files of a release from `github.com`, which sends them from GitHub's download servers.
- **What's sent:** an ordinary web request that names the updater and its version (`Velopack`). There's no account, no ID, no version of yours and nothing about your folders, your files or your PC. GitHub sees your IP address, as any website does, and its own policies cover what it does with that.
- **What happens next:** nothing, until you press **Update**. A download can be canceled, and one that makes no progress for 10 minutes stops by itself. A finished download refreshes the updater (`Update.exe`) right away, before you're asked anything, because Velopack does that as soon as a download completes. The app itself changes only after you agree to restart, and Foldcraft refuses to restart while it's changing folders or while another copy is running from the same install.
- **If something fails:** a check that ran by itself stays silent, and one you asked for says what went wrong. Failures go to Foldcraft's log as the kind of failure only, with no paths.

Velopack keeps a random ID in its `packages` folder for a rollout feature Foldcraft doesn't use. It isn't sent to GitHub.

An update package is checked against the size and SHA-256 hash recorded in its release, and a package that doesn't match isn't used. That catches a damaged download. It isn't the same as a signature: the update packages aren't code signed, so Foldcraft can't tell whether a release itself is genuine, and it trusts what this repository's GitHub Releases page publishes. The installer isn't signed either, so Windows SmartScreen may warn about it.

## Limits of this promise

Foldcraft can't protect a folder from something else. If you delete its data folder while customized folders still use its icons, those folders fall back to a plain look until **Repair Icons** draws the icons again, and Repair needs the history that was in that data folder. Use **Remove All Changes** in Settings first. It puts back every folder it can, removes the right-click entry and lists what's left. Only delete the data folder if that list says you can: a folder you chose to keep as it is still uses its icons, and if a history record can't be read, that folder holds the only copy of what was there before.

Found a way around any of this? Please report it the way [SECURITY.md](../SECURITY.md) describes.
