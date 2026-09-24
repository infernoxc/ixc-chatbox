@echo off
title Install IXC ChatBox
echo Installing (no admin rights needed)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" %*
echo.
pause

