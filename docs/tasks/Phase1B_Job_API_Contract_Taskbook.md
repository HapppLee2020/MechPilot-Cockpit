# Phase 1B: Job API 契约文档与调试样例

## 1. Phase 1A 验收通过证据

### 1.1 多选 checkbox
- 前端 `app.js` 实现了零部件多选 checkbox 列表
- 用户可勾选多个零部件后批量提交属性审核

### 1.2 属性审核走 /api/jobs 队列
- 前端调用 `agent.job.submit` action
- C# `ActionRouter` 路由到 `HermesClient.SubmitJob()`
- 请求发到 `POST /api/jobs`，不走 `/api/v1/agent/invoke`

### 1.3 Job 0accffd9 completed
```json
{
  "job_id": "0accffd9",
  "status": "completed",
  "completed_items": 15,
  "failed_items": 0,
  "progress_percent": 100
}
```

### 1.4 无 invoke
- 整个 Phase 1A 过程中未调用 `/api/v1/agent/invoke`
- 所有属性审核请求均通过 `/api/jobs` 队列

### 1.5 无本地弹窗
- 属性审核结果由前端 WebView2 渲染
- 未触发 WinForms `MessageBox` 或 `SafeMessage`

---

## 2. /api/jobs Request 示例

```json
POST /api/jobs
Content-Type: application/json

{
  "intent": "material_properties_review",
  "session_context": {
    "assembly_path": "D:\\CAD\\Projects\\bracket_assy.SLDASM",
    "assembly_unc_path": "\\\\192.168.31.24\\Share\\CAD\\bracket_assy.SLDASM",
    "view_mode": "flat",
    "auto_fix_enabled": true,
    "engineer_id": "davis",
    "total_selected": 15
  },
  "components": [
    {
      "name": "bracket_left-1",
      "filepath": "D:\\CAD\\Projects\\bracket_left.SLDPRT",
      "doc_type": "part",
      "selected": true,
      "properties": {
        "物料名称": "bracket_left",
        "图号": "BRK-L-001",
        "材料": "Q235",
        "重量": "1.2"
      }
    },
    {
      "name": "bracket_right-1",
      "filepath": "D:\\CAD\\Projects\\bracket_right.SLDPRT",
      "doc_type": "part",
      "selected": true,
      "properties": {
        "物料名称": "bracket_right",
        "图号": "BRK-R-001",
        "材料": "Q235",
        "重量": "1.1"
      }
    },
    {
      "name": "bearing_6205-2",
      "filepath": "D:\\CAD\\Standard\\bearing_6205.SLDPRT",
      "doc_type": "part",
      "selected": true,
      "properties": {
        "物料名称": "bearing_6205",
        "图号": "BRG-6205",
        "材料": "GCr15",
        "重量": "0.12"
      }
    }
  ]
}
```

---

## 3. /api/jobs Response 示例（提交成功）

```json
HTTP 202 Accepted

{
  "success": true,
  "data": {
    "accepted": true,
    "job_id": "job_0accffd9",
    "status": "queued",
    "queue_position": 3,
    "estimated_wait_seconds": 45
  }
}
```

---

## 4. /api/jobs/{job_id} Response 示例（轮询）

### 4.1 排队中
```json
HTTP 200 OK

{
  "job_id": "job_0accffd9",
  "status": "queued",
  "queue_position": 2,
  "estimated_wait_seconds": 30,
  "created_at": "2026-06-24T10:15:00Z"
}
```

### 4.2 执行中
```json
HTTP 200 OK

{
  "job_id": "job_0accffd9",
  "status": "running",
  "progress_percent": 60,
  "current_stage": "property_validation",
  "completed_items": 9,
  "failed_items": 0,
  "total_items": 15,
  "started_at": "2026-06-24T10:15:12Z"
}
```

