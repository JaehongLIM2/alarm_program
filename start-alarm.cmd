@echo off
cd /d "%~dp0"
if exist "%~dp0artifacts\single-file\WindowsAlarm.exe" (
    start "" "%~dp0artifacts\single-file\WindowsAlarm.exe"
    exit /b
)
start "" "%~dp0artifacts\app\WindowsAlarm.exe"
