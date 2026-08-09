@echo off
REM Dev run. %~dp0 is this script's directory, so it works from anywhere.
REM Extra arguments are passed through, e.g. "dev --no-build".
dotnet run --project "%~dp0CCInfoWindows\CCInfoWindows\CCInfoWindows.csproj" %*
