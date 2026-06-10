@echo off
REM Build helper for SteamNetFix
REM Called by build.bat

set RID=%~1
set NAME=%~2
echo  Building %NAME% (%RID%)...

dotnet publish -c Release -r %RID% -o "publish\%RID%" -v minimal
if %errorlevel% equ 0 (
    if exist "publish\%RID%\SteamNetFix.exe" (
        echo    OK: publish\%RID%\SteamNetFix.exe
        del /q "publish\%RID%\*.pdb" 2>nul
        del /q "publish\%RID%\*.xml" 2>nul
        del /q "publish\%RID%\web.config" 2>nul
        del /q "publish\%RID%\*.staticwebassets.endpoints.json" 2>nul
    )
    if exist "publish\%RID%\SteamNetFix" (
        echo    OK: publish\%RID%\SteamNetFix
        del /q "publish\%RID%\*.pdb" 2>nul
        del /q "publish\%RID%\*.xml" 2>nul
        del /q "publish\%RID%\web.config" 2>nul
        del /q "publish\%RID%\*.staticwebassets.endpoints.json" 2>nul
    )
) else (
    echo    FAILED: build %NAME% error
)
echo.
