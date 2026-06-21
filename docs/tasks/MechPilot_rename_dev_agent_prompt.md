# Dev Agent Prompt: Rename SolidWorks Add-in To MechPilot

你是接手本地 SolidWorks 插件工程的开发 Agent。请在当前工作区完成一次受控重命名：把插件对用户可见的品牌从 `SW Agent Platform` / `SW Agent` / `SwAgentAddin` 逐步收敛为 `MechPilot`。这不是简单搜索替换，必须保护现有可加载的 COM 插件基线。

## 项目背景

当前工程目录：

`F:\davis\Documents\WPS灵犀\20260617-22-20-29-842`

当前权威源码目录：

`F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin`

当前推荐部署基线：

`D:\SWAgentAddin\SwAgentAddin_tabicons_v2.dll`

历史上这个插件曾经出现过“SolidWorks 插件列表可见但勾选不上”的问题。最后稳定的修复点是：

- 使用官方 `SolidWorks.Interop.swpublished.ISwAddin`，不要改回本地手写 `ISwAddin`。
- 保留 COM GUID：`E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1`，除非用户明确要求做一个全新的插件身份。
- `SetAddinCallbackInfo2(0, this, Cookie)` 必须在 `ConnectToSW` 早期成功。
- `CodeBase` 是 SolidWorks 实际加载 DLL 的关键真相源，必须每次安装后验证。
- `D:\SWAgentAddin\addin-load.log` 是最重要的运行时诊断日志。

## 目标

完成插件重命名，使 SolidWorks 中用户能看到的插件名称、选项卡名称、任务面板标题、安装脚本输出、README 和交接文档都使用 `MechPilot`。

优先级：

1. 用户可见品牌统一为 `MechPilot`。
2. 保持插件仍可注册、可启用、可显示 CommandManager 选项卡和任务面板。
3. 生成新的可部署基线和交接说明，避免继续依赖历史文件名造成混乱。

## 重点文件位置

请从这些文件开始：

- `output\SW-Agent-Addin\SwAgentAddin.cs`
  - 常量：`AddinTitle`、`CommandTabName`
  - Command group 名称和提示文案
  - TaskPane 标题：`CreateTaskpaneView3(..., "SW Agent Platform")`
  - 默认 HTML 内容中的 `SW Agent Platform` / `SW Agent`
- `output\SW-Agent-Addin\SwAgentAddin.csproj`
  - `RootNamespace`
  - `AssemblyName`
  - `Title`
  - `Description`
  - 注意：是否重命名程序集文件要谨慎处理，因为会影响 RegAsm、TLB、部署脚本和 CodeBase。
- `output\SW-Agent-Addin\taskpane.html`
  - 页面标题、头部标题、提示文案。
- `output\SW-Agent-Addin\install_tabicons_v2.bat`
  - 当前推荐安装脚本。
  - 注册表 `Title` 应改为 `MechPilot`。
  - `Description` 可改为 `MechPilot SolidWorks assistant platform`。
- `output\SW-Agent-Addin\install.bat`
  - 旧通用安装脚本仍可能被误用，至少要把文案和注册表标题改为 `MechPilot`，或明确标注非推荐。
- `output\SW-Agent-Addin\uninstall.bat`
  - 文案改为 `MechPilot`。
- `output\SW-Agent-Addin\README.md`
  - 全文品牌、安装步骤、故障排查。
- `output\SW-Agent-Addin\HANDOFF_2026-06-18.md`
  - 增加本次重命名记录，说明哪些历史名称仍只是兼容/旧文件名。
- `output\SW_Agent_Platform_架构规划文档.md`
  - 如仍作为当前文档使用，重命名或补充为 MechPilot 架构文档。

部署包目录也有副本，但不要把它当成唯一源头：

- `output\packages\SWAgentAddin-deploy-staging-20260618-final`
- `output\packages\SWAgentAddin-deploy-with-docs-20260618.zip`

如果要重新打包，请从源码目录和新的部署目录重新生成包，不要直接手改旧 zip。

## 命名策略

推荐分两层处理：

第一层，必须改：

