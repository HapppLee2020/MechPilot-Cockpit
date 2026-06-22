# MechPilot Agent Cockpit Architecture Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a new MechPilot Agent cockpit button and build a future-proof WebView2 cockpit architecture for SolidWorks property browsing, assembly intelligence, local actions, and remote Agent/MCP orchestration.

**Architecture:** Keep the current SolidWorks add-in as the trusted native shell and data/action backend. Add a new `Agent驾驶舱` command that opens a WebView2-based cockpit; C# collects SolidWorks context into versioned JSON contracts, and the web UI renders assembly trees, property tables, filters, mode switches, reports, and future agent workflows. Existing WinForms read/write features remain as fallback while the cockpit evolves.

**Tech Stack:** C# .NET Framework 4.8, SolidWorks 2022 COM interop, Microsoft WebView2 WinForms, local HTML/CSS/JS cockpit assets, JSON message bridge via `JavaScriptSerializer`.

---

## Product Position

MechPilot is not a traditional SolidWorks Add-in with scattered command dialogs. It is an **Agent驾驶舱**:

- SolidWorks context enters through native C#.
- Local mode can directly read/write current SW files.
- Remote mode can submit tasks to Agent Server, MCP, and target worker machines.
- The cockpit UI should look and behave like a modern engineering operations console.
- The UI must scale toward BOM intelligence, property governance, drawing review, audit, reports, queue state, and multi-agent execution traces.

## Why Change Direction

The current `DataGridView` path proved the local capability, but it is not the right long-term UI substrate for:

- Excel-like header filters.
- Dynamic property columns.
- Assembly tree and table linkage.
- Frozen columns and wide-table navigation.
- Search, grouping, export, status badges, and batch operations.
- Remote task progress and Agent reasoning traces.
- A polished, modern product experience.

So the next step is **not** to keep making the WinForms grid more complex. The next step is to add a new cockpit button and build the WebView2 cockpit beside existing features.

## Button Strategy

Add a new CommandManager button:

| Button | Meaning |
|--------|---------|
| `Agent驾驶舱` | Opens the new WebView2 cockpit. This is the strategic UI surface. |

Existing buttons remain:

- `属性填写`
- `读取属性`
- `属性检查`
- `图纸审核`
- `任务面板`
- `插件设置`

Recommended command order:

```text
Agent驾驶舱 | 属性填写 | 读取属性 | 属性检查 | 图纸审核 | 任务面板 | 插件设置
```

This requires a new 7-icon strip later. For the first build, the cockpit can reuse the MechPilot main icon or a simple generated icon, as long as command text is correct.

## Target Architecture

```text
MechPilot SolidWorks Add-in
  ├─ CommandManager
  │   ├─ Agent驾驶舱
  │   ├─ 属性填写
  │   ├─ 读取属性
  │   └─ ...
  │
  ├─ Native Context Layer
  │   ├─ Active document detector
  │   ├─ Selection resolver
  │   ├─ Assembly tree collector
  │   ├─ Property reader/writer
  │   └─ File/PDM metadata collector
  │
  ├─ Capability Layer
  │   ├─ local.read_properties
  │   ├─ local.write_properties
  │   ├─ remote.submit_task
  │   ├─ remote.poll_task
  │   └─ future: pdm, drawing, bom, rule, audit
  │
  ├─ Cockpit Bridge
  │   ├─ C# -> JS: context JSON
  │   ├─ JS -> C#: command JSON
  │   ├─ event/result envelope
  │   └─ error/audit logging
  │
  └─ WebView2 Cockpit Host
      └─ property-workbench/index.html

property-workbench
  ├─ index.html
  ├─ styles.css
  ├─ app.js
  ├─ mock-data.js
  └─ libs/
```

## Contract First

All cockpit data must flow through versioned JSON. Do not bind UI directly to SolidWorks COM objects.

### Context Envelope