### 4.3 完成
```json
HTTP 200 OK

{
  "job_id": "job_0accffd9",
  "status": "completed",
  "progress_percent": 100,
  "completed_items": 15,
  "failed_items": 0,
  "total_items": 15,
  "started_at": "2026-06-24T10:15:12Z",
  "completed_at": "2026-06-24T10:15:45Z",
  "results": [
    {
      "component": "bracket_left-1",
      "status": "passed",
      "checks": [
        {"field": "物料名称", "expected": "bracket_left", "actual": "bracket_left", "result": "match"},
        {"field": "材料", "expected": "Q235", "actual": "Q235", "result": "match"}
      ]
    }
  ]
}
```

### 4.4 失败
```json
HTTP 200 OK

{
  "job_id": "job_0accffd9",
  "status": "failed",
  "progress_percent": 40,
  "completed_items": 6,
  "failed_items": 2,
  "total_items": 15,
  "error": {
    "code": "EXECUTOR_TIMEOUT",
    "message": "MCP executor timed out after 120s"
  }
}
```

### 4.5 部分失败（partial_failed）
```json
HTTP 200 OK

{
  "job_id": "job_0accffd9",
  "status": "partial_failed",
  "progress_percent": 100,
  "completed_items": 12,
  "failed_items": 3,
  "total_items": 15,
  "started_at": "2026-06-24T10:15:12Z",
  "completed_at": "2026-06-24T10:16:02Z",
  "executor_result": {
    "adapter": "McpExecutor",
    "connected": false,
    "error_code": "ADAPTER_NOT_CONNECTED",
    "message": "MCP executor adapter is not connected in Phase 1B stub."
  },
  "results": [
    {
      "item_id": "item_001",
      "component": "bracket_left-1",
      "status": "completed",
      "success": true,
      "error_code": "",
      "message": "属性校验通过",
      "data": {"物料名称": "bracket_left", "材料": "Q235"}
    },
    {
      "item_id": "item_002",
      "component": "bracket_right-1",
      "status": "failed",
      "success": false,
      "error_code": "PROPERTY_MISMATCH",
      "message": "材料属性不匹配：期望 Q235，实际为空",
      "data": {"物料名称": "bracket_right", "材料": ""}
    }
  ]
}
```

---

## 4A. Job 状态枚举与语义

| status | 语义 |
|--------|------|
| `queued` | 任务已入队，未分配 |
| `assigned` | 任务已被 scheduler 分配给 executor |
| `running` | executor 正在处理 item |
| `completed` | 所有 item `success=true` |
| `partial_failed` | **后续增强**：至少一个 item `success=false`，且至少一个 `success=true`。当前 Phase 1B 未实现，有 failed item 时直接标 `failed` |
| `failed` | 至少一个 item `success=false`，或 job 级不可恢复错误 |
| `cancelled` | 用户或系统取消 |

**Phase 1B 当前行为：**
- MockExecutor `success=true` → job `completed`
- McpExecutor stub `success=false` → job `failed`
- JobStore: 有 `failed_items > 0` 时 job 标 `failed`

**后续增强（Phase 2+）：**
- `partial_failed` 状态：区分"部分成功"和"全部失败"
- 需要 JobStore 增加 item 级 `success` 字段聚合逻辑

**progress_percent 计算规则：**
```
progress_percent = (completed_items + failed_items) / total_items * 100
```

---

## 4B. Item Result 契约

每个 item 的结果结构：

```json
{
  "item_id": "item_001",
  "status": "completed | failed",
  "success": true,
  "error_code": "",
  "message": "",
  "data": {}
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `item_id` | string | item 唯一标识 |
| `status` | string | `completed` 或 `failed` |
| `success` | bool | true=成功，false=失败 |
| `error_code` | string | 失败时的错误码，成功时为空 |
| `message` | string | 人类可读描述 |
| `data` | object | 执行结果数据 |

**常见 error_code：**

| error_code | 说明 |
|------------|------|
| `PROPERTY_MISMATCH` | 属性值不匹配 |
| `PROPERTY_NOT_FOUND` | 属性不存在 |
| `FILE_NOT_FOUND` | 零件文件不存在 |
| `MCP_TIMEOUT` | MCP 工具调用超时 |
| `ADAPTER_NOT_CONNECTED` | Executor adapter 未连接 |
| `EXECUTOR_TIMEOUT` | Executor 整体超时 |

---

## 5. Phase 1B Executor Adapter 目标

### 5.1 MockExecutor（Phase 1A 已实现）
```python
class MockExecutor:
    """模拟执行：延迟 N 秒后标记为 completed"""
    async def execute(self, job_id, items):
        await asyncio.sleep(2)
        return {"completed": len(items), "failed": 0}
