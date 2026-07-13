@echo off
setlocal

echo ========================================
echo  LiteImageViewer - File Association
echo  (HKCU - No admin required)
echo ========================================
echo.

:: Get the directory where this script lives
set "SCRIPT_DIR=%~dp0"

:: Resolve to the exe path.
:: Default usage: copy/run this script next to LiteImageViewer.exe in the published package.
set "EXE_PATH=%SCRIPT_DIR%LiteImageViewer.exe"

:: Developer fallback: allow running from the project folder after `dotnet build`.
if not exist "%EXE_PATH%" (
    set "EXE_PATH=%SCRIPT_DIR%..\artifacts\bin\LiteImageViewer\Debug\net8.0-windows10.0.19041.0\LiteImageViewer.exe"
)

:: Verify the exe exists
if not exist "%EXE_PATH%" (
    echo [ERROR] LiteImageViewer.exe not found at:
    echo   %EXE_PATH%
    echo.
    echo Please build the project first or adjust EXE_PATH in this script.
    exit /b 1
)

echo Using executable:
echo   %EXE_PATH%
echo.

set "PROGID=LiteImageViewer.ImageFile"

:: ============================================
:: 1. Register the ProgId
:: ============================================
echo [1/3] Registering ProgId: %PROGID%

reg add "HKCU\Software\Classes\%PROGID%" /ve /d "LiteImageViewer Image File" /f >nul 2>&1
if errorlevel 1 (
    echo   [ERROR] Failed to create ProgId key.
    exit /b 1
)

reg add "HKCU\Software\Classes\%PROGID%\shell\open\command" /ve /d "\"%EXE_PATH%\" \"%%1\"" /f >nul 2>&1
if errorlevel 1 (
    echo   [ERROR] Failed to set open command.
    exit /b 1
)

reg add "HKCU\Software\Classes\%PROGID%\DefaultIcon" /ve /d "\"%EXE_PATH%\",0" /f >nul 2>&1
if errorlevel 1 (
    echo   [WARNING] Failed to set default icon (non-fatal).
)

echo   OK - ProgId registered.
echo.

:: ============================================
:: 2. Associate each extension with the ProgId
:: ============================================
echo [2/3] Associating file extensions...

set "EXTENSIONS=.jpg .jpeg .png .bmp .gif .webp .tiff .tif"
set COUNT=0
set FAIL=0

for %%E in (%EXTENSIONS%) do (
    reg add "HKCU\Software\Classes\%%E" /ve /d "%PROGID%" /f >nul 2>&1
    if errorlevel 1 (
        echo   [ERROR] Failed to associate %%E
        set /a FAIL+=1
    ) else (
        echo   %%E - OK
        set /a COUNT+=1
    )
)

echo.
echo   Associated: %COUNT% extension(s), Failed: %FAIL%
echo.

:: ============================================
:: 3. Notify Explorer to refresh associations
:: ============================================
echo [3/3] Notifying Explorer to refresh...
echo   (This may take a moment to take effect)
echo.

echo ========================================
echo  Done! Files should now open with LiteImageViewer.
echo  You may need to restart Explorer or log off/on
echo  for all changes to take full effect.
echo ========================================

endlocal
