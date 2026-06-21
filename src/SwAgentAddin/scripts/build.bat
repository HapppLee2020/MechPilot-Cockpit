@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ==========================================
::  MechPilot Add-in — 构建脚本
::  双击运行: build.bat
::  前置条件: .NET Framework 4.8 SDK 已安装
:: ==========================================

echo.
echo  =============================================
echo   MechPilot Add-in 构建
echo  =============================================
echo.

:: 检查 SW_HOME 环境变量
if not defined SW_HOME (
    echo  [检测] SW_HOME 未设置，尝试自动检测...
    
    :: 常见安装路径
    for %%p in (
        "D:\Program Files\SW\2022\SOLIDWORKS"
        "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
        "D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
    ) do (
        if exist "%%~p\SolidWorks.Interop.sldworks.dll" (
            set "SW_HOME=%%~p"
            echo  [检测] 找到 SolidWorks: !SW_HOME!
            goto :found_sw
        )
    )
    
    echo.
    echo  [错误] 未找到 SolidWorks 2022 安装目录。
    echo  请手动设置 SW_HOME 环境变量:
    echo    set SW_HOME=D:\Program Files\SW\2022\SOLIDWORKS
    echo.
    pause
    exit /b 1
)
:found_sw

:: 验证 SW Interop DLL 存在
if not exist "%SW_HOME%\SolidWorks.Interop.sldworks.dll" (
    echo  [错误] 找不到 %SW_HOME%\SolidWorks.Interop.sldworks.dll
    echo  请检查 SW_HOME 路径是否正确。
    pause
    exit /b 1
)
echo  [OK] SW Interop DLL: %SW_HOME%

:: 检查 .NET SDK
where dotnet >nul 2>&1
if errorlevel 1 (
    echo  [错误] 未找到 dotnet CLI。
    echo  请安装 .NET Framework 4.8 SDK 或 .NET 6+ SDK。
    echo  下载: https://dotnet.microsoft.com/download/dotnet-framework
    echo.
    echo  注意: 即使安装 .NET 6+ SDK 也能构建 net48 项目。
    pause
    exit /b 1
)

:: 获取 dotnet 版本
for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do set DOTNET_VER=%%v
echo  [OK] .NET SDK 版本: %DOTNET_VER%

:: 执行构建
echo.
echo  [构建] 开始编译...
echo.

set "BUILD_CONFIG=Release"
set "PROJECT_FILE=%~dp0..\SwAgentAddin.csproj"
set "DEPLOY_DIR=%~dp0..\..\..\deploy"

dotnet build "%PROJECT_FILE%" -c %BUILD_CONFIG%

if errorlevel 1 (
    echo.
    echo  =============================================
    echo   构建失败！请检查上方错误信息。
    echo  =============================================
    echo.
    pause
    exit /b 1
)

:: 验证输出
if exist "%DEPLOY_DIR%\SwAgentAddin.dll" (
    echo.
    echo  =============================================
    echo   构建成功！
    echo
    echo   输出: %DEPLOY_DIR%\SwAgentAddin.dll
    echo
    echo   下一步:
    echo   1. 以管理员身份运行 deploy\install.bat
    echo   2. 编辑 deploy\config.json 填入 MechPilot Server 地址
    echo   3. 启动 SolidWorks 2022
    echo   4. 工具 → 插件 → 勾选 "MechPilot"
    echo  =============================================
) else (
    echo.
    echo  [警告] 构建完成但未找到 deploy\SwAgentAddin.dll
    echo  请检查输出目录。
)

echo.
pause
