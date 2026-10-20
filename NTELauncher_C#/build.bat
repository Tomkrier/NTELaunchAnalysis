@echo off
chcp 65001 >nul
echo === Compiling NTEGameLauncher.exe ===
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /target:exe /out:"%~dp0NTEGameLauncher.exe" "%~dp0NTEGameLauncher.cs"
if %ERRORLEVEL% equ 0 (
    echo Build succeeded!
) else (
    echo Build failed! Error code: %ERRORLEVEL%
)
pause
