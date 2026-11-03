@echo off
setlocal
cd /d "%~dp0"
call scripts\dotnet-env.cmd
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts\Publish-Folder.ps1
exit /b %errorlevel%
