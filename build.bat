@echo off
setlocal EnableDelayedExpansion

echo.
echo  SteamNetFix Build Script
echo  ========================
echo.
echo  Usage: build.bat [platform]
echo    build.bat              Build all platforms
echo    build.bat win-x64      Build Windows x64
echo    build.bat linux-x64    Build Linux x64
echo    build.bat osx-arm64    Build macOS Apple Silicon
echo.

if exist publish (
    echo  Cleaning old build output...
    rmdir /s /q publish
)
mkdir publish

set PLATFORM=%1
if "%PLATFORM%"=="" set PLATFORM=all

if "%PLATFORM%"=="all" (
    call build_platform.bat win-x64    "Windows x64"
    call build_platform.bat win-arm64  "Windows ARM64"
    call build_platform.bat linux-x64  "Linux x64"
    call build_platform.bat linux-arm64 "Linux ARM64"
    call build_platform.bat osx-x64    "macOS x64"
    call build_platform.bat osx-arm64  "macOS ARM64"
) else (
    call build_platform.bat %PLATFORM% %PLATFORM%
)
echo.
echo  ========================
echo  Done!
echo.
if "%PLATFORM%"=="all" (
    echo  Output directory: publish
    echo.
    for %%R in (win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64) do (
        if exist "publish\%%R\SteamNetFix.exe" (
            for %%F in ("publish\%%R\SteamNetFix.exe") do echo    %%R  (%%~zF bytes)
        )
        if exist "publish\%%R\SteamNetFix" (
            for %%F in ("publish\%%R\SteamNetFix") do echo    %%R  (%%~zF bytes)
        )
    )
) else (
    echo  Output directory: publish\%PLATFORM%
)
echo.
pause
