@echo off
setlocal
cd /d "%~dp0"
call scripts\dotnet-env.cmd
rem Keep reflection, WPF resources, WinRT and the native WebView2 loader intact.
dotnet publish src\WindowsAlarm\WindowsAlarm.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts\single-file-build --nologo -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=true -p:DebugSymbols=false -p:DebugType=None
if errorlevel 1 exit /b %errorlevel%
if not exist artifacts\single-file mkdir artifacts\single-file
copy /y artifacts\single-file-build\WindowsAlarm.exe artifacts\single-file\WindowsAlarm.exe >nul
if errorlevel 1 exit /b %errorlevel%
powershell.exe -NoProfile -NonInteractive -Command "$items = @(Get-ChildItem -LiteralPath 'artifacts\single-file' -Force); if ($items.Count -ne 1 -or $items[0].Name -ne 'WindowsAlarm.exe') { throw 'The standalone output must contain only WindowsAlarm.exe.' }"
exit /b %errorlevel%
