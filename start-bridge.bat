@echo off
echo ========================================
echo   SimChecklist Bridge
echo   https://simchecklist.app
echo ========================================
echo.

:: Check if running from source or compiled
if exist "MSFSBridge.exe" (
    echo Starting compiled version...
    MSFSBridge.exe
) else (
    echo Starting from source...
    dotnet run
)

pause
