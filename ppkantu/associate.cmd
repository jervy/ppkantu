@echo off
setlocal

echo ========================================
echo  ppkantu - File Association
echo  (HKCU - No admin required)
echo ========================================
echo.

:: Get the directory where this script lives
set "SCRIPT_DIR=%~dp0"

:: Resolve to the exe path.
:: Default usage: copy/run this script next to ppkantu.exe in the published package.
set "EXE_PATH=%SCRIPT_DIR%ppkantu.exe"

:: Developer fallback: allow running from the project folder after `dotnet build`.
if not exist "%EXE_PATH%" (
    set "EXE_PATH=%SCRIPT_DIR%..\artifacts\bin\ppkantu\Debug\net8.0-windows10.0.19041.0\ppkantu.exe"
)

:: Verify the exe exists
if not exist "%EXE_PATH%" (
    echo [ERROR] ppkantu.exe not found at:
    echo   %EXE_PATH%
    echo.
    echo Please build the project first or adjust EXE_PATH in this script.
    exit /b 1
)

echo Using executable:
echo   %EXE_PATH%
echo.

set "PROGID=ppkantu.ImageFile"
set "LEGACY_PROGID=LiteImageViewer.ImageFile"
set "APPKEY=HKCU\Software\Classes\Applications\ppkantu.exe"
set "ORIGINAL_APPKEY=HKCU\Software\Classes\Applications\LiteImageViewer.exe"
set "APP_MARKER=ppkantu.Managed"
set "APPDATAKEY=HKCU\Software\ppkantu"
set "CAPABILITIESKEY=HKCU\Software\ppkantu\Capabilities"
set "REGISTERED_APPS=HKCU\Software\RegisteredApplications"
set "LEGACY_APPKEY=HKCU\Software\Classes\Applications\鹏鹏看图.exe"

:: ============================================
:: 1. Register the ProgId
:: ============================================
echo [1/3] Registering ProgId: %PROGID%

reg add "HKCU\Software\Classes\%PROGID%" /ve /d "鹏鹏看图 图片文件" /f >nul 2>&1
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

:: Remove known legacy application identities and ProgId before registering the current identity.
reg delete "HKCU\Software\Classes\%LEGACY_PROGID%" /f >nul 2>&1
reg delete "%LEGACY_APPKEY%" /f >nul 2>&1
reg delete "%ORIGINAL_APPKEY%" /f >nul 2>&1
reg delete "HKCU\Software\LiteImageViewer" /f >nul 2>&1
reg delete "HKCU\Software\RegisteredApplications" /v LiteImageViewer /f >nul 2>&1
reg add "%APPKEY%" /v "%APP_MARKER%" /t REG_SZ /d "1" /f >nul 2>&1
reg add "%APPKEY%\shell\open\command" /ve /d "\"%EXE_PATH%\" \"%%1\"" /f >nul 2>&1
reg add "%APPKEY%\DefaultIcon" /ve /d "\"%EXE_PATH%\",0" /f >nul 2>&1
if errorlevel 1 (
    echo   [ERROR] Failed to register fixed application identity.
    exit /b 1
)

:: Register application capabilities and the fixed identity metadata.
reg add "%CAPABILITIESKEY%" /v ApplicationName /t REG_SZ /d "鹏鹏看图" /f >nul 2>&1
reg add "%CAPABILITIESKEY%" /v ApplicationDescription /t REG_SZ /d "轻量、干净、无广告的办公图片查看与处理工具" /f >nul 2>&1
reg add "%CAPABILITIESKEY%" /v ApplicationIcon /t REG_SZ /d "\"%EXE_PATH%\",0" /f >nul 2>&1
reg add "%REGISTERED_APPS%" /v ppkantu /t REG_SZ /d "Software\ppkantu\Capabilities" /f >nul 2>&1
for %%E in (.jpg .jpeg .jpe .jfif .png .bmp .dib .gif .webp .tiff .tif .ico .wdp .jxr .hdp) do (
    reg add "%CAPABILITIESKEY%\FileAssociations" /v %%E /t REG_SZ /d "%PROGID%" /f >nul 2>&1
)

echo   OK - Fixed application identity registered.
echo.

:: ============================================
:: 2. Associate each extension with the ProgId
:: ============================================
echo [2/3] Associating file extensions...

set "EXTENSIONS=.jpg .jpeg .jpe .jfif .png .bmp .dib .gif .webp .tiff .tif .ico .wdp .jxr .hdp"
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
echo  Done! Files should now open with ppkantu.
echo  You may need to restart Explorer or log off/on
echo  for all changes to take full effect.
echo ========================================

endlocal
