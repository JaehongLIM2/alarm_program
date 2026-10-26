@echo off
setlocal
cd /d "%~dp0"
call scripts\dotnet-env.cmd
dotnet build WindowsAlarm.sln --configuration Release --nologo
exit /b %errorlevel%
