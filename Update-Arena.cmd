@echo off
cd /d "%~dp0"
where pwsh >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7 is required. Install it, then run this shortcut again.
  pause
  exit /b 1
)
pwsh -NoProfile -File "%~dp0scripts\publish-arena.ps1"
set "arena_exit=%errorlevel%"
echo.
pause
exit /b %arena_exit%
