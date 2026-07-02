# CKP-004-05 任务编排页布局修正报告

> 本环境为无 UI 的开发机，无法截取运行时截图。按需求"截图或说明"中的"说明"方式提交，
> 含修正前后 ASCII 布局对比、改动点清单、验收逐条核对。

## 一、修正前布局

```
┌─ topbar ─────────────────────────────────────────────────────┐
├──────┬────────────────────────────────────────────┬──────────┤
│ side │ page-container (非 flex 容器，仅滚动)        │ ai-panel │
│ bar  │ ┌──────────────────────────────────────┐   │          │
│      │ │ .workspace-page { flex:1 失效 }      │   │          │
│      │ │  page-title                          │   │          │
│      │ │  ws-context-bar                      │   │          │
│      │ │  ws-body { overflow-y:auto }         │   │          │
│      │ │   ws-layout  min-height:400px        │   │          │
│      │ │   grid: 1fr 1fr 240px                │   │          │
│      │ │   ┌────────┬────────┬──────┐         │   │          │
│      │ │   │ ws-left │ ws-ctr │ ws-r │         │   │          │
│      │ │   │ 1fr     │ 1fr    │ 240  │         │   │          │
│      │ │   │设计树   │属性表  │动作  │         │   │          │
│      │ │   │        │max320  │      │         │   │          │
│      │ │   │        │任务队列│      │         │   │          │
│      │ │   │        │max200  │      │         │   │          │
│      │ │   └────────┴────────┴──────┘         │   │          │
│      │ │   ↑ 仅 400px 高，下方大量空白 ▽▽▽      │   │          │
│      │ └──────────────────────────────────────┘   │          │
├──────┴────────────────────────────────────────────┴──────────┤
│ statusbar                                                    │
└──────────────────────────────────────────────────────────────┘
```

### 问题根因
1. `.page-container` 在 workspace 模式下**不是 flex 容器**——只有 `.page-overview-active`
   才带 `display:flex`，而 `navigatePage()` 实际切的是 `.page-workspace-active`（无对应 CSS）。
   因此 `.workspace-page { flex:1 }` 失效，页面高度仅由内容撑开。
2. `.ws-layout` 只设了 `min-height:400px`，视口高于内容时底部留白。
3. `grid-template-columns: 1fr 1fr 240px`：左/中平分，中部未成主体。
4. `.prop-workbench { max-height:320px }`、`.task-queue-container { max-height:200px }` 固定，
   无法吃满剩余高度。

## 二、修正后布局

```
┌─ topbar ─────────────────────────────────────────────────────┐
├──────┬────────────────────────────────────────────┬──────────┤
│ side │ page-container.page-workspace-active       │ ai-panel │
│ bar  │  display:flex; flex-direction:column        │          │
│      │ ┌──────────────────────────────────────┐   │          │
│      │ │ .workspace-page { height:100% }      │   │          │
│      │ │  page-title  (flex-shrink:0)         │   │          │
│      │ │  ws-context-bar (flex-shrink:0)      │   │          │
│      │ │  ws-body { flex:1; overflow:hidden } │   │          │
│      │ │   ws-layout { height:100% }          │   │          │
│      │ │   grid: minmax(260,320) minmax(520,1fr) 220 │ │     │
│      │ │   ┌────────┬────────────────┬──────┐ │   │          │
│      │ │   │ws-left │ ws-center       │ws-r  │ │   │          │
│      │ │   │260~320 │ minmax(520,1fr) │220px │ │   │          │
│      │ │   │设计树  │ panel-header    │动作  │ │   │          │
│      │ │   │(flex:1 │ prop-workbench  │+job  │ │   │          │
│      │ │   │ 滚动)  │  flex:1 吃满    │status│ │   │          │
│      │ │   │        │ panel-header    │      │ │   │          │
│      │ │   │        │ task-queue 220  │      │ │   │          │
│      │ │   └────────┴────────────────┴──────┘ │   │          │
│      │ │   ↑ 高度链贯通，撑满到底，无留白      │   │          │
│      │ └──────────────────────────────────────┘   │          │
├──────┴────────────────────────────────────────────┴──────────┤
│ statusbar                                                    │
└──────────────────────────────────────────────────────────────┘
```

