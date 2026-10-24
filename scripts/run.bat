@echo off
REM run.bat — Build and run CodeEditor. Pass file paths as arguments to open them on startup.
REM Usage: scripts\run.bat [file.cs file.js ...] [--read-only]
REM   Without args, opens the editor with an empty document.

setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "ROOT_DIR=%SCRIPT_DIR%"
set "ROOT_DIR=%ROOT_DIR:~0,-1%"

REM Sandbox-compatible NuGet cache
if not defined NUGET_HTTP_CACHE_PATH set "NUGET_HTTP_CACHE_PATH=%TEMP%\nuget-cache"
if not defined NUGET_PACKAGES set "NUGET_PACKAGES=%TEMP%\nuget-packages"

set "PROJECT=%ROOT_DIR%\src\CodeEditor\CodeEditor.csproj"

REM Build first
echo === Building ===
call "%SCRIPT_DIR%build.bat" --project "%PROJECT%"

REM Run with forwarded arguments
dotnet run --project "%PROJECT%" -- %*
