# Agent T Prompt：蓝图批次集成 QA、部署、注册与实机冒烟

任务书地址：

`E:\0B 软件开发\MechPilot\Cockpit\docs\tasks\MechPilot_Agent_T_IntegrationQA_Task.md`

## 目标

集成 Agent P/Q/R/S 的成果，确认 MechPilot 新蓝图批次达到：

- SolidWorks 可注册。
- 插件可加载。
- 工具栏可显示。
- AICockpit 页面可打开。
- 本地 P0 功能尽可能可用。
- Hermes 通讯不阻断本地功能。

## 重要路径

工程根目录：

`E:\0B 软件开发\MechPilot\Cockpit`

源码：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin`

工程 deploy：

`E:\0B 软件开发\MechPilot\Cockpit\deploy`

真实运行部署：

`D:\SWAgentAddin`

日志：

`D:\SWAgentAddin\addin-load.log`

## 构建验证

执行：

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build "E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.csproj" -c Release
```

要求：

- 0 错误。
- 警告必须说明来源。

## 部署验证

确认 `D:\SWAgentAddin` 包含：

- `SwAgentAddin.dll`
- `config.json`
- `rules.local.json`
- `assets\icons\mechpilot-blueprint-icons-16-20.bmp`
- `assets\icons\mechpilot-blueprint-icons-16-32.bmp`
- `frontend\property-workbench\index.html`
- `frontend\property-workbench\app.js`
- `frontend\property-workbench\styles.css`
- `frontend\cockpit-contracts\context.schema.json`
- WebView2 相关 DLL

注意：新版结构优先使用：

`D:\SWAgentAddin\frontend\property-workbench`

不要只检查旧路径：

`D:\SWAgentAddin\property-workbench`

## 注册验证

必须确认：

- COM GUID 未变化：`E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1`
- CodeBase 指向：`D:\SWAgentAddin\SwAgentAddin.dll`
- SolidWorks 工具 -> 插件 能看到 MechPilot
- 勾选后不报错
- 日志出现：`ConnectToSW completed successfully. Mode=local`

## UI 验收

确认工具栏：

- AI 工具区按钮存在。
- 本地工程工具区按钮存在。
- 系统区按钮存在。
- 图标为新版蓝图彩色图标。
- 中文名称尽量四个字。

确认 AICockpit：

- `驾驶舱` 能打开。
- `AI助手` 能打开并展开 AI 面板。
- 页面显示当前 SolidWorks 文件。
- 页面不是空白页。
- 页面不是 mock-only 状态。
- Sidebar 可切换。

## 本地功能冒烟

至少验证：

- 属性填写：原能力不回归。
- 读取属性：可用或旧兜底可用。
- 属性检查：可执行或返回清晰占位。
- BOM导出：如实现，生成 CSV/XLSX。
- 批量转换：如实现，至少一个格式成功。
- 图纸导出：如实现，工程图可导出 PDF。
- 打包备份：如实现，生成包目录。

未实现项不能崩溃，必须给中文提示。

## Hermes 冒烟

Hermes 不在线时：

- AICockpit 不崩溃。
- AI 页面给中文提示。
- 本地功能仍可用。

Hermes 在线时：

- `ai.assistant.chat` 可提交。
- `agent.task.submit` 可返回 task_id 或兼容结果。
- `agent.task.poll` 可查询状态。

## 最终交付格式

请输出：

1. 构建结果。
2. 部署结果。
3. SW 注册/加载结果。
4. 工具栏按钮清单。
5. AICockpit 页面验收。
6. 本地 P0 功能完成表。
7. Hermes 通讯结果。
8. 修改文件清单。
9. 剩余风险。
10. 需要用户实机确认的事项。

不要把“构建通过”说成“实机通过”。没有真实 SolidWorks 验证时必须标注待确认。