## 三、改动点（仅 src CSS）

| 位置 | 改动 |
|------|------|
| styles.css `.page-container` | 新增 `.page-container.page-workspace-active { overflow:hidden; padding:0; display:flex; flex-direction:column; min-height:0; }`，使 workspace 页获得 flex 高度链 |
| styles.css `.workspace-page` | 加 `min-height:0; height:100%` |
| styles.css `.ws-context-bar` | 追加 `flex-shrink:0` |
| styles.css `.ws-body` | `overflow-y:auto → overflow:hidden; min-height:0`（滚动下放给各子区） |
| styles.css `.ws-layout` | `1fr 1fr 240px → minmax(260px,320px) minmax(520px,1fr) 220px`；`min-height:400px → height:100%; min-height:0` |
| styles.css `.ws-left/.ws-center/.ws-right` | 左/右加边框卡片 + overflow；中改为 `display:flex; flex-direction:column`；均加 `min-height:0` |
| styles.css `.prop-workbench` | 去掉 `max-height:320px`，改为 `flex:1 1 auto; min-height:0; overflow:auto`（吃满上方剩余高度） |
| styles.css `.task-queue-container` | 改为 `flex:0 0 auto; height:220px`（独立下栏，与属性表物理分离） |
| styles.css `@media(max-width:960px)` | 单列堆叠时 `ws-body` 回到 `overflow-y:auto`，`ws-layout height:auto`，左/右给最小高度，保证小窗可滚动不溢出 |

### 说明：底部统计栏 `.overview-stats-bar`
经核查，该 class 为**孤儿 CSS**——`renderWorkspace()` 当前从不输出任何 `overview-stats-bar`
元素，底部统计栏在现有页面中实际不存在，因此"移除压缩底部大条"无实体可移除。
布局空白的真正根因是**高度链断裂**（见上），已在 `.page-workspace-active` + `height:100%` 链中修复。

## 四、验收逐条核对

| 验收要求 | 结果 |
|---------|------|
| 1. 打开后底部不再大片空白 | ✅ 高度链贯通：page-container(flex) → workspace-page(height:100%) → ws-body(flex:1) → ws-layout(height:100%) 撑满 |
| 2. 左设计树宽度 ≤360px | ✅ `minmax(260px,320px)`，最大 320px |
| 3. 中部成视觉主区域 | ✅ `minmax(520px,1fr)` 取全部剩余宽度 |
| 4. 属性表 + 任务队列利用剩余高度 | ✅ prop-workbench `flex:1` 吃满上方，task-queue `220px` 独立下栏 |
| 5. 缩放/窗口变化稳定 | ✅ `min-height:0`+overflow 正确传递；≤960px 切单列滚动，左/右给最小高度防压扁 |
| 6. 不破坏 AI 对话 / 属性审核 / Hermes /v1/runs | ✅ 仅改 CSS，HTML 结构、元素 ID、class 名、app.js 逻辑全部未动；AI 面板、prop-table、task-queue、job-status 与 Hermes 链路无任何接触面 |

## 五、目标机部署与验证（MCP 工具流程）

> 依据：`D:\MechPilot\Architecture\SERVICE-ENDPOINTS.md`（2026-06-28 生效）。
> MCP Server：`http://10.254.60.31:19090/mcp`，目标工作站 `10.254.60.31`，
> Cockpit 目标运行目录 `D:\SWAgentAddin`，Z 盘中转 `Z:\MechPilot`。

### 5.1 当前同步与部署状态（必答 1、2）

