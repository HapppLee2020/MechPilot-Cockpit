# Agent R Prompt：AIPilot / AICockpit Shell、页面导航与 AI 侧栏

任务书地址：

`E:\0B 软件开发\MechPilot\Cockpit\docs\tasks\MechPilot_Agent_R_AIPilot_CockpitShell_Task.md`

## 背景

AICockpit 是 MechPilot 的智能系统入口，不吞并本地工具，而是承载 AI 对话、图纸审核、快速选型、物料检索、设计计算、Agent 任务和上下文展示。

本任务负责 WebView2 前端 Shell 重构。必须保证页面可打开，不是空白页，并能显示真实 SolidWorks 上下文。

## 重要路径

工程根目录：

`E:\0B 软件开发\MechPilot\Cockpit`

前端源码：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench`

关键文件：

- `E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\index.html`
- `E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\app.js`
- `E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\styles.css`
- `E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\mock-data.js`

构建后部署：

`E:\0B 软件开发\MechPilot\Cockpit\deploy\frontend\property-workbench`

真实运行：

`D:\SWAgentAddin\frontend\property-workbench`

注意：旧的 `D:\SWAgentAddin\property-workbench` 可能仍存在，但新结构应优先使用 `D:\SWAgentAddin\frontend\property-workbench`。

## 页面结构

实现：

```text
AICockpit
├─ TopBar
├─ Sidebar
│  ├─ 总览
│  ├─ AI助手
│  ├─ 图纸审核
│  ├─ 快速选型
│  ├─ 物料检索
│  ├─ 设计计算
│  ├─ Agent任务
│  └─ 设置
├─ 主内容区
└─ 右侧 AI 对话面板
```

## 页面要求

### 总览

- 当前文件名。
- 文档类型。
- 当前执行模式。
- 数据来源：真实数据 / 演示数据。
- 装配体零部件数量。
- **设计树必须作为总览主视图默认展开**。驾驶舱最基本和最重要的对象是零部件，总览不能只做统计卡片。
- 总览布局建议：
  - 左侧：当前文档/装配体设计树，默认展开根节点和第一层子装配。
  - 中间：选中零部件的摘要卡片、关键属性、数量、文件路径、文件大小。
  - 右侧或底部：当前选中对象可执行动作，例如读取属性、属性检查、BOM定位、发送给AI分析。
- 设计树节点需要显示：
  - 零部件/子装配名称。
  - 文档类型。
  - 数量。
  - 抑制/轻化状态。
  - 属性缺失或警告标记，如上下文中可获得。
- 总览中的设计树必须和后续属性/BOM/AI页面共享同一个选中对象状态。
- 点击设计树节点后：
  - 更新当前选中对象。
  - 刷新右侧摘要。
  - AI侧栏发送消息时应带上该选中对象上下文。
- 属性完整度占位。
- 最近任务占位。

### AI助手

- 消息列表。
- 输入框。
- 发送按钮。
- 通过 WebView2 `postMessage` 发送 `ai.assistant.chat`。
- Hermes 未接入时返回本地占位回答。

### 图纸审核

- 这是 AI 页面，不是本地图纸导出。
- 发送 `ai.drawing.review`。
- Hermes 未在线时显示“等待 Agent 服务接入”。

### 快速选型 / 物料检索 / 设计计算

- 先实现页面、输入区、结果区。
- 可以使用 mock 结果。
- 必须通过统一命令协议发 action。

### 物料检索 RAG 要求

`物料检索` 不只是普通关键字搜索，应设计为 RAG 向量检索入口。

前端页面需要展示或预留：

- 向量数据库状态。
- SQLite 原始库路径。
- collection 名称。
- top_k。
- score_threshold。
- 检索结果列表：物料名称、规格型号、材料、供应商、图号、相似度、来源片段。

命令仍发送：

`ai.material.search`

payload 建议：

```json
{
  "query": "不锈钢直线导轨",
  "material": "SUS304",
  "top_k": 8,
  "collection": "materials"
}
```

如果 HighSight/RAG 服务未接入，显示本地 mock 结果和中文提示，不要崩溃。

### Agent任务

- 显示任务列表。
- 支持 task_id、状态、进度、结果摘要占位。

### 设置

- 展示当前 `execution_mode`、Hermes 地址、context mode、部署目录摘要。
- 展示 RAG 配置摘要：
  - provider: highsight
  - sqlite_db_path
  - collection
  - top_k
  - score_threshold

## AI 侧栏

右侧 AI 面板必须：

- 可展开/收起。
- 任意页面可用。
- 发送消息时附带当前页面、当前选中对象、当前文件摘要、context mode。
- 没有 Hermes 时不崩溃。

## 与 C# 通讯

必须兼容：

- `receiveContext(context)`
- `sendCommand(type, payload)`
- `refresh_context`
- `local.read_properties`

新增支持：

- `navigate_page`
- `ai.assistant.chat`
- `ai.drawing.review`
- `ai.selection.recommend`
- `ai.material.search`
- `ai.design.calculate`
- `agent.task.submit`
- `agent.task.poll`

如果 Agent P 提供工具栏页面快捷入口，前端需要能根据 C# 传入的页面参数切换到指定页面。

## 验收

执行：

```powershell
node --check "E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\app.js"
```

并构建：

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build "E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.csproj" -c Release
```

必须：

- 页面不是空白页。
- 能显示真实当前文件名。
- Sidebar 可切换。
- AI 侧栏可展开/收起。
- `D:\SWAgentAddin\frontend\property-workbench\app.js` 是新版本。
