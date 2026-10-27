@echo off
setlocal
cd /d "%~dp0"
call scripts\dotnet-env.cmd
rem A folder deployment keeps Assembly.Location and WebView2 native loading predictable.
rem Include .NET so scheduled launches work even when only .NET 8 is installed system-wide.
dotnet publish src\WindowsAlarm\WindowsAlarm.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts\app --nologo -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugSymbols=false -p:DebugType=None
if errorlevel 1 exit /b %errorlevel%
rem Remove a symbol file left by an earlier publish; distribution must not expose source paths.
if exist artifacts\app\WindowsAlarm.pdb del /q artifacts\app\WindowsAlarm.pdb
if exist artifacts\app\WindowsAlarm.pdb exit /b 1
copy /y README.md artifacts\app\README.md >nul
if not exist artifacts\app\docs mkdir artifacts\app\docs
copy /y docs\*.md artifacts\app\docs\ >nul
exit /b %errorlevel%