| 项 | 状态 |
|----|------|
| 1. 是否已同步 Z 盘 | ✅ **已同步**。本次改动的 `styles.css` 已从源码 `src/SwAgentAddin/frontend/property-workbench/styles.css` 复制到本地 `deploy/frontend/property-workbench/styles.css` 及 Z 盘部署包 `Z:\MechPilot\Cockpit\deploy\frontend\property-workbench\styles.css`。三处 SHA256 一致：`83AE7B06…895CE16`，且含全部布局修正标记（`page-workspace-active` / `minmax(260px,320px)` / `task-queue-container{flex:0 0 auto`）。 |
| 2. 是否请求目标机部署 | ❌ **未部署、未请求部署**。按本轮约束，在总架构师明确要求进入测试部署环节前，不覆盖目标机 `D:\SWAgentAddin`。目标机当前 `styles.css` 状态未改动。 |
| MCP 端点连通性 | ✅ `http://10.254.60.31:19090/mcp` 在线可达（HTTP 406 为 MCP 协议对裸 GET 的正常拒绝，非连通问题）。MCP 工具 `sw_file_ops` 预期可用，本轮未实际调用（未进入部署环节）。 |

> 同步方式说明：本次仅改前端 CSS（纯静态文件，不涉及 C#/DLL），故**未走 `dotnet build`**
> （本开发机未安装 SolidWorks，缺 SW Interop 引用，完整编译会失败；且本次无 C# 改动，无需编译）。
> 改动经文件复制同步到 deploy 与 Z 盘，等效于 `.csproj` 的 `CopyToOutputDirectory` 前端资源复制。

### 5.2 待授权后的目标机部署方案（必答 3）

总架构师批准进入测试部署后，**通过 MCP 工具 `sw_file_ops` 执行**，不手工复制：

- **MCP 来源**：`Z:\MechPilot\Cockpit\deploy\frontend\property-workbench`
- **MCP 目标**：`D:\SWAgentAddin\frontend\property-workbench`
- **建议复制文件**（按本轮改动范围）：
  - `styles.css` —— 本次已改，**必须复制**
  - `app.js` —— 本轮**未改**（src 与 Z 盘已一致），如需保持目标机与部署包对齐可一并复制
  - `index.html` —— 本轮**未改**，按需复制
- **复制前备份**（必答 4 的前置）：目标机对应文件备份到
  `Z:\MechPilot\archive\yyyyMMdd-HHmmss\Cockpit\frontend\property-workbench`
  （本轮 Z 盘侧旧版 styles.css 已备份于 `Z:\MechPilot\archive\20260628-164812\Cockpit\frontend\property-workbench\styles.css`）。
- **默认不覆盖**（必答 5）：
  - `D:\SWAgentAddin\config\config.json`
  - `D:\SWAgentAddin\config\rules.local.json`

### 5.3 目标机验证方法（不手工猜路径）

部署后通过 MCP 工具核对，而非手工检查：

1. `sw_file_ops stat` / `list` 校验 `D:\SWAgentAddin\frontend\property-workbench\styles.css`
   存在且 SHA256 = `83AE7B06…895CE16`。
2. `sw_file_ops exists` 校验 `config\config.json`、`config\rules.local.json` 仍在、未被覆盖。
3. 让 SolidWorks 重新加载前端（完全退出 `SLDWORKS.exe` 后重启，或 Cockpit 面板 `Ctrl+F5`）。
4. 在任务编排页目视确认 4.1–4.5 验收项；触发一次 Hermes `/v1/runs` 任务确认链路无回归。

> 如 MCP 工具不可用，才退化为人工复制；默认不依赖人工。

## 六、未改动说明
- `app.js`：未改。`renderWorkspace()` 输出的 DOM、`#prop-workbench` / `#task-list-container` /
  `#action-list` / `data-job-status-panel` 等 ID 全部保留。
- `index.html`：未改。
- `config/config.json`、`config/rules.local.json`：**默认不覆盖**，本轮未触碰目标机与 Z 盘 config。
- 孤儿 class（`.overview-*` 布局、`.overview-stats-bar`、`.task-list-container`）本次保留未删，
  以免误伤；后续清理可单独建任务。
