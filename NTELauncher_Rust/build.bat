@echo off
chcp 65001 >nul
echo === Building NTERustLauncher (release) ===
cd /d "%~dp0"
cargo build --release
if %ERRORLEVEL% equ 0 (
    echo Build succeeded!
    echo Output: %~dp0target\release\NTERustLauncher.exe
) else (
    echo Build failed! Error code: %ERRORLEVEL%
)
pause
