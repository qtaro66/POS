@echo off
rem Double-click launcher: runs verify.ps1 with PowerShell 7 and keeps the window open.
pwsh -NoProfile -File "%~dp0verify.ps1" %*
echo.
pause
