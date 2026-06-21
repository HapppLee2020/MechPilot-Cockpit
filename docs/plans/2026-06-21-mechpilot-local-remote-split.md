# MechPilot Local And Remote Mode Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build MechPilot into a SolidWorks add-in with two execution modes: local mode writes SolidWorks custom properties directly, and remote mode submits tasks to Agent Server/MCP.

**Architecture:** Keep one plugin and two executors. Shared code resolves the active target, collects context, builds a property update plan, then dispatches either to a local SolidWorks property writer or a remote task submitter. The first deliverable must make local mode visibly update part/assembly/component custom properties inside SolidWorks.

**Tech Stack:** C# .NET Framework 4.8, SolidWorks 2022 COM interop, embedded TaskPane HTML, JSON config via `JavaScriptSerializer`.

---

## Current State

Workspace:

`F:\davis\Documents\WPS灵犀\20260617-22-20-29-842`

Primary source:

`output\SW-Agent-Addin`

Current core file:

`output\SW-Agent-Addin\SwAgentAddin.cs`

Current plugin status:

- User-visible branding has mostly been renamed to `MechPilot`.
- The add-in loads through official `SolidWorks.Interop.swpublished.ISwAddin`.
- Buttons call `ExecuteTask(...)`, which currently POSTs to `/api/v1/task`.
- There is no local property update mode yet.
- There is no status/result rendering loop yet.

Do not change:

- COM GUID: `E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1`
- Official `SolidWorks.Interop.swpublished.ISwAddin`
- `ClassInterface(ClassInterfaceType.AutoDual)`
- Early `SetAddinCallbackInfo2(0, this, Cookie)` behavior
- Conservative load path: UI failures must not make `ConnectToSW` return `false`

## Two-Agent Split

### Agent A: Local SolidWorks Property Execution

Owns:

- Target resolution
- Local update plan execution
- Custom property writing
- Selection behavior in part/assembly/drawing contexts
- Result summary for local writes

Avoids:

- Rewriting config schema beyond consuming fields from Agent B
- Reworking remote POST behavior
- Large TaskPane redesign

Task file:

`output\MechPilot_Agent_A_LocalMode_Task.md`

### Agent B: Config, Rules, Modes, And Remote Contract

Owns:

- Config schema
- Local rules JSON
- Execution mode abstraction
- Remote task request contract
- README/HANDOFF updates
- Optional mock Agent Server for remote smoke

Avoids:

- Direct SolidWorks property writing
- SelectionManager/component traversal logic
- COM registration changes unless docs/scripts need wording updates

Task file:

`output\MechPilot_Agent_B_ConfigRemote_Task.md`

## Merge Order

1. Merge Agent B first if it only adds config/rules/types and keeps current behavior compatible.
2. Merge Agent A after it consumes the new config shape.
3. Final integration owner verifies local mode in SolidWorks.
4. Only after local mode passes, re-check remote mode still submits to `/api/v1/task`.

## Acceptance Criteria

Local mode:

- `execution_mode = "local"` causes `Property Fill` to update properties without requiring Agent Server.
- Open part with no selection: updates active part properties.
- Open assembly with no selection: updates assembly document properties.
- Open assembly with one selected component: updates selected component model document properties.
- Open assembly with multiple selected components: shows a confirmation form/list before updating each selected component.
- Result reports target count, succeeded count, failed count, and changed property names.
- No property names or fixed values are hardcoded in the execution logic; defaults come from JSON rules.

Remote mode:

- `execution_mode = "remote"` preserves current POST behavior.
- Request body includes enough context for later MCP execution: target scope, selected component paths, document type, engineer id, task type, and local rule version if present.
- Remote mode failure reports server connection errors clearly.

Verification:

```powershell
dotnet build output\SW-Agent-Addin\SwAgentAddin.csproj -c Release
```

Then perform one real SolidWorks smoke run before calling the work done.

## Final Integration Checklist

- Build succeeds.
- `config.json` contains `execution_mode`.
- `rules.local.json` exists and is copied to deploy output.
- The add-in still loads and can be enabled.
- `D:\SWAgentAddin\addin-load.log` shows `ConnectToSW completed successfully.`
- Local mode writes test properties.
- Remote mode still submits task JSON.
- README explains local vs remote mode.
- HANDOFF records what was verified and what remains demo-only.
