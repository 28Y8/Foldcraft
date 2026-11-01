# Security policy

## Reporting a problem

Please don't open a public issue for a security problem. Report it privately through GitHub's private vulnerability reporting:

1. Go to the [Security tab](https://github.com/28Y8/Foldcraft/security) of this repository.
2. Choose **Report a vulnerability**.
3. Tell us what you found, how to reproduce it and what it could lead to.

Only the maintainer and you can see the report. Foldcraft is a volunteer project, so replies may take a few days. You'll hear back about whether the problem is confirmed and what happens next. Please give us a fair chance to fix it before you talk about it publicly. We're happy to credit you when the fix is out.

## What counts

Foldcraft promises it can't damage anything, so anything that breaks that promise matters. For example:

- Changing, losing or overwriting a file that isn't Foldcraft's own
- Getting past a safety check, such as customizing a protected folder or following a junction
- A crafted theme file, picture, shortcut or launcher file that does harm or runs code
- A change that can't be undone
- Anything that gives Foldcraft more rights than the person running it has
- An update check that sends anything beyond what [how safety works](docs/safety.md#updates) describes
- A way to make Foldcraft install an update that isn't from this repository's Releases page
- An install, update or uninstall that touches the data folder

## Supported versions

Foldcraft is at an early stage, so only the latest release gets fixes.

## Downloads

The installer and the app aren't signed, so Windows SmartScreen may warn about them. Only download Foldcraft from this repository's [Releases page](https://github.com/28Y8/Foldcraft/releases). Each release has a `.sha256` file next to the zip, which you can check against the download:

```powershell
Get-FileHash .\Foldcraft-0.1.0-win-x64.zip -Algorithm SHA256
```

## Updates

Foldcraft contacts GitHub only to check for and download updates, over HTTPS and for stable releases only. It sends no personal data and no information about your folders. GitHub sees your IP address, as any website does. You can turn the check off in Settings, and nothing downloads until you press **Update**. A finished download refreshes the updater (`Update.exe`) right away, and the app itself changes only after you agree to restart.

Update packages are integrity checked: the size and SHA-256 hash of each one must match what its release recorded, or it isn't used. They aren't code signed. The hash and the package come from the same release, so this catches a damaged download but can't prove who made it. Keeping this repository's releases safe is what protects updates. See [how safety works](docs/safety.md#updates) for the details.
