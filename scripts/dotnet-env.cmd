@echo off
set "DOTNET_CLI_HOME=%~dp0..\artifacts\dotnet-home"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=false"
set "NUGET_PACKAGES=%~dp0..\artifacts\packages"
