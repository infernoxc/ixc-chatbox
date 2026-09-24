@echo off
title Uninstall
if exist "%LOCALAPPDATA%\IXC-OBS\app\scripts\uninstall.ps1" (powershell -NoProfile -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\IXC-OBS\app\scripts\uninstall.ps1" -App chat %*) else (powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\uninstall.ps1" -App chat %*)
pause

