@echo off
setlocal

echo ========================================
echo  LiteImageViewer - Remove File Association
echo  (HKCU - No admin required)
echo ========================================
echo.

set "PROGID=LiteImageViewer.ImageFile"

:: ============================================
:: 1. Remove extension associations
:: ============================================
echo [1/2] Removing file extension associations...

set "EXTENSIONS=.jpg .jpeg .png .bmp .gif .webp .tiff .tif"
set COUNT=0
set FAIL=0

for %%E in (%EXTENSIONS%) do (
    :: Only remove if it points to our ProgId
    reg query "HKCU\Software\Classes\%%E" /ve 2>nul | findstr /i "%PROGID%" >nul 2>&1
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
        echo   %%E - Skipped (not associated with LiteImageViewer)
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

echo.
echo ========================================
echo  Done! File associations have been removed.
echo  Windows will fall back to the next registered
echo  handler for each extension.
echo ========================================

endlocal
