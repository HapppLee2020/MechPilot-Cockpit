@echo off
chcp 65001 >nul
setlocal

:: ==========================================
::  MechPilot Add-in — 卸载脚本
::  以管理员身份运行: 右键 uninstall.bat → 以管理员身份运行
:: ==========================================

set "ADDIN_GUID=E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1"
set "DLL_PATH=%~dp0SwAgentAddin.dll"

:: 查找 RegAsm.exe
set "REGASM="
for %%p in (
    "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
    "%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"
) do (
    if exist %%p set "REGASM=%%~p"
)

echo.
echo  =============================================
echo   MechPilot Add-in 卸载
echo  =============================================
echo.

:: 1. 从 SolidWorks Add-in 列表移除
echo  [1/3] 从 SolidWorks 插件列表移除...
reg delete "HKCU\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /f >nul 2>&1
reg delete "HKLM\Software\SolidWorks\Addins\{%ADDIN_GUID%}" /f >nul 2>&1
echo        HKCU + HKLM 已移除。

:: 2. 反注册 COM 组件
echo  [2/3] 反注册 COM 组件...
if exist "%DLL_PATH%" (
    if not "%REGASM%"=="" (
        "%REGASM%" "%DLL_PATH%" /unregister
        echo        反注册成功。
    ) else (
        echo        未找到 RegAsm，跳过反注册。
    )
) else (
    echo        DLL 不存在，跳过反注册。
)

:: 3. 提示
echo  [3/3] 清理文件...
echo.
echo  以下文件将被保留（不会自动删除）:
echo    - config.json  (配置文件)
echo    - logs\        (日志目录)
echo.
echo  如需完全清理，请手动删除:
echo    %~dp0
echo.

echo  =============================================
echo   卸载完成！请重启 SolidWorks 2022 使卸载生效。
echo  =============================================
echo.
pause
