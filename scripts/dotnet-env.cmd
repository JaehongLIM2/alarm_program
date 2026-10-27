@echo off
rem Prefer an optional project-local SDK without changing machine-wide PATH or installed SDKs.
if exist "%~dp0..\artifacts\dotnet10\dotnet.exe" (
    set "DOTNET_ROOT=%~dp0..\artifacts\dotnet10"
    set "PATH=%~dp0..\artifacts\dotnet10;%PATH%"
)
set "DOTNET_CLI_HOME=%~dp0..\artifacts\dotnet-home"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=false"
set "NUGET_PACKAGES=%~dp0..\artifacts\packages"
