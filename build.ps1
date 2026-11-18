[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$Runtime = $Runtime.ToLowerInvariant()
$projectFile = Join-Path $PSScriptRoot 'SoundDock\SoundDock.csproj'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'The .NET 10 SDK is required to build. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and open a new terminal.'
    exit 1
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "publish\$Runtime"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot $OutputDirectory
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$publishArguments = @(
    'publish', $projectFile,
    '--configuration', 'Release',
    '--runtime', $Runtime,
    '--self-contained', 'true',
    '--output', $OutputDirectory,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:GenerateDocumentationFile=false'
)

# Resolve the SDK version from global.json.
Push-Location $PSScriptRoot
try {
    & dotnet @publishArguments
    $publishExitCode = $LASTEXITCODE
    if ($publishExitCode -ne 0) {
        throw "dotnet publish failed (exit code $publishExitCode)."
    }

    $executable = Join-Path $OutputDirectory 'SoundDock.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw 'Publish completed without producing SoundDock.exe.'
    }

    Write-Host "`nReady: $executable"
    Write-Host 'This executable includes the .NET runtime. Copy it to a permanent folder before enabling Start with Windows.'
}
finally {
    Pop-Location
}
