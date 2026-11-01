<#
.SYNOPSIS
Builds what a release ships. The release workflow runs this, and you can run it by hand.

.DESCRIPTION
Publishes the self-contained app, adds the license files and then makes:

  <Output>\Foldcraft-<version>-win-x64\      the app, ready to run from a folder
  <Output>\Foldcraft-<version>-win-x64.zip   the same folder as a zip, with a .sha256 file
  <Output>\installer\                        Setup.exe and the update packages

Nothing here runs Setup.exe, installs anything or uploads anything.

.PARAMETER Version
The version to build. Leave it out to use the one in Directory.Build.props.

.PARAMETER Output
Where everything goes. The default is the artifacts folder.

.PARAMETER KeepPreviousPackages
Leaves <Output>\installer as it is, so an earlier full package there gets a delta update.

.PARAMETER TestUpdateFeed
For tools\Test-Update.ps1 only: a local folder this build takes its updates from. It also adds
the installer's portable zip, which a release doesn't ship.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Output,
    [switch] $KeepPreviousPackages,
    [string] $TestUpdateFeed
)

$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param([string] $What, [scriptblock] $Command)

    & $Command

    if ($LASTEXITCODE -ne 0) {
        throw "$What failed with exit code $LASTEXITCODE."
    }
}

function Get-ProjectProperty {
    param([string] $Name)

    $value = dotnet msbuild $project "-getProperty:$Name"

    if ($LASTEXITCODE -ne 0) {
        throw "Could not read $Name from the project."
    }

    return ([string]$value).Trim()
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\Foldcraft.App\Foldcraft.App.csproj'

# dotnet finds vpk through the tool manifest at the top of the repository.
Set-Location $root
Invoke-Checked 'dotnet tool restore' { dotnet tool restore }

if (-not $Version) { $Version = Get-ProjectProperty 'Version' }
if (-not $Output) { $Output = Join-Path $root 'artifacts' }

$packageId = Get-ProjectProperty 'FoldcraftPackageId'
$title = Get-ProjectProperty 'Product'
$publisher = Get-ProjectProperty 'Company'

# The installer's folder is %LOCALAPPDATA%\<id>, and uninstalling empties it.
if ($packageId -eq 'Foldcraft') {
    throw 'The package id must never be the name of the data folder.'
}

$name = "Foldcraft-$Version-win-x64"
$app = Join-Path $Output $name
$installer = Join-Path $Output 'installer'
$zip = "$app.zip"

foreach ($old in @($app, $zip, "$zip.sha256")) {
    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Recurse -Force }
}

if ((Test-Path -LiteralPath $installer) -and -not $KeepPreviousPackages) {
    Remove-Item -LiteralPath $installer -Recurse -Force
}

$publish = @(
    'publish', $project,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '-p:DebugType=none',
    "-p:Version=$Version",
    '--output', $app
)

if ($TestUpdateFeed) { $publish += "-p:FoldcraftTestUpdateFeed=$TestUpdateFeed" }

Invoke-Checked 'dotnet publish' { dotnet @publish }

# A build for the update test takes its updates from a folder, so one must never ship.
$bytes = [IO.File]::ReadAllBytes((Join-Path $app 'Foldcraft.dll'))
$hasTestFeed = [Text.Encoding]::GetEncoding(28591).GetString($bytes).Contains('FoldcraftTestUpdateFeed')

if ($hasTestFeed -and -not $TestUpdateFeed) {
    throw 'This build still holds a test update feed. Delete the obj folders and build again.'
}

Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $app

$packages = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
$runtimePack = Get-ChildItem (Join-Path $packages 'microsoft.netcore.app.runtime.win-x64') -Directory | Select-Object -Last 1
Copy-Item (Join-Path $runtimePack.FullName 'THIRD-PARTY-NOTICES.TXT') (Join-Path $app 'DOTNET-THIRD-PARTY-NOTICES.txt')

$pack = @(
    'vpk', '--skip-updates', 'pack',
    '--packId', $packageId,
    '--packVersion', $Version,
    '--packDir', $app,
    '--mainExe', 'Foldcraft.exe',
    '--packTitle', $title,
    '--packAuthors', $publisher,
    '--icon', (Join-Path $root 'src\Foldcraft.App\Assets\Foldcraft.ico'),
    '--shortcuts', 'StartMenuRoot',
    '--runtime', 'win-x64',
    '--outputDir', $installer
)

if (-not $TestUpdateFeed) { $pack += '--noPortable' }

Invoke-Checked 'vpk pack' { dotnet @pack }

Compress-Archive -Path (Join-Path $app '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name.zip" | Set-Content -Encoding ascii "$zip.sha256"

Write-Output "Built $name in $Output"
