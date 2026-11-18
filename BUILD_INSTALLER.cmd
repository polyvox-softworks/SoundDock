@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1"
if errorlevel 1 (
    echo.
    echo The installer build failed. See the error above.
    pause
    exit /b 1
)
echo.
echo Open the dist folder to find SoundDock Setup.
explorer.exe "%~dp0dist"
pause
