@echo off
setlocal enabledelayedexpansion

set "GAME_ROOT=G:\Game\Neverness To Everness"
set "LAUNCHER_EXE=%~dp0target\release\NTERustLauncher.exe"

if not exist "%GAME_ROOT%" (
    echo [ERROR] Game root not found: %GAME_ROOT%
    pause
    exit /b 1
)

if not exist "%LAUNCHER_EXE%" (
    echo [ERROR] NTERustLauncher.exe not found: %LAUNCHER_EXE%
    echo   Please run: cargo build --release
    pause
    exit /b 1
)

echo ==========================================
echo  Neverness To Everness Rust Standalone Launcher
echo ==========================================
echo Game root: %GAME_ROOT%
echo Launcher:  %LAUNCHER_EXE%
echo.

echo [1/1] Launching game with ACE handshake...
"%LAUNCHER_EXE%" "%GAME_ROOT%"

echo.
echo Launcher finished.
echo Log file: "%GAME_ROOT%\LaunchAnalysis\launch.log"
echo.
pause
