@echo off
setlocal
set "RUNTIME=%~1"
if not defined RUNTIME set "RUNTIME=win-x64"
if /i not "%RUNTIME%"=="win-x64" if /i not "%RUNTIME%"=="win-arm64" (
    echo Usage: build.cmd [win-x64^|win-arm64]
    exit /b 2
)
where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET 10 SDK is required to build SoundDock.
    echo https://dotnet.microsoft.com/download/dotnet/10.0
    exit /b 1
)

pushd "%~dp0"
if errorlevel 1 exit /b 1

dotnet publish "SoundDock\SoundDock.csproj" --configuration Release --runtime "%RUNTIME%" --self-contained true --output "publish\%RUNTIME%" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 goto failed
if not exist "publish\%RUNTIME%\SoundDock.exe" goto failed

echo.
echo Built: "%~dp0publish\%RUNTIME%\SoundDock.exe"
popd
exit /b 0

:failed
echo Build failed.
popd
exit /b 1
