[CmdletBinding()]
param([switch]$SkipPublish)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectFile = Join-Path $root 'SoundDock\SoundDock.csproj'
$exe = Join-Path $root 'publish\win-x64\SoundDock.exe'
$innoScript = Join-Path $root 'SoundDock.iss'

if (-not $SkipPublish) {
    & (Join-Path $root 'build.ps1') -Runtime win-x64
    if ($LASTEXITCODE -ne 0) {
        throw 'The SoundDock publish step failed.'
    }
}
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "Missing $exe. Publish SoundDock first or remove -SkipPublish."
}

$iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
if (-not $iscc) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $iscc = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}
if (-not $iscc) {
    throw 'Install Inno Setup 6 or 7 from https://jrsoftware.org/inno-setup/downloads, then try again.'
}

[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw 'Unable to read the application version from SoundDock.csproj.'
}
$version = $versionNode.InnerText.Trim()

Push-Location $root
try {
    & $iscc "/DAppVersion=$version" $innoScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }
}
finally { Pop-Location }

$setupFile = Join-Path $root "dist\SoundDock-Setup-$version-win-x64.exe"
if (-not (Test-Path -LiteralPath $setupFile -PathType Leaf)) {
    throw "Installer compiler returned success but did not create $setupFile."
}
Write-Host "`nInstaller ready: $setupFile" -ForegroundColor Green
