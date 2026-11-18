@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 exit /b 1

echo Building SoundDock...
call "%~dp0build.cmd" win-x64
if errorlevel 1 (
    echo.
    echo Build failed. Check the output above.
    pause
    popd
    exit /b 1
)

start "" "%~dp0publish\win-x64\SoundDock.exe"
popd