- SolidWorks 插件列表显示名：`MechPilot`
- SolidWorks CommandManager 选项卡名：`MechPilot`
- TaskPane 标题：`MechPilot`
- 安装脚本屏幕输出和注册表 `Title`：`MechPilot`
- README、交接文档、架构文档中的当前项目名：`MechPilot`

第二层，谨慎改：

- C# namespace、class、assembly、DLL 文件名。
- 如果你决定把程序集从 `SwAgentAddin.dll` 改成 `MechPilot.dll`，必须同步修改：
  - `.csproj` 的 `AssemblyName`
  - 所有安装脚本中的 DLL/TLB 路径
  - `RegAsm` 调用
  - `CodeBase` 验证命令中的期望值
  - 部署目录里的 DLL/PDB/TLB
  - README 和 HANDOFF

如果时间有限，优先完成用户可见品牌重命名，保留内部程序集名 `SwAgentAddin` 作为兼容名称，并在文档中明确：“内部历史程序集名暂保留，用户可见品牌已统一为 MechPilot。”

## 禁止事项

- 不要修改 COM GUID：`E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1`。
- 不要改回本地手写 `ISwAddin`。
- 不要重新引入 `SolidWorksTools.dll` 作为加载必需依赖。
- 不要只改 README，必须改源码、任务面板和安装注册脚本。
- 不要把旧的 `install.bat` 当最终验证脚本；优先维护并使用 `install_tabicons_v2.bat` 或新建一个清晰命名的 MechPilot 安装脚本。
- 不要在没有验证 `CodeBase` 的情况下宣称安装完成。
- 不要删除历史 DLL，除非用户明确要求清理；可以标注文档说明哪些是历史旁路版本。

## 建议实施步骤

1. 先全局搜索旧品牌：
   - `SW Agent`
   - `SW Agent Platform`
   - `SwAgentAddin`
   - `Agent assistant platform`
   - `Agent Server`
   - `Hermes Agent`
2. 判断每个命中是“用户可见品牌”还是“内部历史兼容名”。
3. 修改源码和静态 HTML，使 UI 显示为 `MechPilot`。
4. 修改安装脚本，使注册表 `Title` 为 `MechPilot`，`Description` 为清晰的 MechPilot 描述。
5. 修改 README、交接文档、架构文档。
6. 构建 Release。
7. 准备新的部署目录，例如：
   - `output\MechPilot-Addin`
   - 或 `output\packages\MechPilot-deploy-staging-YYYYMMDD`
8. 从新部署目录运行推荐安装脚本。
9. 验证注册表和真实加载路径。
10. 打开 SolidWorks 做实机冒烟。

## 必须验证

构建验证：

```powershell
dotnet build output\SW-Agent-Addin\SwAgentAddin.csproj -c Release
```

注册路径验证：

```cmd
reg query "HKCR\CLSID\{E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1}\InprocServer32" /v CodeBase /reg:64
```

SolidWorks 插件发现键验证：

```cmd
reg query "HKLM\Software\SolidWorks\Addins\{E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1}" /s
reg query "HKCU\Software\SolidWorks\Addins\{E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1}" /s
```

日志验证：

```cmd
type D:\SWAgentAddin\addin-load.log
```

实机冒烟验收：

- 打开 SolidWorks。
- 工具 -> 插件中能看到 `MechPilot`。
- 勾选后插件能启用，不出现“列表有显示但勾选不上”。
- 打开零件、装配体、工程图，CommandManager 中能看到 `MechPilot` 选项卡。
- 任务面板标题显示 `MechPilot`。
- `Property Fill`、`Property Check`、`Drawing Review`、`Task Panel`、`Settings` 按钮仍可见。
- `D:\SWAgentAddin\addin-load.log` 中能看到 `ConnectToSW completed successfully.` 和 Command tab 创建成功相关日志。

## 最终交付

完成后请交付：

- 修改文件清单。
- 是否保留内部 `SwAgentAddin` 程序集名的说明。
- 新的推荐安装脚本路径。
- 新的推荐部署 DLL 路径。
- `CodeBase` 查询结果。
- SolidWorks 实机冒烟结果。
- 如果重新打包，给出源码包和部署包路径。

最终回答必须明确一句：

“MechPilot 重命名是否已经达到可交付状态：是/否。”

如果是否定，必须列出阻塞项和下一步最短路径。
