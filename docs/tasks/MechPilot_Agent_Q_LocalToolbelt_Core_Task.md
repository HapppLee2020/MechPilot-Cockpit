# Agent Q Prompt：统一命令协议与 LocalToolbelt 本地 P0 工具

任务书地址：

`E:\0B 软件开发\MechPilot\Cockpit\docs\tasks\MechPilot_Agent_Q_LocalToolbelt_Core_Task.md`

## 背景

MechPilot 新架构是双核心：

- `AIPilot / AICockpit`：智能系统。
- `LocalToolbelt`：本地工程工具底座。

本任务负责本地能力底座和 P0 本地功能。注意：本地功能不是 AICockpit 的附属，它必须能从 SolidWorks 工具栏直接执行，也能被 AICockpit/AI 通过统一命令协议调用。

## 重要路径

工程根目录：

`E:\0B 软件开发\MechPilot\Cockpit`

源码目录：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin`

主代码：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.cs`

配置：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\config\config.json`

规则：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\config\rules.local.json`

运行部署：

`D:\SWAgentAddin`

本地输出目录建议：

`D:\SWAgentAddin\outputs`

## 任务 1：统一命令协议

实现或补齐：

- `MechPilotCommand`
- `MechPilotCommandTarget`
- `MechPilotResult`
- `MechPilotWarning`
- `MechPilotArtifact`
- `ActionRouter`
- `LocalToolbeltExecutor`

命令格式建议：

```json
{
  "schema_version": "mechpilot.command.v1",
  "command_id": "cmd-001",
  "source": "toolbar",
  "feature": "properties",
  "action": "read",
  "executor": "local",
  "target": {
    "scope": "current_selection"
  },
  "payload": {}
}
```

结果格式建议：

```json
{
  "schema_version": "mechpilot.result.v1",
  "command_id": "cmd-001",
  "ok": true,
  "message": "执行完成",
  "data": {},
  "warnings": [],
  "artifacts": []
}
```

必须兼容旧命令：

- `local.read_properties`
- `refresh_context`
- 原有属性填写按钮逻辑

## 任务 2：LocalToolbelt P0 本地功能

尽可能实现以下 P0 功能。未完成项不能崩溃，必须返回中文占位提示。

### 2.1 文件名拆分写属性

Action：

`local.filename.parse_to_properties`

要求：

- 从当前文件名解析属性。
- 支持正则或模板规则。
- 能写入当前零件、装配体或选中组件。
- 执行前显示预览和确认。

配置建议：

```json
{
  "filename_parse_rules": [
    {
      "name": "默认规则",
      "pattern": "^(?<project>[^-]+)-(?<drawing_no>[^-]+)-(?<part_name>[^-]+)-(?<version>[^.]+)",
      "map": {
        "project": "项目号",
        "drawing_no": "图号",
        "part_name": "物料名称",
        "version": "版本"
      }
    }
  ]
}
```

### 2.2 BOM 导出

Action：

`local.bom.export`

要求：

- 当前装配体导出 BOM。
- 至少支持：
  - 顶级汇总
  - 纯零件汇总
- 输出 CSV；如已有 Excel 能力可输出 XLSX。
- 包含数量、零部件名称、文件路径、关键属性。

输出示例：

`D:\SWAgentAddin\outputs\bom\接料固定机构_20260622.csv`

### 2.3 批量转换

Action：

`local.file.convert`

优先实现：

- 工程图 -> PDF
- 工程图 -> DWG/DXF，如 SW API 可用
- 零件/装配体 -> STEP，如 SW API 可用

要求：

- 不支持格式返回中文提示。
- 不默默覆盖文件。
- 输出路径可配置。

### 2.4 图纸导出

Action：

`local.drawing.export`

注意：图纸导出是本地工具，图纸审核属于 AIPilot。

要求：

- 当前工程图导出 PDF。
- 当前为零件/装配体时，可尝试寻找同名工程图；找不到则提示。

### 2.5 打包备份

Action：

`local.package.backup`

要求：

- 当前装配体或当前文件打包到时间戳目录。
- 包含当前文件、引用零部件、可找到的同名工程图、配置文件和规则文件。
- 优先实现复制备份，不要求替换引用。

## 守护约束

- 原有 `属性填写` 必须不回归。
- 写入/覆盖类动作必须确认。
- 单个文件失败不能中断整批任务。
- 所有结果写日志到 `D:\SWAgentAddin\addin-load.log`。

## 验收

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build "E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.csproj" -c Release
```

必须：

- 0 错误。
- 至少 3 个 P0 功能达到可运行或可演示。
- 所有输出进入 `D:\SWAgentAddin\outputs`。
- 未完成功能有中文提示，不崩溃。
- 原有本地属性填写可用。

