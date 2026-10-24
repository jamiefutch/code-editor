@echo off
REM build.bat — Build the CodeEditor project with sandbox-compatible NuGet settings.
REM Usage: scripts\build.bat [--project ^<path^>] [--configuration Debug^|Release]
REM   Without args, builds CodeEditor.csproj in Debug mode.

setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "ROOT_DIR=%SCRIPT_DIR%"
set "ROOT_DIR=%ROOT_DIR:~0,-1%"

REM Sandbox-compatible NuGet cache (outside ~/.nuget which may be read-only)
if not defined NUGET_HTTP_CACHE_PATH set "NUGET_HTTP_CACHE_PATH=%TEMP%\nuget-cache"
if not defined NUGET_PACKAGES set "NUGET_PACKAGES=%TEMP%\nuget-packages"

set "PROJECT_PATH=%ROOT_DIR%\src\CodeEditor\CodeEditor.csproj"
set "CONFIG=Debug"

REM Parse arguments (iterate through %1, %2, etc.)
:parse_loop
if "%~1"=="" goto :end_parse

if /i "%~1"=="--project" (
    set "PROJECT_PATH=%~2"
    shift
    shift
    goto :parse_loop
)
if /i "%~1"=="--configuration" (
    set "CONFIG=%~2"
    shift
    shift
    goto :parse_loop
)
if /i "%~1"=="Debug" (
    set "CONFIG=Debug"
    shift
    goto :parse_loop
)
if /i "%~1"=="Release" (
    set "CONFIG=Release"
    shift
    goto :parse_loop
)

echo Unknown argument: %~1 >&2
exit /b 1

:end_parse

echo === CodeEditor Build ===
echo Root:     %ROOT_DIR%
echo Project:  %PROJECT_PATH%
echo Config:   %CONFIG%
echo NuGet pkg: %NUGET_PACKAGES%
echo.

dotnet clean "%PROJECT_PATH%" -c "%CONFIG%" --nologo && ^
dotnet restore "%PROJECT_PATH%" && ^
dotnet build "%PROJECT_PATH%" -c "%CONFIG%"
