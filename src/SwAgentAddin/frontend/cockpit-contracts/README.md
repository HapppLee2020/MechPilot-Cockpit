# MechPilot Agent驾驶舱 JSON 合同

## 定位

Agent驾驶舱（Cockpit）是 MechPilot 的可选前端层 —— 一个嵌入 SolidWorks Task Pane 的 WebView2/WinForms 面板，实时展示当前文档状态并接收用户操作。

**合同优先原则**：前端与 C# 宿主之间 **只通过 JSON 信封通信**，不直接调用 COM API。所有数据流经过合同定义的 schema，确保：

- 前端可独立开发/测试（mock JSON 即可）
- C# 宿主可独立演进（只要 JSON 格式兼容）
- 未来远程模式/MCP Worker 可复用同一合同

## 合同文件

| 文件 | 方向 | 说明 |
|------|------|------|
| `context.schema.json` | C# → 前端 | 上下文快照：文档、树、属性表、能力 |
| `command.schema.json` | 前端 → C# | 命令信封：属性填写/检查/审核等 |
| `result.schema.json` | C# → 前端 | 执行结果：成功/失败/变更列表 |

## 版本管理

- 版本号在 `schema_version` 字段中，格式 `mechpilot.cockpit.<type>.vN`
- config.json 中的 `cockpit_schema_version` 记录当前宿主使用的 context 版本
- 向后兼容：旧前端遇到未知字段应忽略，旧宿主遇到未知命令应返回错误码

## 配置

config.json 中的驾驶舱相关字段：

| 字段 | 默认值 | 说明 |
|------|--------|------|
| `cockpit_enabled` | `true` | 是否启用驾驶舱按钮 |
| `cockpit_url_mode` | `local` | `local`=嵌入HTML, `dev`=开发服务器 |
| `cockpit_entry` | `property-workbench/index.html` | 本地 HTML 入口路径 |
| `cockpit_dev_url` | `http://127.0.0.1:5173` | Vite 开发服务器 URL |
| `cockpit_prefer_webview2` | `true` | 优先使用 WebView2 |
| `cockpit_fallback_to_winforms` | `true` | WebView2 不可用时回退 WinForms |
| `cockpit_schema_version` | `mechpilot.cockpit.context.v1` | 合同版本标识 |

## C# 数据类

合同对应的 POCO 类定义在 `SwAgentAddin.cs` 中（第一版单文件）：

- `CockpitContext` — 上下文快照根对象
- `CockpitClientInfo` — 宿主环境信息
- `CockpitDocumentInfo` — 活动文档元数据
- `CockpitSelectionInfo` — 选中实体
- `CockpitTreeNode` — 装配体树节点
- `CockpitPropertyTable` — 属性表容器
- `CockpitPropertyRow` — 单条属性
- `CockpitCommandEnvelope` — 命令信封
- `CockpitResultEnvelope` — 结果信封

所有类为纯序列化载体，**不包含 COM 对象引用**。
