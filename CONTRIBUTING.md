# Contributing to Foldcraft

Thanks for helping out. Bug reports, ideas and pull requests are all welcome. By taking part you agree to the [code of conduct](CODE_OF_CONDUCT.md).

## The one rule that matters most

Foldcraft changes how folders look and can never damage anything. Every change must be recorded before it happens and must be undoable. Foldcraft stays usable without administrator rights, and it never restarts Explorer. A pull request that weakens any of this won't be merged, however nice the feature is.

## Getting set up

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build
dotnet test
```

Builds run with warnings as errors. To try the app without touching your real data, give it a throwaway data folder:

```powershell
dotnet run --project src/Foldcraft.App -- --data-dir "$env:TEMP\foldcraft-trial"
```

## Tests

- New behavior needs a test, and a bug fix needs a test that failed before the fix.
- Tests must only touch folders inside a `Sandbox`, which lives in the temp directory, and must clean up after themselves.
- Tests must never write to the real registry. Code that reaches the registry goes through `IUserRegistry`, and tests use `FakeRegistry`.
- Tests must never scan the real PC. Use `FakePc`, with icons drawn in code by `DrawnArt`.
- A test that opens a real window gets `[Trait("Category", TestCategories.Desktop)]`. CI still runs these, but a failure there doesn't fail the build, because they depend on the runner's desktop. To leave them out:

  ```powershell
  dotnet test --filter "Category!=Desktop"
  ```

- Tests must never reach the network. Code that does goes through `IUpdateService`, and tests use `FakeUpdateService`.
- The installer's package id is what keeps installs away from the data folder. Never make it `Foldcraft`. `InstallRootTests` guards this.

Don't weaken or delete a test to get a green build. If a test really has to change, say why in the pull request.

## Looking at your changes

These environment variables save pictures while the tests run. Point them at a folder of your own under your temp directory.

| Variable | What it saves | Run |
| --- | --- | --- |
| `FOLDCRAFT_SCREENSHOT_DIR` | Every screen in light and dark, at each stage of the window test | `dotnet test` |
| `FOLDCRAFT_ART_DIR` | Contact sheets of the folders, symbols and badges | `dotnet test` |
| `FOLDCRAFT_README_DIR` | The pictures in `docs/images` | `dotnet test --filter "FullyQualifiedName~The_window_works"` |

`Assets/Foldcraft.ico` is the renderer's own output, and a test fails if the two drift apart. If you changed how folders are drawn, run that test once with `FOLDCRAFT_WRITE_APP_ICON=1` to write a fresh one.

The README pictures are real renders of the window on sample folders. The test swaps real paths for sample ones right before each picture and refuses to save one if a real path is left. Read every picture before committing it.

## Art and licensing

Everything in this repository is our own work under the MIT license. Third-party logos and trademarked art must never be committed: not in source, tests, fixtures, screenshots or docs. App icons are read from a user's own PC at run time and stay there. Tests draw their own.

Every picture in the repository sits in `docs/images` and is named in `WritingRulesTests`. Adding one means adding its name there, after checking it shows no third-party logo and no personal data such as real user paths or a Windows account name.

A new dependency needs a license compatible with MIT and an entry in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Writing rules

These apply everywhere: code comments, text in the app, docs, test names and templates. `WritingRulesTests` checks what a test can check.

- Never use the em dash character. Use a colon, a comma, parentheses or a new sentence.
- Never put a comma before the final "and" or "or" in a list. Write "lettuce, cheese and meat".
- Use US spelling, like "color".
- Keep text in the app plain and friendly, and never mention the machinery, like `desktop.ini`, on a main screen.

## Code style

Match the code around you. `.editorconfig` covers the basics. The app uses hand-written MVVM and the built-in Fluent theme, so please don't add a framework for either. All file system work goes through `FileOperations` so tests can make it fail, and every failure in a batch is reported per folder and never stops the rest.

## Pull requests

1. Open an issue first for anything large, so no one's time is wasted.
2. Keep a pull request to one thing.
3. Update the README, docs and [CHANGELOG.md](CHANGELOG.md) if what you changed shows up there.
4. Fill in the pull request template.

## Making a release

Pushing a tag is the one step that makes a release:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

Get the commit ready first:

1. Set the version in `Directory.Build.props` and in `src/Foldcraft.App/app.manifest`.
2. Add the version and date to `CHANGELOG.md`, and update the version the `AboutViewModelTests` expect.
3. Run all the tests on a real desktop, including the desktop ones.
4. Run `tools\Test-Update.ps1 -SampleRoot <an empty folder of yours>`. It builds two versions, updates one to the other through the real app and the real updater with a local folder in place of GitHub, and cleans up after itself. It makes no registry changes and adds no shortcuts.

The release workflow then tests the code and stops if the tag doesn't match the version. It fetches the previous full package so the update can include a delta and runs `tools\package.ps1`, all with a token that can only read. A second job, which runs none of the project's code, then publishes a release holding `FoldcraftApp-win-Setup.exe`, the update packages and the portable zip. The release is a draft until every file is up, so nobody can be offered half of one. A version with a suffix, like `0.2.0-rc1`, is marked as a prerelease, and Foldcraft never offers those.

`tools\package.ps1` makes the same files in `artifacts` on your own PC and uploads nothing. Don't run `Setup.exe` on a PC whose Foldcraft you care about without reading [how safety works](docs/safety.md#installing-updating-and-uninstalling).

The `Velopack` package and the `vpk` tool in `dotnet-tools.json` have to stay on the same exact version. Dependabot groups them, and `InstallRootTests` fails if they drift.

Each project's `packages.lock.json` pins its packages by hash. After you change a package, run `dotnet restore` and commit the lock files, because GitHub Actions refuses a restore that doesn't match them.
