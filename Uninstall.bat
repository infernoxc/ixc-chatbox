@echo off
title Uninstall IXC
if exist "%LOCALAPPDATA%\IXC-OBS\app\scripts\uninstall.ps1" (powershell -NoProfile -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\IXC-OBS\app\scripts\uninstall.ps1" %*) else (powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\uninstall.ps1" %*)
pause
