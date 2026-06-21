@echo off
setlocal

set "ADDIN_GUID=E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1"
set "DLL_PATH=%~dp0SwAgentAddin_tabicons_v2.dll"
set "TLB_PATH=%~dp0SwAgentAddin_tabicons_v2.tlb"
set "REGASM=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

echo.
echo =============================================
echo  MechPilot Add-in installer
echo  Target: SwAgentAddin_tabicons_v2.dll
echo =============================================
echo.

net session >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Please run this installer as Administrator.
    pause
    exit /b 1
)

if not exist "%DLL_PATH%" (
    echo [ERROR] Missing DLL:
    echo         %DLL_PATH%
    pause
    exit /b 1
)

if not exist "%REGASM%" (
    echo [ERROR] RegAsm.exe was not found:
    echo         %REGASM%
    pause
    exit /b 1
)

echo [1/4] Registering COM assembly
"%REGASM%" "%DLL_PATH%" /unregister >nul 2>&1
"%REGASM%" "%DLL_PATH%" /codebase /tlb:"%TLB_PATH%"
if errorlevel 1 (
    echo [ERROR] RegAsm registration failed.
    pause
    exit /b 1
)

echo [2/4] Registering SolidWorks Add-in discovery keys
reg add "HKLM\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /ve /t REG_DWORD /d 1 /f
if errorlevel 1 goto :reg_failed
reg add "HKLM\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /v Title /t REG_SZ /d "MechPilot" /f
if errorlevel 1 goto :reg_failed
reg add "HKLM\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /v Description /t REG_SZ /d "MechPilot SolidWorks assistant platform" /f
if errorlevel 1 goto :reg_failed

echo [3/4] Registering current-user load state
reg add "HKCU\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /ve /t REG_DWORD /d 1 /f
if errorlevel 1 goto :reg_failed
reg add "HKCU\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /v Title /t REG_SZ /d "MechPilot" /f
if errorlevel 1 goto :reg_failed
reg add "HKCU\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /v Description /t REG_SZ /d "MechPilot SolidWorks assistant platform" /f
if errorlevel 1 goto :reg_failed

echo [4/4] Checking registered CodeBase
reg query "HKCR\CLSID\{%ADDIN_GUID%}\InprocServer32" /v CodeBase /reg:64

echo.
echo =============================================
echo  Install complete. Restart SolidWorks.
echo =============================================
echo.
pause
exit /b 0

:reg_failed
echo [ERROR] Registry write failed. Please run as Administrator.
pause
exit /b 1
