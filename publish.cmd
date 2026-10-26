@echo off
setlocal
cd /d "%~dp0"
call scripts\dotnet-env.cmd
dotnet publish src\WindowsAlarm\WindowsAlarm.csproj --configuration Release --no-self-contained --output artifacts\app --nologo -p:DebugSymbols=false -p:DebugType=None
if errorlevel 1 exit /b %errorlevel%
rem Remove a symbol file left by an earlier publish; distribution must not expose source paths.
if exist artifacts\app\WindowsAlarm.pdb del /q artifacts\app\WindowsAlarm.pdb
if exist artifacts\app\WindowsAlarm.pdb exit /b 1
copy /y README.md artifacts\app\README.md >nul
if not exist artifacts\app\docs mkdir artifacts\app\docs
copy /y docs\*.md artifacts\app\docs\ >nul
exit /b %errorlevel%