```

### 5.2 McpExecutor（Phase 1B 当前实现）

**当前支持的 intent：**

| intent | 行为 | 说明 |
|--------|------|------|
| `status` | 调用 `sw_get_status` | 查询 MCP 连接状态 |
| `ping` | 调用 `sw_get_status` | 同 status |
| `material_properties_review` | dry_run smoke | 不做真实属性修改，只验证 MCP 可达 |
| 其他 | 返回 `failed`，`error_code=UNSUPPORTED_INTENT` | 当前不支持 |

**dry_run 返回示例：**

```json
{
  "success": true,
  "status": "completed",
  "data": {
    "dry_run": true,
    "mcp_tool": "sw_get_status",
    "intent": "material_properties_review",
    "items_processed": 15,
    "note": "Phase 1B dry_run: MCP 可达，未执行真实属性修改"
  }
}
```

**当前不做（明确边界）：**
- ❌ 真实属性写入（`sw_set_custom_properties`）
- ❌ PDM checkout / checkin
- ❌ batch job backend（`sw_execute_batch_job`）
- ❌ 任何修改 SolidWorks 文档的操作

```python
class McpExecutor:
    """Phase 1B：dry_run / smoke，不做真实属性修改"""
    async def execute(self, job_id, items):
        intent = items[0].get("intent", "") if items else ""

        # 1. MCP 未连接 → 失败
        if not self.mcp_connected:
            return {
                "success": False,
                "status": "failed",
                "error_code": "ADAPTER_NOT_CONNECTED",
                "message": "MCP executor adapter is not connected in Phase 1B stub."
            }

        # 2. status / ping → sw_get_status
        if intent in ("status", "ping"):
            mcp_status = await self.mcp_client.call("sw_get_status", {})
            return {
                "success": True,
                "status": "completed",
                "data": {"dry_run": False, "mcp_tool": "sw_get_status", "mcp_status": mcp_status}
            }

        # 3. material_properties_review → dry_run smoke
        if intent == "material_properties_review":
            await self.mcp_client.call("sw_get_status", {})  # 验证 MCP 可达
            return {
                "success": True,
                "status": "completed",
                "data": {
                    "dry_run": True,
                    "mcp_tool": "sw_get_status",
                    "intent": "material_properties_review",
                    "items_processed": len(items),
                    "note": "Phase 1B dry_run: MCP 可达，未执行真实属性修改"
                }
            }

        # 4. 其他 intent → 不支持
        return {
            "success": False,
            "status": "failed",
            "error_code": "UNSUPPORTED_INTENT",
            "message": f"Phase 1B McpExecutor 不支持 intent: {intent}"
        }
```

---

### 5.3 Phase 1C 才考虑的能力

Phase 1C 在 Phase 1B dry_run 验证通过后启动，目标是真实属性落库：

| 能力 | 说明 |
|------|------|
| `sw_set_custom_properties` | item 级真实属性写入 |
| PDM status 锁定 | checkout → 修改 → checkin 流程 |
| STA 串行真实执行 | 通过 StaWorkerHost COM 串行执行 |
| `sw_execute_batch_job` | MCP batch 能力 |
| PDM workflow transition | 审批流状态变更 |

**Phase 1C 不改变 Phase 1B 的 API 契约**，只替换 executor adapter 实现。

```python
class Phase1C_McpExecutor:
    """Phase 1C：真实属性写入"""
    async def execute(self, job_id, items):
        results = []
        for item in items:
            try:
                # 真实写入
                await self.mcp_client.call("sw_set_custom_properties", {
                    "filepath": item["filepath"],
                    "properties": item["properties"],
                    "config": "默认"
                })
                results.append({
                    "item_id": item.get("item_id", ""),
                    "status": "completed",
                    "success": True,
                    "data": {"written": list(item.get("properties", {}).keys())}
                })
            except Exception as ex:
                results.append({
                    "item_id": item.get("item_id", ""),
                    "status": "failed",
                    "success": False,
                    "error_code": "MCP_WRITE_FAILED",
                    "message": str(ex),
                    "data": {}
                })
        return {"success": True, "results": results}
