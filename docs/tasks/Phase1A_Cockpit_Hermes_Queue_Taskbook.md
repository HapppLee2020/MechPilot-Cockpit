# Phase 1A: Cockpit ↔ Hermes Queue Integration Taskbook

## Scope

This phase builds the queue-oriented interaction loop between MechPilot Cockpit and Hermes.

It does not modify SW_Remote_MCP or the high-performance SolidWorks execution daemon.

Target loop:

```text
Cockpit WebView2 UI
-> C# ActionRouter / HermesClient
-> Hermes /api/jobs
-> queued job receipt
-> Cockpit polls /api/jobs/{job_id}
-> UI shows queued/running/completed state
```

## Non-Goals

- Do not add or change MCP capabilities.
- Do not change `StaWorkerHost`, `PdmStaWorkerHost`, or COM execution boundaries.
- Do not implement Redis/RabbitMQ.
- Do not execute real remote batch SolidWorks modifications in this phase.
- Do not block the SolidWorks UI thread while waiting for Hermes.

## Agent Split

### Agent B1: C# Cockpit Bridge

Owned files:

- `src/SwAgentAddin/HermesClient.cs`
- `src/SwAgentAddin/ActionRouter.cs`
- `src/SwAgentAddin/MechPilotProtocol.cs`
- `src/SwAgentAddin/config/config.json`
- `src/SwAgentAddin/SwAgentAddin.cs` only for config fields if required

Responsibilities:

- Add queue job submit and poll support.
- Add config fields:
  - `job_submit_endpoint`, default `/api/jobs`
  - `job_status_endpoint_template`, default `/api/jobs/{job_id}`
  - `job_poll_interval_seconds`, default `3`
- Route:
  - `agent.job.submit`
  - `agent.job.poll`
  - optionally `material.properties.review.submit`
- Return a frontend-friendly result shape:

```json
{
  "success": true,
  "data": {
    "accepted": true,
    "job_id": "job_8848",
    "status": "queued",
    "queue_position": 3,
    "estimated_wait_seconds": 45
  }
}
```

Validation:

```powershell
$env:SW_HOME='E:\0B 软件开发\MechPilot\Cockpit\deploy'
dotnet build 'src\SwAgentAddin\SwAgentAddin.csproj' -v:minimal
```

### Agent B2: WebView2 Frontend Queue UI

Owned files:

- `src/SwAgentAddin/frontend/property-workbench/app.js`
- `src/SwAgentAddin/frontend/property-workbench/styles.css`

Responsibilities:

- Add queue job state to the frontend.
- Submit `agent.job.submit` or `material.properties.review.submit`.
- Render queued receipt:
  - job id
  - queue position
  - estimated wait seconds
  - current status
- Poll every 3 seconds with `agent.job.poll`.
- Stop polling on `completed`, `failed`, or `cancelled`.
- Avoid timer leaks when submitting a new job.
- Show Chinese fallback message when Hermes is offline.

### Agent A: Hermes Queue Service

This is a separate repository task. Do not implement it inside the Cockpit repo unless the Hermes service code is present.

Responsibilities:

- Create SQLite tables:
  - `jobs`
  - `job_items`
- Add:
  - `POST /api/jobs`
  - `GET /api/jobs/{job_id}`
- Add mock `SchedulerCore` for Phase 1A:
  - `queued -> assigned -> running -> completed`
  - updates `completed_items`, `failed_items`, `progress_percent`
- Keep MCP calls behind an adapter interface and use a mock executor in this phase.

## Payload Draft

```json
{
  "intent": "material_properties_review",
  "session_context": {
    "assembly_path": "C:\\CAD\\Asm.SLDASM",
    "assembly_unc_path": "\\\\192.168.1.20\\Share\\CAD\\Asm.SLDASM",
    "view_mode": "flat",
    "auto_fix_enabled": true,
    "engineer_id": "davis",
    "total_selected": 15
  },
  "components": []
}
```

## Integration Acceptance

- C# build passes.
- Cockpit can submit a job command without crashing when Hermes is offline.
- With a mock Hermes service, UI shows:
  - queued
  - queue position
  - estimated wait
  - running progress
  - completed or failed
- Existing buttons and pages continue to work.

