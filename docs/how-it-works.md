# How Foldcraft works

## Customizing a folder

Windows has its own way of giving a folder a custom icon, and Foldcraft uses it as it is:

1. **Draw:** the look you designed is rendered to an `.ico` file in Foldcraft's data folder, at nine sizes from 16 to 256 pixels. The file is named after a hash of the look, so the same look is one file however many folders use it.
2. **Check:** the folder goes through the [safety checks](safety.md).
3. **Record:** the change is written to the undo history and flushed to disk.
4. **Write:** a `desktop.ini` file is written inside the folder, pointing at the icon. If the folder already had one, only the icon line changes and the rest stays exactly as it was.
5. **Mark:** Explorer only reads `desktop.ini` in a folder with the read-only or system mark, so Foldcraft adds the read-only mark if neither is there. On a folder, this mark doesn't stop anything from being changed.
6. **Date:** writing the file moved the folder's date, so Foldcraft puts it back, [100 nanoseconds later than it was](#old-pictures).
7. **Tell Explorer:** Foldcraft sends the standard notice that the folder changed. It doesn't restart Explorer.

The result looks like this:

```ini
[.ShellClassInfo]
IconResource=C:\Users\you\AppData\Local\Foldcraft\Icons\example.ico,0
```

### Old pictures

At Medium icons and larger, Windows shows a saved picture of a folder with something in it. That picture is filed under the folder's path and exact date, so while the date stays the same, the old picture stays in use and hides the new look. Details view shows the small icon instead, which is why it was never affected.

So applying a look leaves the folder's date 100 nanoseconds later than it was, and each further look adds one more step. Windows then finds no picture for that date and shows the icon. 100 nanoseconds is the smallest step NTFS can keep, and it worked every time in testing, as did 1 ms, 10 ms, 1 s and 2 s, on dates that fell on an even second, on an odd second or just short of the next one. A file system that keeps dates less finely would round that step away, so Foldcraft reads the date back and takes the smallest step that stayed: 10 ms on exFAT and 2 s on FAT. That part is covered by unit tests, not by a real exFAT or FAT drive.

The picture under the old date is still there, so undo and restore only have to put the exact date back and the real preview returns at once. Nothing is redrawn. Because this is all done with the date, it survives Foldcraft closing or crashing. Foldcraft doesn't open the files in a folder to do it: it reads and changes the folder's `desktop.ini` and lists names when it cleans up a temp file of its own.

While a folder has a look, Windows can't draw a preview of it, and it saves that failure under the date too. So a plain folder must not be left on a date it had with a look on. A date that moved while the look was on, because a file was added or a program set it, may hold such a failure, so undo keeps that date but moves it on. It also steps over every date a Foldcraft look left on that folder, including looks already undone. If a program put the date back to the original, for example, one step on is the date the look used, so the folder gets the step after that.

Windows can still save a failure after the look has gone. A process that has drawn a folder with a look can't draw a preview of it for a while afterward, between 30 and 60 seconds when measured. If a folder has no saved preview for its date, and an open window asks for one in that time, the failure is saved and the folder shows plain. So about 75 seconds after an undo, Foldcraft asks the thumbnail cache with `WTS_INCACHEONLY`, which draws nothing and reads none of the folder's files. If a failure is saved for a folder that has something in it and no icon of its own, Foldcraft moves the date one more step, and Windows draws the preview. Every other folder keeps the date undo gave it. The list of folders to check is in the undo history, so if Foldcraft closes first, it finishes the job the next time it starts, and until then the folder stays plain. A preview that was saved before the look went on is never affected, because nothing has to be drawn. All of this was tested with a long-running test program standing in for Explorer, not in a real Explorer window.

If Foldcraft is stopped partway through an undo, the next try still leaves the right date, because the date it chose goes into the undo history before the folder is touched.

Earlier builds kept the date and had Windows redraw the folder with `IThumbnailCache` and `WTS_FORCEEXTRACTION`. That took the old picture away on undo too, and the same delay meant nothing could replace it, so the folder lost its preview. Retrying within seconds didn't help, and waiting long enough would have needed Foldcraft to stay open.

## The data folder

Foldcraft keeps everything of its own in `%LOCALAPPDATA%\Foldcraft`.

