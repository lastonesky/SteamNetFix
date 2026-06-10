@echo off
chcp 65001 >nul 2>&1
setlocal EnableDelayedExpansion

echo.
echo  SteamNetFix 构建脚本
echo  ====================
echo.

REM 清理旧的构建
if exist publish (
    echo  清理旧的构建产物...
    rmdir /s /q publish
)
mkdir publish

REM 是否只构建当前平台
set PLATFORM=%1
if "%PLATFORM%"=="" set PLATFORM=all

if "%PLATFORM%"=="all" (
    call :build win-x64    "Windows x64"
    call :build win-arm64  "Windows ARM64"
    call :build linux-x64  "Linux x64"
    call :build osx-x64    "macOS x64"
    call :build osx-arm64  "macOS ARM64 (Apple Silicon)"
) else (
    call :build %PLATFORM% %PLATFORM%
)

echo.
echo  ====================
echo  构建完成!
echo.

if "%PLATFORM%"=="all" (
    echo  产物目录: publish\
    echo.
    echo  各平台文件:
    for %%R in (win-x64 win-arm64 linux-x64 osx-x64 osx-arm64) do (
        if exist "publish\%%R\SteamNetFix.exe" (
            for %%F in ("publish\%%R\SteamNetFix.exe") do echo    %%R  ^(%%~zF bytes^)
        )
        if exist "publish\%%R\SteamNetFix" (
            for %%F in ("publish\%%R\SteamNetFix") do echo    %%R  ^(%%~zF bytes^)
        )
    )
) else (
    echo  产物目录: publish\%PLATFORM%\
)

echo.
pause
goto :eof

:build
set RID=%~1
set NAME=%~2
echo  [%date% %time%] 构建 %NAME% (%RID%)...

dotnet publish -c Release -r %RID% -o "publish\%RID%" -v minimal
if !errorlevel! equ 0 (
    REM 删除多余文件，只保留单个exe和wwwroot
    if exist "publish\%RID%\SteamNetFix.exe" (
        echo    成功: publish\%RID%\SteamNetFix.exe
        REM 清理不需要的文件(pdb、xml、配置等)
        del /q "publish\%RID%\*.pdb" 2>nul
        del /q "publish\%RID%\*.xml" 2>nul
        del /q "publish\%RID%\web.config" 2>nul
        del /q "publish\%RID%\*.staticwebassets.endpoints.json" 2>nul
    )
    if exist "publish\%RID%\SteamNetFix" (
        echo    成功: publish\%RID%\SteamNetFix
        del /q "publish\%RID%\*.pdb" 2>nul
        del /q "publish\%RID%\*.xml" 2>nul
        del /q "publish\%RID%\web.config" 2>nul
        del /q "publish\%RID%\*.staticwebassets.endpoints.json" 2>nul
    )
) else (
    echo    失败: 构建 %NAME% 出错
)
echo.
goto :eof
