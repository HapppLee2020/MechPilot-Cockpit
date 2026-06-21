# Agent I Task: MechPilot Agent驾驶舱 JSON 合同、配置与架构落地

你负责为 MechPilot Agent驾驶舱定义稳定合同、配置字段和工程目录结构。不要实现 WebView2 宿主，不要改 SolidWorks COM 加载链路。

## 目标

建立未来可适配的 cockpit 数据协议，让本地模式、远程模式、MCP/Worker、未来 Agent 报告都通过统一 JSON 结构流动。

## 必须创建/修改

创建：

- `output\SW-Agent-Addin\cockpit-contracts\context.schema.json`
- `output\SW-Agent-Addin\cockpit-contracts\command.schema.json`
- `output\SW-Agent-Addin\cockpit-contracts\result.schema.json`
- `output\SW-Agent-Addin\cockpit-contracts\README.md`

修改：

- `output\SW-Agent-Addin\config.json`
- `output\SW-Agent-Addin\SwAgentAddin.cs`
- `output\SW-Agent-Addin\SwAgentAddin.csproj`
- `output\SW-Agent-Addin\README.md`

## 新配置字段

加入 `config.json` 并写入 `AddinConfig`：

```json
{
  "cockpit_enabled": true,
  "cockpit_url_mode": "local",
  "cockpit_entry": "property-workbench/index.html",
  "cockpit_dev_url": "http://127.0.0.1:5173",
  "cockpit_prefer_webview2": true,
  "cockpit_fallback_to_winforms": true,
  "cockpit_schema_version": "mechpilot.cockpit.context.v1"
}
```

语义：

- `cockpit_enabled`：是否启用 Agent驾驶舱按钮。
- `cockpit_url_mode`：`local` 或 `dev`。
- `cockpit_entry`：本地 cockpit HTML 入口。
- `cockpit_dev_url`：前端开发调试 URL，生产不依赖。
- `cockpit_prefer_webview2`：优先 WebView2。
- `cockpit_fallback_to_winforms`：WebView2 不可用时允许回退。
- `cockpit_schema_version`：上下文 JSON 版本。

旧配置缺字段时必须安全默认。

## JSON 合同

至少定义三个 schema/示例：

1. `context.schema.json`
2. `command.schema.json`
3. `result.schema.json`

不要求完整 JSON Schema 严格校验，但文件必须是有效 JSON，并包含 `description`、`required`、`example`。

核心字段参考：

```json
{
  "schema_version": "mechpilot.cockpit.context.v1",
  "active_document": {},
  "assembly_tree": {},
  "property_table": {},
  "capabilities": []
}
```

## C# 数据类

在 `SwAgentAddin.cs` 中添加轻量 POCO，暂可单文件：

- `CockpitContext`
- `CockpitClientInfo`
- `CockpitDocumentInfo`
- `CockpitSelectionInfo`
- `CockpitTreeNode`
- `CockpitPropertyTable`
- `CockpitPropertyRow`
- `CockpitCommandEnvelope`
- `CockpitResultEnvelope`

这些类第一版只做序列化载体，不要塞 COM 对象。

## csproj

复制 contract 文件到 deploy：

```xml
<None Include="cockpit-contracts\**\*.*" CopyToOutputDirectory="PreserveNewest" />
```

如当前 `.csproj` 还没复制 `config.json`，必须补上：

```xml
<None Include="config.json" CopyToOutputDirectory="PreserveNewest" />
```

## 验收

- `dotnet build` 0 警告 0 错误。
- `deploy\config.json` 包含 cockpit 字段。
- `deploy\cockpit-contracts\` 中存在三个合同文件。
- README 解释 Agent驾驶舱定位和合同优先原则。

## 交付总结

必须说明：

- 新增配置字段。
- 合同文件路径。
- C# 数据类清单。
- 是否补齐 config.json 输出复制。