| Name | What it holds |
| --- | --- |
| `Icons` | The icon files your customized folders point to |
| `AppArt` | Pictures read from your apps. Undo doesn't remove these, because the history may need them to redraw an icon |
| `Scratch` | A short-lived place to build a shortcut before it's copied into a new folder |
| `journal.fcj` | The undo history, including the folders still waiting for a second look after an undo |
| `settings.json` | Your settings, including when updates were last checked |
| `settings.unreadable.json` | A settings file Foldcraft couldn't read, moved here when you next change a setting so it isn't lost. Only present after that happens |
| `themes.json` | Themes you saved |
| `rules.json` | Rules you set up |
| `foldcraft.log` | Problems the app recovered from. Update failures are recorded by kind, with no paths |

Your customized folders depend on `Icons`, and undoing depends on `journal.fcj`, so leave this folder in place while you use them. Installing, updating and uninstalling leave it alone.

## Undo, restore and repair

- **Undo** on the History screen reverses one recorded change. One press of Apply counts as one change, however many folders it covered.
- **Restore Original** puts one folder back to how it was before Foldcraft first changed it.
- **Restore Everything** does that for every folder.
- **Remove All Changes** in Settings restores everything, removes the right-click entry and lists what's left.
- **Repair Icons** finds folders whose icon has gone missing, has been damaged or was drawn by an older version, and draws it again from the look recorded when it was applied. Repairs appear in the history like any other change. It can't redraw an icon from before looks were recorded, or one whose picture has since gone or changed, and it says so.

## Rules

A rule says "if a folder matches this, use that look". It can match on a folder's name (contains, is exactly, starts with or a `*` and `?` pattern) or on what the folder mostly holds: pictures, music, videos, documents, code, archives or one file extension. "Mostly" means more than half of the files directly inside.

You choose a folder and how many levels to look through (one to five), then press **Preview**. The preview changes nothing. It lists what would happen, you uncheck anything you don't want and **Apply** does the rest as one undoable change. The first rule that fits a folder wins, and rules skip hidden and system folders.

Rules read file names, plus a matching folder's own `desktop.ini` to see if it already has the look. They never read the contents of your files.

## Themes

A theme file is a small JSON file that you can share:

```json
{
  "format": "foldcraft-themes",
  "version": 1,
  "themes": [
    {
      "name": "Ocean",
      "mood": "Cool",
      "fill": { "start": "#0EA5E9", "end": "#1E3A8A", "angle": 135 },
      "shape": "soft3d",
      "glyph": "fish",
      "badge": { "kind": "done", "corner": "bottomRight", "color": "#16A34A" }
    }
  ]
}
```

Every file is checked strictly when it's imported, and a file that breaks any rule is refused as a whole. See [how safety works](safety.md#files-from-outside).

## Apps and games

**Scan** runs four sources, most specific first, and removes duplicates:

| Source | What it reads |
| --- | --- |
| Steam | Its install location from your account's registry, then its library files and game manifests |
| Roblox | Its per-user install and shortcuts |
| Epic Games | The launcher's manifests in ProgramData |
| Start Menu | Shortcuts for your account and all users, leaving out uninstallers, documentation and parts of Windows |

Each app's icon is read from the software on your PC, and the folder's colors are picked from it. If the icon would disappear against the folder, Foldcraft shifts the folder's color or puts a subtle plate behind the icon. A small icon is drawn smaller to stay sharp, and you're told. Pictures stay in memory until you press the button that makes folders.

Pressing that button records and then makes each folder, with its shortcut inside. The whole run undoes as one step.

- If a name is taken, Foldcraft skips that item or adds a number, as you prefer. Names Windows doesn't allow are fixed first.
- Roblox and some Windows Installer apps start through their own Start Menu shortcut, because their program moves. If you uninstall the app or remove that shortcut, the shortcut in your folder stops working.
- **Use on a Folder...** gives a folder you already have the same look, such as a game library on another drive.
- Changing a game's own install folder is off until you turn it on, because a game update can undo it. Protected places stay blocked, so many games are skipped.

## The right-click menu

This is off until you turn it on in Settings. It adds one entry for your account only, under `HKCU\Software\Classes\Directory\shell`, and needs no administrator rights. The app writes nothing else to the registry. It reads where Steam is installed when you press **Scan**, reads its own entry when you turn the setting on or off and, in an installed copy with the entry on, reads it once at each start to check it's still there.

Turning it off removes exactly what was added and nothing else. If another app changed that entry in the meantime, Foldcraft leaves it alone and tells you. If you move a copy from the zip to another folder, Settings says so and offers to update the entry.