```

---

## 6. 非目标（Phase 1B 不做）

| 非目标 | 说明 |
|--------|------|
| 不改 MCP backend | `sw-remote-mcp-master` 的 MCP server 代码不变 |
| 不接 Redis | Job 队列用 SQLite，不引入 Redis/RabbitMQ |
| 不做真实批量属性落库 | Phase 1B 只读属性做验证，不写入 SolidWorks |
| 不实现 `/api/v1/agent/invoke` | 旧 invoke 接口不在 Phase 1B 范围内 |
| 不改 SolidWorks COM 注册 | RegAsm、COM GUID 不变 |
| 不改 WebView2 前端框架 | 只改数据交互，不改 UI 框架 |

---

## 6A. Cockpit UI 状态处理

前端必须识别并正确渲染以下 job 状态：

| job status | Cockpit UI 表现 |
|------------|----------------|
| `queued` | 显示排队中、queue_position、estimated_wait_seconds |
| `running` | 显示进度条、progress_percent、current_stage |
| `completed` | 显示绿色成功，展示 results |
| `failed` | 显示红色失败，展示 error_code + message |
| `partial_failed` | **后续增强**：显示黄色警告，区分成功/失败 item |

**progress_percent 计算：**
```
progress_percent = (completed_items + failed_items) / total_items * 100
```

---

## 6B. 禁止假阳性（Phase 1B 验收红线）

**绝对不能接受的情况：**

```
executor 返回 success=false
但 job status = completed
```

**验证方法：**
1. McpExecutor stub 返回 `ADAPTER_NOT_CONNECTED` 时，job 必须标为 `failed`
2. 任何 item `success=false` 时，job 不能标为 `completed`
3. 查询 `/api/jobs/{job_id}` 返回的 `failed_items` 必须 > 0

**测试用例：**
```bash
# 提交一个会触发 McpExecutor stub 的 job
curl -X POST http://127.0.0.1:8080/api/jobs -d '{"intent":"material_properties_review", ...}'

# 轮询直到终态
curl http://127.0.0.1:8080/api/jobs/job_xxx | jq '.status'
# 期望: "failed"（不是 "completed"）

# 验证 failed_items
curl http://127.0.0.1:8080/api/jobs/job_xxx | jq '.failed_items'
# 期望: > 0
```

---

## 7. 调试样例

### 7.1 提交 Job
```bash
curl -X POST http://127.0.0.1:8080/api/jobs \
  -H "Content-Type: application/json" \
  -d '{
    "intent": "material_properties_review",
    "session_context": {
      "assembly_path": "D:\\CAD\\test.SLDASM",
      "engineer_id": "davis",
      "total_selected": 2
    },
    "components": [
      {"name": "part_a-1", "filepath": "D:\\CAD\\part_a.SLDPRT", "doc_type": "part", "selected": true},
      {"name": "part_b-1", "filepath": "D:\\CAD\\part_b.SLDPRT", "doc_type": "part", "selected": true}
    ]
  }'
```

### 7.2 轮询状态
```bash
curl http://127.0.0.1:8080/api/jobs/job_0accffd9
```

### 7.3 验证 completed
```bash
curl http://127.0.0.1:8080/api/jobs/job_0accffd9 | jq '.status'
# 期望: "completed"
```
