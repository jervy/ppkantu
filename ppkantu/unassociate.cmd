@echo off
setlocal

echo ========================================
echo  ppkantu - Remove File Association
echo  (HKCU - No admin required)
echo ========================================
echo.

set "PROGID=ppkantu.ImageFile"
set "LEGACY_PROGID=LiteImageViewer.ImageFile"
set "APPKEY=HKCU\Software\Classes\Applications\ppkantu.exe"
set "ORIGINAL_APPKEY=HKCU\Software\Classes\Applications\LiteImageViewer.exe"
set "APP_MARKER=ppkantu.Managed"
set "LEGACY_APPKEY=HKCU\Software\Classes\Applications\鹏鹏看图.exe"
set "APPDATAKEY=HKCU\Software\ppkantu"
set "CAPABILITIESKEY=HKCU\Software\ppkantu\Capabilities"
set "REGISTERED_APPS=HKCU\Software\RegisteredApplications"

:: ============================================
:: 1. Remove extension associations
:: ============================================
echo [1/2] Removing file extension associations...

set "EXTENSIONS=.jpg .jpeg .png .bmp .gif .webp .tiff .tif"
set COUNT=0
set FAIL=0

for %%E in (%EXTENSIONS%) do (
    :: Only remove if it points to our ProgId
    reg query "HKCU\Software\Classes\%%E" /ve 2>nul | findstr /i "%PROGID% %LEGACY_PROGID%" >nul 2>&1
    if not errorlevel 1 (
        reg delete "HKCU\Software\Classes\%%E" /f >nul 2>&1
        if errorlevel 1 (
            echo   [ERROR] Failed to remove %%E
            set /a FAIL+=1
        ) else (
            echo   %%E - Removed
            set /a COUNT+=1
        )
    ) else (
        echo   %%E - Skipped (not associated with ppkantu)
    )
)

echo.
echo   Removed: %COUNT% extension(s), Failed: %FAIL%
echo.

:: ============================================
:: 2. Remove the ProgId
:: ============================================
echo [2/2] Removing ProgId: %PROGID%

reg delete "HKCU\Software\Classes\%PROGID%" /f >nul 2>&1
if errorlevel 1 (
    echo   [ERROR] Failed to delete ProgId key.
    echo   This may already be removed.
) else (
    echo   OK - ProgId removed.
)

:: Remove the fixed and legacy application identity keys.
reg delete "HKCU\Software\Classes\%LEGACY_PROGID%" /f >nul 2>&1
reg delete "%APPKEY%" /f >nul 2>&1
reg delete "%LEGACY_APPKEY%" /f >nul 2>&1
reg delete "%ORIGINAL_APPKEY%" /f >nul 2>&1
reg delete "%APPDATAKEY%" /f >nul 2>&1
reg delete "HKCU\Software\LiteImageViewer" /f >nul 2>&1
reg delete "%REGISTERED_APPS%" /v ppkantu /f >nul 2>&1
reg delete "%REGISTERED_APPS%" /v LiteImageViewer /f >nul 2>&1

echo.
echo ========================================
echo  Done! File associations have been removed.
echo  Windows will fall back to the next registered
echo  handler for each extension.
echo ========================================

endlocal