```json
{
  "schema_version": "mechpilot.cockpit.context.v1",
  "generated_at": "2026-06-21T20:00:00+08:00",
  "client": {
    "name": "MechPilot",
    "version": "1.0.0",
    "machine": "DESKTOP"
  },
  "mode": "local",
  "active_document": {
    "name": "Assembly1.SLDASM",
    "path": "D:\\vault\\Assembly1.SLDASM",
    "doc_type": "assembly",
    "configuration": "默认"
  },
  "selection": {
    "selected_count": 0,
    "targets": []
  },
  "assembly_tree": {
    "node_id": "root",
    "name": "Assembly1",
    "doc_type": "assembly",
    "file_path": "D:\\vault\\Assembly1.SLDASM",
    "quantity": 1,
    "children": []
  },
  "property_table": {
    "intrinsic_columns": ["零部件名称", "数量", "文档类型", "文件路径", "文件大小"],
    "property_columns": ["物料名称", "图号", "材料", "重量"],
    "rows": []
  },
  "capabilities": [
    "local.read_properties",
    "local.write_properties",
    "remote.submit_task"
  ]
}
```

### Command Envelope

```json
{
  "schema_version": "mechpilot.cockpit.command.v1",
  "command_id": "cmd-001",
  "type": "local.read_properties",
  "payload": {
    "scope": "active_document"
  }
}
```

### Result Envelope

```json
{
  "schema_version": "mechpilot.cockpit.result.v1",
  "command_id": "cmd-001",
  "ok": true,
  "message": "读取完成",
  "data": {}
}
```

## First Milestone

The first WebView2 cockpit must demonstrate:

- New `Agent驾驶舱` button opens a cockpit window.
- Cockpit loads from local `property-workbench/index.html`.
- Cockpit shows current file name and mode.
- Cockpit receives context JSON from C#.
- Left side renders assembly tree.
- Right side renders property table.
- Table supports:
  - dynamic columns
  - header filtering
  - search
  - resolved/raw value switch
  - responsive width
- Tree and table selection can link through `node_id` / `row_id`.
- Existing WinForms `读取属性` remains available as fallback.

## Cockpit Overview Must Be Component-Centric

The cockpit overview is not a generic dashboard. For SolidWorks users, the most important operating object is always the current part/component/assembly node. Therefore the overview page must use the design tree / assembly tree as its primary view.

Required overview behavior:

- Render the current document design tree by default.
- Expand the root node and first-level subassemblies by default.
- Show part and subassembly nodes with document name, document type, quantity, suppressed/lightweight state, and warning/property status when available.
- Keep a shared selected component state across overview, property, BOM, and AI assistant views.
- When a tree node is selected, update the component summary panel immediately.
- The summary panel should show component name, document type, quantity, file path, file size, key properties, and available actions.
- AI assistant messages should include the currently selected component context.

Recommended layout:

```text
Overview
├─ Left: design tree / assembly tree
├─ Center: selected component summary and key properties
└─ Right or bottom: quick actions
   ├─ read properties
   ├─ check properties
   ├─ locate in BOM
   └─ send to AI assistant
```

## Multi-Agent Execution

Use these task files:

- `output\MechPilot_Agent_I_CockpitContracts_Task.md`
- `output\MechPilot_Agent_J_WebView2Host_Task.md`
- `output\MechPilot_Agent_K_CockpitFrontend_Task.md`
- `output\MechPilot_Agent_L_ContextCollector_Task.md`
- `output\MechPilot_Agent_M_IntegrationQA_Task.md`

## Merge Order

1. Agent I: contracts and config.
2. Agent K: frontend can run with mock data independently.
3. Agent J: WebView2 host and button.
4. Agent L: real context collector and bridge data.
5. Agent M: integration, deployment, real SolidWorks smoke.

## Non-Negotiable Guardrails

- Do not change COM GUID.
- Do not break existing add-in loading.
- Do not remove existing local mode or read/write commands.
- Do not require internet at runtime.
- Do not use CDN assets in the deployed cockpit.
- Do not pass COM objects into JavaScript; pass JSON only.
- Cockpit failure must not make `ConnectToSW` fail.

## Verification

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build output\SW-Agent-Addin\SwAgentAddin.csproj -c Release
```

Then run a real SolidWorks smoke test:

- enable MechPilot
- open Agent驾驶舱
- verify cockpit loads
- verify current file context appears
- verify property table and assembly tree render
- verify existing 属性填写 and 读取属性 still work
