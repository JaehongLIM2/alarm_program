@echo off
setlocal
cd /d "%~dp0"
call scripts\dotnet-env.cmd
dotnet publish src\WindowsAlarm\WindowsAlarm.csproj --configuration Release --no-self-contained --output artifacts\app --nologo
if errorlevel 1 exit /b %errorlevel%
copy /y README.md artifacts\app\README.md >nul
if not exist artifacts\app\docs mkdir artifacts\app\docs
copy /y docs\*.md artifacts\app\docs\ >nul
exit /b %errorlevel%
