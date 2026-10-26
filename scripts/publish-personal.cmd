@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0Publish-Personal.ps1" %*
exit /b %errorlevel%
