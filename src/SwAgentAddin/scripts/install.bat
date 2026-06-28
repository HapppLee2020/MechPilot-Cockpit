@echo off
setlocal

set "ADDIN_GUID=E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1"
set "INSTALL_DIR=D:\SWAgentAddin"
set "SOURCE_DIR=%~dp0..\..\deploy\"
set "TARGET_DLL=%INSTALL_DIR%\SwAgentAddin.dll"
set "REGASM=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

echo.
echo =============================================
echo  MechPilot Add-in installer
echo =============================================
echo.

net session >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Please run this installer as Administrator.
    echo         Right-click install.bat and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

if not exist "%SOURCE_DIR%SwAgentAddin.dll" (
    echo [ERROR] Missing DLL:
    echo         %SOURCE_DIR%SwAgentAddin.dll
    echo.
    pause
    exit /b 1
)

if not exist "%REGASM%" (
    echo [ERROR] RegAsm.exe was not found:
    echo         %REGASM%
    echo.
    pause
    exit /b 1
)

echo [1/5] Deploying files to %INSTALL_DIR%
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

REM --- DLLs (root) ---
copy /Y "%SOURCE_DIR%SwAgentAddin.dll" "%INSTALL_DIR%\" >nul
copy /Y "%SOURCE_DIR%SwAgentAddin.pdb" "%INSTALL_DIR%\" >nul 2>&1
copy /Y "%SOURCE_DIR%SolidWorks.Interop.sldworks.dll" "%INSTALL_DIR%\" >nul 2>&1
copy /Y "%SOURCE_DIR%SolidWorks.Interop.swconst.dll" "%INSTALL_DIR%\" >nul 2>&1
copy /Y "%SOURCE_DIR%SolidWorks.Interop.swpublished.dll" "%INSTALL_DIR%\" >nul 2>&1
for %%F in ("%SOURCE_DIR%Microsoft.Web.WebView2.*.dll") do copy /Y "%%F" "%INSTALL_DIR%\" >nul

REM --- frontend ---
if not exist "%INSTALL_DIR%\frontend" mkdir "%INSTALL_DIR%\frontend"
copy /Y "%SOURCE_DIR%frontend\taskpane.html" "%INSTALL_DIR%\frontend\" >nul 2>&1
if exist "%SOURCE_DIR%frontend\cockpit-contracts" xcopy /Y /E /I "%SOURCE_DIR%frontend\cockpit-contracts" "%INSTALL_DIR%\frontend\cockpit-contracts" >nul
if exist "%SOURCE_DIR%frontend\property-workbench" xcopy /Y /E /I "%SOURCE_DIR%frontend\property-workbench" "%INSTALL_DIR%\frontend\property-workbench" >nul

REM --- config (PROTECT existing target config) ---
if not exist "%INSTALL_DIR%\config" mkdir "%INSTALL_DIR%\config"
if not exist "%INSTALL_DIR%\config\config.json" (
    if exist "%SOURCE_DIR%config\config.template.json" (
        copy /Y "%SOURCE_DIR%config\config.template.json" "%INSTALL_DIR%\config\config.json" >nul
        echo       [>>] Config template deployed - please edit config.json before first use
        call "%SOURCE_DIR%validate_config_template.bat" "%INSTALL_DIR%\config\config.json"
    ) else if exist "%SOURCE_DIR%config\config.json" (
        copy /Y "%SOURCE_DIR%config\config.json" "%INSTALL_DIR%\config\" >nul
        echo       [WARN] Legacy config.json copied - prefer config.template.json for new installs
    )
) else (
    echo       [SKIP] Config already exists, preserving target machine config
)
if exist "%INSTALL_DIR%\config\rules.local.json" (
    echo       [SKIP] rules.local.json already exists, preserving target machine rules
) else if exist "%SOURCE_DIR%config\rules.local.json" (
    copy /Y "%SOURCE_DIR%config\rules.local.json" "%INSTALL_DIR%\config\" >nul
    echo       [>>] rules.local.json deployed
)

REM --- assets/icons ---
if not exist "%INSTALL_DIR%\assets\icons" mkdir "%INSTALL_DIR%\assets\icons"
for %%F in ("%SOURCE_DIR%assets\icons\mechpilot-*.bmp") do copy /Y "%%F" "%INSTALL_DIR%\assets\icons\" >nul

REM --- runtimes (WebView2 native) ---
if exist "%SOURCE_DIR%runtimes" xcopy /Y /E /I "%SOURCE_DIR%runtimes" "%INSTALL_DIR%\runtimes" >nul

REM --- scripts (install/uninstall for future use) ---
if not exist "%INSTALL_DIR%\scripts" mkdir "%INSTALL_DIR%\scripts"
copy /Y "%SOURCE_DIR%install.bat" "%INSTALL_DIR%\scripts\" >nul 2>&1
copy /Y "%SOURCE_DIR%uninstall.bat" "%INSTALL_DIR%\scripts\" >nul 2>&1

echo [2/5] Registering COM assembly
echo       DLL: %TARGET_DLL%
"%REGASM%" "%TARGET_DLL%" /unregister >nul 2>&1
"%REGASM%" "%TARGET_DLL%" /codebase /tlb:"%INSTALL_DIR%\SwAgentAddin.tlb"
if errorlevel 1 (
    echo [ERROR] RegAsm registration failed.
    echo.
    pause
    exit /b 1
)

echo [3/5] Registering SolidWorks Add-in discovery keys
reg add "HKLM\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /ve /t REG_DWORD /d 1 /f
if errorlevel 1 goto :reg_failed
reg add "HKLM\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /v Title /t REG_SZ /d "MechPilot" /f
if errorlevel 1 goto :reg_failed
reg add "HKLM\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /v Description /t REG_SZ /d "MechPilot SolidWorks assistant platform" /f
if errorlevel 1 goto :reg_failed

echo [4/5] Registering current-user load state
reg add "HKCU\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /ve /t REG_DWORD /d 1 /f
if errorlevel 1 goto :reg_failed
reg add "HKCU\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /v Title /t REG_SZ /d "MechPilot" /f
if errorlevel 1 goto :reg_failed
reg add "HKCU\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /v Description /t REG_SZ /d "MechPilot SolidWorks assistant platform" /f
if errorlevel 1 goto :reg_failed

echo [5/5] Checking registered CodeBase
reg query "HKCR\CLSID\{%ADDIN_GUID%}\InprocServer32" /v CodeBase /reg:64

echo.
echo =============================================
echo  Install complete.
echo  Registered DLL should be:
echo  file:///D:/SWAgentAddin/SwAgentAddin.DLL
echo =============================================
echo.
pause
exit /b 0

:reg_failed
echo [ERROR] Registry write failed. Please run as Administrator.
echo.
pause
exit /b 1
