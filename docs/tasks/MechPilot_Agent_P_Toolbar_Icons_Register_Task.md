# Agent P Prompt：工具栏三分区、蓝图图标、SW 注册与部署稳定性

任务书地址：

`E:\0B 软件开发\MechPilot\Cockpit\docs\tasks\MechPilot_Agent_P_Toolbar_Icons_Register_Task.md`

## 背景

MechPilot 当前已经迁移到新工程目录：

`E:\0B 软件开发\MechPilot\Cockpit`

这是 SolidWorks 插件项目，目标是落地新版双核心架构：

- `AIPilot / AICockpit`：AI 工具、图纸审核、快速选型、物料检索、设计计算、Hermes 远端 Agent 联动。
- `LocalToolbelt`：本地属性、BOM、批量转换、图纸导出、打包备份等高频工程工具。

本任务负责工具栏入口、图标、注册、部署稳定性。

## 重要路径

工程根目录：

`E:\0B 软件开发\MechPilot\Cockpit`

源码目录：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin`

主代码：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.cs`

项目文件：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.csproj`

源码图标目录：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\assets\icons`

工程 deploy 目录：

`E:\0B 软件开发\MechPilot\Cockpit\deploy`

真实运行部署目录：

`D:\SWAgentAddin`

新的部署结构：

```text
D:\SWAgentAddin
├─ SwAgentAddin.dll
├─ config.json
├─ rules.local.json
├─ assets\icons\
├─ config\
├─ frontend\property-workbench\
├─ frontend\cockpit-contracts\
├─ logs\
├─ scripts\
├─ runtimes\
└─ cockpit-cache\
```

加载日志：

`D:\SWAgentAddin\addin-load.log`

## 新图标资产

优先使用 Ribbon 风格图标，视觉参考用户截图：白底、黑色线性符号、小面积彩色强调，中文名称由 SolidWorks CommandManager 显示。

使用这套新图标：

- `src\SwAgentAddin\assets\icons\mechpilot-ribbon-icons-16-20.bmp`
- `src\SwAgentAddin\assets\icons\mechpilot-ribbon-icons-16-32.bmp`
- `src\SwAgentAddin\assets\icons\mechpilot-ribbon-main-20.bmp`
- `src\SwAgentAddin\assets\icons\mechpilot-ribbon-main-32.bmp`
- 预览：`src\SwAgentAddin\assets\icons\mechpilot-ribbon-icons-16-preview.png`

旧蓝图图标只作为备选：

- `src\SwAgentAddin\assets\icons\mechpilot-blueprint-icons-16-20.bmp`
- `src\SwAgentAddin\assets\icons\mechpilot-blueprint-icons-16-32.bmp`
- `src\SwAgentAddin\assets\icons\mechpilot-blueprint-main-20.bmp`
- `src\SwAgentAddin\assets\icons\mechpilot-blueprint-main-32.bmp`
- 预览：`src\SwAgentAddin\assets\icons\mechpilot-blueprint-icons-16-preview.png`

请把 `.csproj` 的复制规则更新为支持这些新图标，构建后它们应进入：

`E:\0B 软件开发\MechPilot\Cockpit\deploy\assets\icons`

安装后它们应进入：

`D:\SWAgentAddin\assets\icons`

## 工具栏按钮设计

按钮名称尽量四个字，按三分区组织。

| Index | 名称 | 分区 | 回调建议 |
|------:|------|------|----------|
| 0 | 驾驶舱 | AI 工具区 | `SwCmd_Cockpit` |
| 1 | AI助手 | AI 工具区 | `SwCmd_AIAssistant` |
| 2 | 图纸审核 | AI 工具区 | `SwCmd_AIDrawingReview` |
| 3 | 快速选型 | AI 工具区 | `SwCmd_AISelection` |
| 4 | 物料检索 | AI 工具区 | `SwCmd_MaterialSearch` |
| 5 | 设计计算 | AI 工具区 | `SwCmd_DesignCalc` |
| 6 | 属性填写 | 本地工程工具区 | `SwCmd_PropertyFill` |
| 7 | 读取属性 | 本地工程工具区 | `SwCmd_ReadProperties` |
| 8 | 属性检查 | 本地工程工具区 | `SwCmd_PropertyCheck` |
| 9 | BOM导出 | 本地工程工具区 | `SwCmd_BomExport` |
| 10 | 批量转换 | 本地工程工具区 | `SwCmd_BatchConvert` |
| 11 | 图纸导出 | 本地工程工具区 | `SwCmd_DrawingExport` |
| 12 | 打包备份 | 本地工程工具区 | `SwCmd_PackageBackup` |
| 13 | 插件设置 | 系统区 | `SwCmd_Settings` |
| 14 | 规则配置 | 系统区 | `SwCmd_RulesConfig` |
| 15 | 关于 | 系统区 | `SwCmd_About` |

如果 SolidWorks CommandManager 不方便做真实视觉分区，至少按这个顺序排列，并用按钮名称和图标体现分区。

## 行为要求

- `驾驶舱` 打开 AICockpit 总览。
- `AI助手` 打开 AICockpit 并展开右侧 AI 面板。
- `图纸审核` 打开 AICockpit 的图纸审核页面，不要走本地图纸审核。
- `快速选型`、`物料检索`、`设计计算` 打开 AICockpit 对应页面。
- 本地工具按钮调用 Agent Q 的 Action Router；如果 Q 尚未合入，允许临时调用旧逻辑或中文提示“功能开发中”。
- `插件设置` 保留打开配置能力。
- `规则配置` 打开 `rules.local.json` 或规则配置页面。
- `关于` 弹出版本、部署目录、日志路径。

## 守护约束

- 不要修改 COM GUID：`E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1`。
- 不要移除 `SolidWorks.Interop.swpublished.ISwAddin`。
- 不要移除 `ClassInterface(ClassInterfaceType.AutoDual)`。
- 不要破坏 `SetAddinCallbackInfo2(0, this, Cookie)`。
- WebView2 或 AICockpit 失败不能导致 `ConnectToSW` 失败。
- 不要重新引入对 `SolidWorksTools.dll` 的硬依赖。

## 验收

执行：

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build "E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.csproj" -c Release
```

必须确认：

- 0 错误，尽量 0 警告。
- `E:\0B 软件开发\MechPilot\Cockpit\deploy\SwAgentAddin.dll` 存在。
- `deploy\assets\icons\mechpilot-blueprint-icons-16-20.bmp` 存在。
- `deploy\assets\icons\mechpilot-blueprint-icons-16-32.bmp` 存在。
- `deploy\assets\icons\mechpilot-ribbon-icons-16-20.bmp` 存在。
- `deploy\assets\icons\mechpilot-ribbon-icons-16-32.bmp` 存在。
- 安装/部署后 `D:\SWAgentAddin\assets\icons` 存在新图标。
- SolidWorks 可加载插件。
- `D:\SWAgentAddin\addin-load.log` 显示 `ConnectToSW completed successfully. Mode=local`。
