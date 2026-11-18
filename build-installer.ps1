[CmdletBinding()]
param([switch]$SkipPublish)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectFile = Join-Path $root 'SoundDock\SoundDock.csproj'
$innoScript = Join-Path $root 'SoundDock.iss'
$publishDir = Join-Path $root 'publish\win-x64'
$exe = Join-Path $publishDir 'SoundDock.exe'
$distDir = Join-Path $root 'dist'

# This script publishes the application itself. It does not rely on build.ps1,
# which may not be present when installer support is added to an existing repo.
if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
    throw "SoundDock.csproj was not found at '$projectFile'. Place the installer files in the repository root, alongside the SoundDock project folder."
}
if (-not (Test-Path -LiteralPath $innoScript -PathType Leaf)) {
    throw "SoundDock.iss was not found at '$innoScript'."
}

[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw 'Unable to read the application version from SoundDock.csproj.'
}
$version = $versionNode.InnerText.Trim()

if (-not $SkipPublish) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0.'
    }

    $publishArguments = @(
        'publish', $projectFile,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $publishDir,
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:PublishTrimmed=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-p:GenerateDocumentationFile=false'
    )

    Write-Host 'Publishing SoundDock for Windows x64...'
    Push-Location $root
    try {
        & dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "SoundDock.exe was not found at '$exe'. Run without -SkipPublish to publish the application first."
}

$isccCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
$iscc = if ($isccCommand) { $isccCommand.Source } else { $null }
if (-not $iscc) {
    $searchRoots = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique

    foreach ($programFiles in $searchRoots) {
        foreach ($folder in @('Inno Setup 6', 'Inno Setup 7')) {
            $candidate = Join-Path (Join-Path $programFiles $folder) 'ISCC.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $iscc = $candidate
                break
            }
        }
        if ($iscc) { break }
    }
}
if (-not $iscc) {
    throw 'Inno Setup 6 or 7 was not found. Install it from https://jrsoftware.org/inno-setup/downloads.'
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
$setupFile = Join-Path $distDir "SoundDock-Setup-$version-win-x64.exe"

Write-Host "Building SoundDock $version installer..."
Push-Location $root
try {
    & $iscc "/DAppVersion=$version" $innoScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $setupFile -PathType Leaf)) {
    throw "Inno Setup completed without creating '$setupFile'."
}
Write-Host "Installer ready: $setupFile" -ForegroundColor Green