An installed copy runs from `%LOCALAPPDATA%\FoldcraftApp\current\Foldcraft.exe`, and an update replaces what's in `current` and keeps that path, so the entry goes on working with nothing to change. If the program it points to has gone, Foldcraft moves the entry to this copy at its next start. If the entry itself has gone, as after an uninstall and a new install, the switch goes back to off and Settings says so, because the entry is only ever added when you turn it on.

Uninstalling removes the entry through the same code, and only if it starts the copy being removed. That step reads the settings file and changes nothing in the data folder.

## Installing and updating

Download `FoldcraftApp-win-Setup.exe` from the [Releases page](https://github.com/28Y8/Foldcraft/releases). If SmartScreen says it protected your PC, choose **More info** and then **Run anyway**. It shows up because the installer isn't signed. .NET comes included.

The `Foldcraft-...-win-x64.zip` on the same page runs from any folder of its own, and that copy can't update itself. To remove one, press **Remove All Changes** in Settings if you want your folders put back, and then delete its folder.

The installer is built with [Velopack](https://velopack.io). It's per-user, so it needs no administrator rights:

| Path | What's there |
| --- | --- |
| `%LOCALAPPDATA%\FoldcraftApp\current` | The app. An update replaces this folder |
| `%LOCALAPPDATA%\FoldcraftApp\Update.exe` | The updater, which installs a downloaded update and runs the uninstall. A finished download replaces it right away |
| `%LOCALAPPDATA%\FoldcraftApp\packages` | The downloaded update, until it's installed |
| `%LOCALAPPDATA%\velopack\velopack_FoldcraftApp.log` | The updater's log. It stays on your PC and an uninstall leaves it behind |

Installing also adds a Start Menu shortcut and the uninstall entry for your account. None of this is the data folder, which is `%LOCALAPPDATA%\Foldcraft`. The installer's id, `FoldcraftApp`, keeps the two apart, and Velopack only ever replaces or empties the folder named after that id.

An update goes like this:

1. **Check:** at startup, at most once a day and off the main thread, Foldcraft asks GitHub Releases for the newest stable version. This never delays the start, and it stays silent if it fails.
2. **Offer:** a banner shows the version and a link to its release notes. Nothing has been downloaded.
3. **Download:** pressing **Update** downloads the update, with progress and a Cancel button. If you already have the version before it, this is usually a small delta. Otherwise it's the whole app. Each package that's downloaded, delta or full, is checked against the size and SHA-256 hash recorded in the release. The full package rebuilt from deltas isn't checked again as a whole, because rebuilding it can't be expected to give back the same bytes. A download that makes no progress for 10 minutes stops by itself. When a download finishes, Velopack also replaces `Update.exe` with the new version's right away, before any restart. A download that's canceled or fails can clear the `packages` folder, so the next try may need the whole app.
4. **Restart:** the app itself doesn't change until you confirm. If you say no, the update waits and the banner offers it at the next start. Foldcraft won't restart while it's working or while another copy is running from the same install, because `Update.exe` would stop that copy.
5. **Install:** Foldcraft closes in the normal way, `Update.exe` swaps the `current` folder and starts the new version with the same data folder.

Automatic install on the next start, which Velopack does by default, is turned off. A stray `--veloapp-` argument can't show anything either: those runs belong to the installer, and they end before any window or the single-instance check exists.

[How safety works](safety.md#updates) says what's sent and what isn't.

## Command line

| Option | What it does |
| --- | --- |
| `--customize "<folder>"` | Adds a folder to the list. If Foldcraft is already open, the folder joins that window. Nothing changes until you press Apply |
| `--data-dir "<folder>"` | Uses another data folder, which is handy for trying the app without touching your real one |

## Known limitations

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

## Project layout

| Path | What's there |
| --- | --- |
| `src/Foldcraft.Core` | Safety checks, icon rendering, the undo history, applying and restoring, themes, rules, app discovery and the right-click entry. It has no UI |
| `src/Foldcraft.App` | The WPF app, using .NET's built-in Fluent theme and hand-written MVVM |
| `tests/Foldcraft.Tests` | xUnit tests for both |
| `tools` | The script that builds a release and the end-to-end update test |
| `docs` | These pages and pictures of the app |

The folder is drawn at the same size and height as the stock Windows 11 folder so the two line up in File Explorer, but it's drawn from scratch and no stock art is used.
