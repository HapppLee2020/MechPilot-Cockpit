# Agent S Prompt：Hermes Client、远端 Agent 通讯与上下文裁剪

任务书地址：

`E:\0B 软件开发\MechPilot\Cockpit\docs\tasks\MechPilot_Agent_S_Hermes_Client_Task.md`

## 背景

远端 Agent 当前以 Hermes 部署。AICockpit 未来必须和 Hermes 通讯，但前端 WebView2 不应该直接请求 Hermes。正确链路是：

```text
AICockpit WebView2
→ C# Add-in Broker
→ Hermes Client
→ Hermes Server
→ Agent / MCP / Worker
```

本任务负责 C# 侧 Hermes Client、远端任务提交、状态轮询、上下文裁剪和 AICockpit 命令入口。

## 重要路径

工程：

`E:\0B 软件开发\MechPilot\Cockpit`

主代码：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.cs`

配置：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\config\config.json`

前端：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\app.js`

运行配置：

`D:\SWAgentAddin\config.json`

日志：

`D:\SWAgentAddin\addin-load.log`

## 配置扩展

保持旧字段兼容：

- `server_url`
- `remote_task_endpoint`
- `remote_status_endpoint_template`
- `remote_result_endpoint_template`

新增建议：

```json
{
  "agent_server": {
    "provider": "hermes",
    "base_url": "http://127.0.0.1:8080",
    "auth_mode": "none",
    "api_key": "",
    "task_endpoint": "/api/v1/task",
    "status_endpoint_template": "/api/v1/task/{task_id}",
    "result_endpoint_template": "/api/v1/task/{task_id}/result",
    "stream_endpoint_template": "/api/v1/task/{task_id}/stream",
    "invoke_endpoint": "/api/v1/agent/invoke",
    "timeout_seconds": 120,
    "poll_interval_seconds": 3,
    "context_mode_default": "summary"
  }
}
```

如果 `agent_server` 不存在，使用旧字段回退。

同时新增 RAG 配置。物料检索由 HighSight 完成向量检索，MechPilot 只负责配置、请求转发和结果展示。

```json
{
  "rag": {
    "enabled": true,
    "provider": "highsight",
    "sqlite_db_path": "D:\\\\SWAgentAddin\\\\rag\\\\materials.sqlite.db",
    "collection": "materials",
    "top_k": 8,
    "score_threshold": 0.35,
    "search_endpoint": "/api/v1/rag/search",
    "index_endpoint": "/api/v1/rag/index",
    "metadata_fields": ["物料名称", "规格型号", "材料", "供应商", "图号"]
  }
}
```

## 请求模式

### 短动作

用于 AI 助手简单问答、ping、规则解释：

```text
POST /api/v1/agent/invoke
```

如果 Hermes 当前没有该接口，返回本地占位结果，不影响页面。

### 长任务

用于 AI 图纸审核、物料检索、设计计算、远程 Worker：

```text
POST /api/v1/task
GET  /api/v1/task/{task_id}
GET  /api/v1/task/{task_id}/result
```

### 流式进度

如果 Hermes 支持：

```text
GET /api/v1/task/{task_id}/stream
```

本阶段 SSE 可选，不强制。必须先完成提交 + 轮询。

## 上下文裁剪

不要每次把完整 `CockpitContext` 发给 Hermes。实现三档：

```text
summary  当前文件、选择集、统计、关键属性
selected 当前选中对象及局部上下文
full     完整上下文，仅手动指定或必要时使用
```

默认 `summary`。

## 必须支持的 action

```text
ai.assistant.chat
ai.drawing.review
ai.selection.recommend
ai.material.search
ai.design.calculate
agent.task.submit
agent.task.poll
```

其中：

- `图纸审核` 必须走 `ai.drawing.review`。
- `图纸导出` 才走本地工具。
- `物料检索` 必须优先走 HighSight RAG 向量检索接口，原始数据可以是 `sqlite_db_path` 指向的 SQLite.db。

## HighSight RAG 物料检索

请求建议：

```text
POST {agent_server.base_url}{rag.search_endpoint}
```

请求体：

```json
{
  "provider": "highsight",
  "db_path": "D:\\\\SWAgentAddin\\\\rag\\\\materials.sqlite.db",
  "collection": "materials",
  "query": "不锈钢直线导轨",
  "top_k": 8,
  "score_threshold": 0.35,
  "metadata_filter": {
    "材料": "SUS304"
  },
  "context": {
    "active_document": {},
    "selection": {}
  }
}
```

返回结果至少兼容：

```json
{
  "ok": true,
  "items": [
    {
      "name": "物料名称",
      "spec": "规格型号",
      "material": "材料",
      "supplier": "供应商",
      "drawing_no": "图号",
      "score": 0.82,
      "snippet": "命中的文本片段"
    }
  ]
}
```

HighSight 不在线或 SQLite 路径不存在时：

- 返回中文错误。
- 前端显示“RAG 服务未连接 / 数据库不可用”。
- 不影响其他 AI action 和本地功能。

## 错误体验

Hermes 不在线时：

- AICockpit 不崩溃。
- 返回中文错误或占位回答。
- 本地工具不受影响。

日志必须记录：

- Hermes base_url
- action
- request_id/task_id
- status
- 耗时
- 错误摘要

不要把 API key 写进日志。

## 验收

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build "E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.csproj" -c Release
```

必须：

- 0 错误。
- Hermes 不在线时页面不崩溃。
- Hermes 在线时至少可提交一个 mock/真实任务。
- 能轮询任务状态并返回给 AICockpit。
- 日志中有 Hermes 请求记录。
