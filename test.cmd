@echo off
setlocal
cd /d "%~dp0"
call scripts\dotnet-env.cmd
dotnet run --project tests\WindowsAlarm.Tests --configuration Release -- %*
exit /b %errorlevel%
