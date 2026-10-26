@echo off
setlocal
cd /d "%~dp0"
call scripts\dotnet-env.cmd
dotnet publish src\WindowsAlarm\WindowsAlarm.csproj --configuration Release --no-self-contained --output artifacts\app --nologo
exit /b %errorlevel%
