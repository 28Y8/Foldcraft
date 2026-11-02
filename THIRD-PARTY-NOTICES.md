# Third-party notices

Foldcraft itself is under the [MIT license](LICENSE). This file lists what it's built on.

## Included in a release

The installer, the update packages and the release zip are self-contained, so they carry these with them.

| Component | Version | License | Copyright |
| --- | --- | --- | --- |
| .NET runtime (`Microsoft.NETCore.App`) | 10.0 | MIT | .NET Foundation and Contributors |
| Windows Desktop runtime, including WPF (`Microsoft.WindowsDesktop.App`) | 10.0 | MIT | .NET Foundation and Contributors |
| `System.Drawing.Common` | 10.0.0 | MIT | Microsoft Corporation |
| `Microsoft.Win32.SystemEvents` | 10.0.0 | MIT | Microsoft Corporation |
| `Velopack`, the library that checks for and downloads updates | 1.2.161 | MIT | Caelan Sayler and Velopack Ltd. |
| Velopack's `Setup.exe`, `Update.exe` and launcher, which `vpk` adds to the installer and update packages | 1.2.161 | MIT | Caelan Sayler and Velopack Ltd. |

The .NET runtime includes third-party code of its own. Its notices are in `DOTNET-THIRD-PARTY-NOTICES.txt` in the release, and in the [.NET runtime repository](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT).

Velopack's `Setup.exe`, `Update.exe` and launcher are compiled from Rust and include open-source Rust libraries. Those are listed in [Velopack's `Cargo.lock`](https://github.com/velopack/velopack/blob/1.2.161/Cargo.lock) for that version, and Velopack's [`deny.toml`](https://github.com/velopack/velopack/blob/1.2.161/deny.toml) sets which licenses they may use.

## Used only to build a release

These make the installer and are never included in a release.

| Component | Version | License | Copyright |
| --- | --- | --- | --- |
| `vpk`, the Velopack command line tool | 1.2.161 | MIT | Caelan Sayler and Velopack Ltd. |

## Used only to test the code

These are never included in a release.

| Component | Version | License | Copyright |
| --- | --- | --- | --- |
| `xunit`, with `xunit.core`, `xunit.assert`, `xunit.abstractions`, `xunit.analyzers`, `xunit.extensibility.core` and `xunit.extensibility.execution` | 2.9.3 | Apache-2.0 | .NET Foundation and Contributors |
| `xunit.runner.visualstudio` | 3.1.4 | Apache-2.0 | .NET Foundation and Contributors |
| `Microsoft.NET.Test.Sdk`, with `Microsoft.TestPlatform.ObjectModel`, `Microsoft.TestPlatform.TestHost` and `Microsoft.CodeCoverage` | 17.14.1 | MIT | Microsoft Corporation |
| `coverlet.collector` | 6.0.4 | MIT | Coverlet contributors |
| `Newtonsoft.Json` | 13.0.3 | MIT | James Newton-King |

## Used from Windows, never included

Foldcraft draws its text with the Segoe UI and Segoe Fluent Icons fonts, and emoji with Segoe UI Emoji, all from the copy of Windows it runs on. No font is included in the repository or in a release.

## Icons of other software

The Apps and Games screen reads icons from software already on the PC it runs on. They stay on that PC. None are included in the repository or in a release, and they remain the property of their owners.

## Text

[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) is adapted from the [Contributor Covenant](https://www.contributor-covenant.org) version 2.1, which is under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). That file says what was changed.

## Art

All symbols, badges and folder art are plain vector geometry written for this project. Nothing in the repository comes from a third-party icon set, and it holds no logos.

## Trademarks

Windows, Steam, Roblox, Epic Games, OneDrive and all other product names belong to their owners. They're mentioned only to say what Foldcraft works with. Foldcraft is an independent project and is not affiliated with, endorsed by or sponsored by any of them.
