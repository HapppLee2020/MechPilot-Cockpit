# Agent B Task: MechPilot Config, Rule Provider, And Remote Mode Contract

你负责 MechPilot 的 **配置化、规则抽象、本地/远程模式边界**。目标是让插件不是一次性 Demo，而是从第一版就有可演进的配置和执行器抽象。

## 你的边界

你主要改：

- `output\SW-Agent-Addin\config.json`
- `output\SW-Agent-Addin\SwAgentAddin.cs`
- `output\SW-Agent-Addin\README.md`
- `output\SW-Agent-Addin\HANDOFF_2026-06-18.md`

你需要新建：

- `output\SW-Agent-Addin\rules.local.json`

可选新建：

- `output\mock-agent-server\server.py`
- `output\mock-agent-server\README.md`

尽量不要做：

- SolidWorks SelectionManager 目标解析
- 自定义属性实际写入
- COM 注册和安装路径大改

## 配置目标

把 `config.json` 扩展为支持两种模式：

```json
{
  "execution_mode": "local",
  "server_url": "http://127.0.0.1:8080",
  "engineer_id": "davis",
  "poll_interval_seconds": 3,
  "request_timeout_seconds": 120,
  "enable_feishu_notify": false,
  "log_level": 2,
  "auto_show_taskpane": true,
  "confirm_before_write": true,
  "multi_select_behavior": "show_form",
  "local_rules_file": "rules.local.json",
  "remote_task_endpoint": "/api/v1/task",
  "remote_status_endpoint_template": "/api/v1/task/{task_id}",
  "remote_result_endpoint_template": "/api/v1/task/{task_id}/result"
}
```

要求：

- 缺字段时有安全默认值。
- 旧配置仍能加载。
- `execution_mode` 只接受 `local` 或 `remote`，其他值回退为 `remote` 或显示明确错误。
- 默认建议先用 `local`，方便 Demo。

## 本地规则文件

新建 `rules.local.json`：

```json
{
  "version": "demo-2026-06-21",
  "property_sets": {
    "part": {
      "物料名称": "{file_name_no_ext}",
      "图号": "{file_name_no_ext}",
      "文档类型": "{doc_type}",
      "处理状态": "MechPilot Local Demo",
      "处理人": "{engineer_id}",
      "处理日期": "{date}"
    },
    "assembly": {
      "装配体名称": "{file_name_no_ext}",
      "图号": "{file_name_no_ext}",
      "文档类型": "{doc_type}",
      "处理状态": "MechPilot Local Demo",
      "处理人": "{engineer_id}",
      "处理日期": "{date}"
    },
    "drawing": {
      "图纸名称": "{file_name_no_ext}",
      "图号": "{file_name_no_ext}",
      "文档类型": "{doc_type}",
      "处理状态": "MechPilot Local Demo",
      "处理人": "{engineer_id}",
      "处理日期": "{date}"
    }
  }
}
```

后续用户会替换成真实公司规则；当前只要通用、可演示、可配置。

## 代码目标

在 `SwAgentAddin.cs` 中扩展：

- `AddinConfig`
  - `ExecutionMode`
  - `ConfirmBeforeWrite`
  - `MultiSelectBehavior`
  - `LocalRulesFile`
  - `RemoteTaskEndpoint`
  - `RemoteStatusEndpointTemplate`
  - `RemoteResultEndpointTemplate`
- `LocalPropertyRules`
  - `Version`
  - `PropertySets`
- `RuleProvider`
  - `LoadLocalRules(AddinConfig config)`
  - 找不到规则文件时自动创建 demo 规则

如果代码已经很长，仍优先保持单文件，避免本轮引入项目文件复杂变更。后续再拆文件。

## 远程模式契约

保留现有 POST `/api/v1/task` 行为，但请求体应向未来 Agent Server/MCP 靠拢：

```json
{
  "task_type": "property_fill",
  "execution_mode": "remote",
  "engineer_id": "davis",
  "client": {
    "name": "MechPilot",
    "version": "1.0.0",
    "machine": "HOSTNAME"
  },
  "active_document": {
    "filename": "bracket_left.SLDPRT",
    "filepath": "D:\\vault\\bracket_left.SLDPRT",
    "doc_type": "part",
    "title": "bracket_left.SLDPRT"
  },
  "target_scope": {
    "mode": "active_document",
    "selected_count": 0,
    "targets": []
  },
  "local_rules_version": "demo-2026-06-21",
  "priority": 3
}
```

Agent A 会提供更准确的 selected targets 后，你只需要保证字段命名和序列化方式稳定。

## 可选 Mock Server

如果时间允许，建一个最小 Python mock：

- `POST /api/v1/task` 返回 `{task_id,status,queue_position}`
- `GET /api/v1/task/{id}` 返回执行中或完成
- `GET /api/v1/task/{id}/result` 返回 Demo 报告

这不是生产 Server，只为 remote mode 演示和调试。

## 文档要求

更新 README，说明：

- `local` 模式：插件直接写 SolidWorks 属性。
- `remote` 模式：插件提交任务给 Agent Server，后续由 MCP/Worker 执行。
- Demo 规则在 `rules.local.json`。
- 用户真实规则后续替换该文件。

更新 HANDOFF，说明：

- 当前哪些是本地 Demo 能力。
- 远程模式目前做到哪一步。
- 后续接 Agent Server/MCP 时不需要重写插件，只替换 executor。

## 验收

必须完成：

```powershell
dotnet build output\SW-Agent-Addin\SwAgentAddin.csproj -c Release
```

配置验证：

- 删除 `config.json` 后重新加载插件，会生成包含新字段的默认配置。
- 删除 `rules.local.json` 后重新加载或点击本地任务，会生成 demo 规则。
- `execution_mode = "local"` 时不会强制要求 server 可达。
- `execution_mode = "remote"` 时仍会向 `server_url + remote_task_endpoint` 提交任务。

交付说明必须写清：

- 新增配置字段。
- 本地规则 JSON 格式。
- 远程请求 JSON 格式。
- 是否提供 mock server。
