<#
.SYNOPSIS
Proves a real update from start to finish, with no GitHub and no registry writes.

.DESCRIPTION
Builds two versions into a unique temp folder, each one taking its updates from a local folder.
Then it unpacks the portable package of the older one and runs UpdateEndToEndTests, which presses
the real buttons: the check, the download, the confirmed restart and undo in the new version.
Finally it deletes everything it made.

Run it twice over: once as a fresh copy that needs the full package, and once holding the older
full package, as an installed copy does, so the delta update gets used.

A portable package writes no uninstall entry and no shortcuts, and the test checks that. This
does open real windows, so leave the mouse and keyboard alone while it runs.

.PARAMETER SampleRoot
An existing folder outside AppData, for one sample folder that gets customized and put back.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SampleRoot,
    [string] $OldVersion = '0.1.0',
    [string] $NewVersion = '0.1.1',
    [string] $Pictures
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$package = Join-Path $PSScriptRoot 'package.ps1'
$work = Join-Path $env:TEMP ('foldcraft-update-test-' + [guid]::NewGuid().ToString('N'))
$build = Join-Path $work 'build'
$feed = Join-Path $build 'installer'
$portable = Join-Path $work 'FoldcraftApp-portable.zip'
$sample = Join-Path $SampleRoot ('update-test-' + [guid]::NewGuid().ToString('N'))
$updaterLog = Join-Path $env:LOCALAPPDATA 'velopack\velopack_FoldcraftApp.log'
$hadUpdaterLog = Test-Path -LiteralPath $updaterLog

function Invoke-Pass {
    param([string] $Name, [switch] $WithBasePackage)

    $app = Join-Path $work "$Name-app"
    $data = Join-Path $work "$Name-data"

    Expand-Archive -LiteralPath $portable -DestinationPath $app

    if ($WithBasePackage) {
        New-Item -ItemType Directory -Force (Join-Path $app 'packages') | Out-Null
        Copy-Item (Join-Path $feed "FoldcraftApp-$OldVersion-full.nupkg") (Join-Path $app 'packages')
    }

    $env:FOLDCRAFT_UPDATE_TEST_APP = $app
    $env:FOLDCRAFT_UPDATE_TEST_DATA = $data
    $env:FOLDCRAFT_UPDATE_TEST_SAMPLE = Join-Path $sample $Name
    $env:FOLDCRAFT_UPDATE_TEST_OLD = $OldVersion
    $env:FOLDCRAFT_UPDATE_TEST_NEW = $NewVersion

    if ($Pictures) { $env:FOLDCRAFT_UPDATE_TEST_PICTURES = $Pictures }

    dotnet test (Join-Path $repo 'tests\Foldcraft.Tests\Foldcraft.Tests.csproj') `
        --configuration Release --no-build --filter 'FullyQualifiedName~UpdateEndToEndTests'

    if ($LASTEXITCODE -ne 0) {
        throw "The update test failed on the $Name pass."
    }

    Write-Output "Passed: $Name"
}

try {
    New-Item -ItemType Directory -Force $work, $sample | Out-Null

    & $package -Version $OldVersion -Output $build -TestUpdateFeed $feed
    Copy-Item (Join-Path $feed 'FoldcraftApp-win-Portable.zip') $portable

    & $package -Version $NewVersion -Output $build -TestUpdateFeed $feed -KeepPreviousPackages

    Get-ChildItem $feed | Select-Object Name, Length | Format-Table -AutoSize

    Invoke-Pass -Name 'full'
    Invoke-Pass -Name 'delta' -WithBasePackage
}
finally {
    Get-Process Foldcraft -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "$work*" } |
        Stop-Process -Force

    foreach ($made in @($sample, $work)) {
        if (Test-Path -LiteralPath $made) {
            Remove-Item -LiteralPath $made -Recurse -Force
        }
    }

    # The updater logs to one place on the PC. A real installed copy's log stays.
    if (-not $hadUpdaterLog -and (Test-Path -LiteralPath $updaterLog)) {
        Remove-Item -LiteralPath $updaterLog -Force
        $folder = Split-Path $updaterLog

        if (-not (Get-ChildItem -LiteralPath $folder -Force)) { Remove-Item -LiteralPath $folder }
    }
}
