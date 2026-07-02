# Agent V Task: 属性工作区字段筛选与复选框联动一次性交付

## 背景

当前 Cockpit 的“任务编排”页中间区域有“选中零部件属性工作区”。用户要求在属性工作区每个字段增加之前规划的筛选功能，并在“文件名称”前增加与左侧设计树联动的复选框，使用户可以直接在属性工作区取消勾选对应零部件。

本任务要求开发 agent 尽可能一次性交付：先做 Git 存档，再开发、反复自测、修复问题、构建通过后完成部署。遇到需要管理员权限、UAC、COM 注册、SolidWorks 关闭/重启等授权场景，开发 agent 应根据风险自行判断并提交授权请求，不要把部署留给用户。

## 必须先做：Git 存档

动手改代码前必须在仓库当前状态上做一次可回滚存档。

工作目录：

```powershell
cd D:\MechPilot\Cockpit
git status --short
git add -A
git commit -m "archive: before property workbench field filters"
git rev-parse --short HEAD
```

要求：

- 不要丢弃现有未提交改动。
- 如果 `git commit` 因 user.name / user.email 缺失失败，先做本仓库级配置后再提交，例如：

```powershell
git config user.name "MechPilot Dev Agent"
git config user.email "dev-agent@mechpilot.local"
git commit -m "archive: before property workbench field filters"
```

- 如果没有任何可提交内容，也要记录 `git status --short` 和 `git rev-parse --short HEAD`。
- 如果后续开发失败且无法修复，优先用这个存档 commit 回滚，不要手工猜测恢复。

## 目标文件

主改文件：

- `D:\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\app.js`
- `D:\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\styles.css`

可选同步文件：

- `D:\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\README.md`
- `D:\MechPilot\Cockpit\docs\tasks\MechPilot_Agent_V_PropertyWorkbench_FieldFilters_Task.md`

构建和部署相关文件只读参考，不要随意改：

- `D:\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.csproj`
- `D:\MechPilot\Cockpit\deploy\scripts\build.bat`
- `D:\MechPilot\Cockpit\deploy\setup_deploy.bat`
- `D:\MechPilot\Cockpit\deploy\quick_update.bat`
- `D:\MechPilot\Cockpit\deploy\DEPLOY_GUIDE.md`

## 当前代码入口

在 `app.js` 中重点查看这些位置和函数名，行号可能会随修改变化，以函数名为准：

- `state`：当前已有 `checkedNodeIds`、`treeFilters`、`taskQueueFilter` 等状态，应新增属性工作区筛选状态。
- `PROP_COLUMNS`：当前属性工作区列定义。
- `resolvePropValue(node, propKey)`：属性值解析入口，筛选与渲染都应复用它，避免显示值和筛选值不一致。
- `getWorkspaceItems()`：属性工作区唯一数据源，按 group 去重。
- `renderPropertyWorkbench()`：当前生成 `<table class="prop-table">`、表头和行。
- `renderDesignTree()` / `renderTreeNode()` / 勾选相关函数：设计树复选框与 `state.checkedNodeIds` 的源头。
- `toggleNodeCheck(...)`、`syncGroupCheckState(...)`、`getNodeGroupKey(...)`、`findNodesByGroupKey(...)` 等与左侧树勾选联动相关函数。

当前 `PROP_COLUMNS` 目标字段为：

| key | label | 类型 |
| --- | --- | --- |
| `fileName` | `文件名称` | 固有字段 |
| `docType` | `文件类型` | 固有字段 |
| `instanceCount` | `实例数` | 固有字段 |
| `filePath` | `文件路径` | 固有字段 |
| `fileSize` | `文件大小` | 固有字段 |
| `物料编码` | `物料编码` | SW 自定义属性，带别名 |
| `物料名称` | `物料名称` | SW 自定义属性，带别名 |
| `规格型号` | `规格型号` | SW 自定义属性，带别名 |
| `材质` | `材质` | SW 自定义属性，带别名 |
| `表面处理` | `表面处理` | SW 自定义属性，带别名 |
| `设计人` | `设计人` | SW 自定义属性，带别名 |
| `物料状态` | `物料状态` | SW 自定义属性，带别名 |

## 范围

### 必做

1. 给属性工作区每个字段增加列级筛选入口。
2. 筛选入口必须放在每个字段表头上，不要额外增加一整排筛选按钮。
3. 支持所有 `PROP_COLUMNS` 字段筛选，包括固有字段和自定义属性字段。
4. 支持多列 AND，同列多值 OR。
5. 支持空值筛选，空值显示为 `(空白)`，表格空单元格仍可显示为 `—`。
6. 表头需展示筛选状态，例如未筛选 `字段名 ▾`，已筛选 `字段名 ●` 或等效视觉标记。
7. 增加“清空全部筛选”入口，仅在存在筛选时显示，位置可放在属性工作区表格上方的小工具条中。
8. 筛选后更新 `prop-table-count`，显示当前可见/已选零件数量，建议格式：`可见 N / 已选 M 个零件`。
9. 在“文件名称”列前增加一列复选框列，表头有全选/全不选复选框。
10. 属性工作区行复选框必须与左侧设计树勾选状态双向联动：
    - 属性工作区取消勾选某一行，左侧设计树对应 group 的所有节点同步取消勾选，该行从当前工作区消失或在下一次渲染后不再出现。
    - 属性工作区勾选某一行，左侧设计树对应 group 的所有可提交节点同步勾选。
    - 左侧设计树勾选/取消后，属性工作区复选框和行数据同步刷新。
11. 表头全选/全不选只作用于“当前筛选后的可见行”，不要影响被筛选隐藏的行。
12. 保持当前行点击选择节点的行为，不要因为复选框点击触发错误的行选择；复选框点击要 `stopPropagation()`。
13. 不破坏现有“读取属性”“属性检查”“属性审核”“任务队列”“设计树筛选”等功能。
14. 保持现有整体布局和视觉风格，不要大改页面结构。
15. 完成后必须构建、部署，并验证部署目录中的前端文件确实更新。

### 不做

- 不改 C# 属性读取主链路，除非发现前端无法完成且必须改。
- 不改 Hermes 服务接口。
- 不改 PDM/MCP 后端。
- 不重写整个 `app.js` 或引入前端框架。
- 不删除当前设计树筛选功能。

## 任务列表

### 任务 1：字段表头筛选

目标：属性工作区每个字段都能在表头直接筛选。

- 覆盖字段：`文件名称`、`文件类型`、`实例数`、`文件路径`、`文件大小`、`物料编码`、`物料名称`、`规格型号`、`材质`、`表面处理`、`设计人`、`物料状态`。
- 筛选入口放在字段表头，不允许做成额外一整排按钮。
- 支持同列多值 OR、多列 AND、`(空白)`、清空单列、清空全部、无结果提示和表头已筛选标记。

### 任务 2：属性工作区复选框与设计树联动

目标：在属性工作区 `文件名称` 前面增加一列复选框，让用户可以直接在属性工作区取消勾选对应数据内容，并同步影响左侧设计树和后续属性审核任务。

- 在属性工作区表格首列新增复选框列，位置必须在 `文件名称` 前面。
- 每行复选框状态必须来自左侧设计树同一份勾选状态：`state.checkedNodeIds`。
- 在属性工作区取消勾选某一行时，必须同步取消左侧设计树中对应零部件/同 group 所有实例的勾选。
- 在属性工作区勾选某一行时，必须同步勾选左侧设计树中对应零部件/同 group 所有可提交实例。
- 左侧设计树勾选或取消后，属性工作区的复选框状态和可见数据必须同步刷新。
- 表头复选框用于全选/全不选，但只作用于当前筛选后的可见行，不允许影响筛选隐藏的行。
- 点击行复选框必须阻止冒泡，不能误触发行选中；点击行其它区域仍保留原来的设计树定位/选中联动。
- 取消勾选后的零件不得进入 `属性审核`、`属性检查` 或任何基于当前选中零件集合生成的任务 payload。

### 任务 3：联动回归与任务 payload 校验

目标：确认筛选和复选框不会造成工作区、设计树、任务队列之间状态不一致。

- 筛选后执行表头全选/全不选，只改变当前可见行。
- 清空筛选后，已取消勾选的零件不应自动恢复，除非用户重新勾选。
- 从设计树重新勾选零件后，属性工作区应重新出现该行。
- 提交属性审核任务前，检查 payload 只包含当前仍勾选的零件。
- 保持 `读取属性`、`属性检查`、`属性审核`、`任务队列`、`AI 对话` 正常工作。

## 推荐实现方案

### 1. 属性工作区筛选状态

在 `state` 中新增独立状态，例如：

```javascript
propertyWorkbenchFilters: {},
propertyWorkbenchFilterOpen: null
```

推荐结构：

```javascript
state.propertyWorkbenchFilters = {
  fileName: { values: ['A.SLDPRT', '(空白)'] },
  材质: { values: ['SUS304'] }
};
```

实现要求：

- 使用列 `key` 作为状态键。
- 空值内部可以用常量，例如 `__EMPTY__`，展示时映射为 `(空白)`。
- 若某列筛选值为空数组或不存在，视为未筛选。
- 重新读取属性或勾选变化后，不要自动清空筛选；但如果筛选条件导致无结果，应显示友好空状态。

### 2. 筛选工具函数

建议新增这些函数，名称可按现有风格调整：

```javascript
function normalizeFilterValue(value) { ... }
function getPropertyCellValue(item, col) { ... }
function getFilterOptions(workspaceItems, col) { ... }
function isPropertyFilterActive(colKey) { ... }
function doesItemPassPropertyFilters(item) { ... }
function getFilteredWorkspaceItems(workspaceItems) { ... }
function clearPropertyWorkbenchFilters() { ... }
```

关键要求：

- `getPropertyCellValue(item, col)` 必须复用 `resolvePropValue(node, col.key)`；`instanceCount` 要使用 `item.instanceCount || node.quantity || 1`，与当前渲染一致。
- 筛选候选项从“未应用当前列筛选、但应用其他列筛选”的数据中生成更符合 Excel 体验；如果实现复杂，至少要从当前全部已选 workspaceItems 中生成。
- 候选值按中文/数字友好排序；空值放最后。
- 候选项显示值要转义，不能引入 HTML 注入。

### 3. 表头筛选 UI

在 `renderPropertyWorkbench()` 中将表头从纯文本改为可点击筛选按钮，例如：

```html
<th data-col-key="材质">
  <button class="prop-filter-header" data-prop-filter-key="材质">
    <span>材质</span><span class="prop-filter-mark">▾</span>
  </button>
</th>
```

已筛选列添加 class，例如：

```html
<th class="prop-filter-active">...</th>
```

点击表头按钮后弹出轻量浮层或 dropdown：

- 标题：`筛选：材质`
- 搜索框：可选但推荐；候选值多时必须有搜索框。
- 候选值多选复选框列表。
- 操作按钮：`全选`、`清空`、`确定`、`取消`。
- 支持 `(空白)`。
- 点击外部或 ESC 关闭弹层。

注意：

- 不能让筛选弹层遮挡后无法关闭。
- 不要使用浏览器不支持的现代语法；WebView2 环境可用 ES5/基础 ES6，但建议沿用当前 `var` 和函数风格。
- 样式写入 `styles.css`，不要大量内联 style。

### 4. 筛选后表格渲染

`renderPropertyWorkbench()` 推荐流程：

```javascript
var workspaceItems = getWorkspaceItems();
var filteredItems = getFilteredWorkspaceItems(workspaceItems);
update count: filteredItems.length / workspaceItems.length
render toolbar if active filters
render table with filteredItems
```

空状态分两类：

- `workspaceItems.length === 0`：继续显示当前提示 `请在设计树中勾选零件...`。
- `workspaceItems.length > 0 && filteredItems.length === 0`：显示 `当前筛选无匹配零件，请调整或清空筛选。` 并提供 `清空全部筛选` 按钮。

### 5. 文件名称前复选框列

新增首列，不属于 `PROP_COLUMNS`：

```html
<th class="prop-check-col"><input type="checkbox" id="prop-visible-check-all"></th>
<td class="prop-check-col"><input type="checkbox" class="prop-row-check" ...></td>
```

要求：

- 行复选框 checked 状态来自 `state.checkedNodeIds` / group 状态，而不是临时 UI 状态。
- 每个 `workspaceItems` 已有 `nodeIds`、`groupKey` 或可从 `getNodeGroupKey(node)` 得到；优先按 group 联动，而不是只处理单个 node id。
- 对于一个 group 有多个树节点实例的情况，属性工作区一行取消勾选应同步取消该 group 所有对应树节点。
- 表头复选框状态：
  - 当前筛选可见行全选中：checked。
  - 部分选中：indeterminate。
  - 全未选：unchecked。
- 表头全选/全不选只处理当前 `filteredItems`。
- 复选框变更后调用现有刷新路径，至少刷新：设计树、属性工作区、顶部 action bar、统计区域。

### 6. 与设计树联动

优先复用现有设计树勾选逻辑，避免两套状态不同步。

如果现有函数可以直接用，优先调用：

- `toggleNodeCheck(...)`
- `findNodesByGroupKey(...)`
- `getNodeGroupKey(...)`
- `isSubmittableNode(...)`
- `renderDesignTree()`
- `renderPropertyWorkbench()`
- `renderActionBar()`
- `renderStatusbar()` / `updateOverviewBottomStats()`

如果没有现成“设置 group 勾选状态”的函数，新增一个小函数，例如：

```javascript
function setWorkspaceItemChecked(item, checked) { ... }
function setVisibleWorkspaceItemsChecked(items, checked) { ... }
```

实现要点：

- `checked=true`：把该 group 下所有 `isSubmittableNode(node)` 的 node id 加入 `state.checkedNodeIds`。
- `checked=false`：把该 group 下所有对应 node id 从 `state.checkedNodeIds` 删除。
- 对没有稳定 groupKey 的单节点，至少处理当前 `item.nodeIds`。
- 更新后持久化/刷新必须沿用当前项目已有方式；不要新建不必要的全局缓存。

## 样式要求

在 `styles.css` 中增加或调整：

- `.prop-filter-header`
- `.prop-filter-active`
- `.prop-filter-mark`
- `.prop-filter-popover`
- `.prop-filter-options`
- `.prop-filter-actions`
- `.prop-filter-toolbar`
- `.prop-check-col`
- `.prop-row-check`

视觉要求：

- 表头筛选按钮要像字段名的一部分，不要像一排突兀按钮。
- 已筛选列有明显但克制的状态，例如蓝色小点/浅蓝底/加粗标记。
- 复选框列窄列固定宽度，不能挤压文件名称列太多。
- 文件路径列仍保持当前省略/tooltip 行为。
- 表格横向滚动、窄窗口下不能明显破版。

## 验收清单

开发 agent 必须逐条验证，并在最终回复写明证据。

### 功能验收

1. 选择多个零件后，属性工作区显示复选框列 + 所有原字段列。
2. 每个字段表头都能打开筛选浮层。
3. `文件名称` 可筛选。
4. `文件类型` 可筛选。
5. `实例数` 可筛选。
6. `文件路径` 可筛选。
7. `文件大小` 可筛选。
8. `物料编码` 可筛选。
9. `物料名称` 可筛选。
10. `规格型号` 可筛选。
11. `材质` 可筛选。
12. `表面处理` 可筛选。
13. `设计人` 可筛选。
14. `物料状态` 可筛选。
15. 同一列多选值是 OR。
16. 多列筛选叠加是 AND。
17. 空值可以通过 `(空白)` 筛选。
18. 清空单列筛选后该列恢复。
19. 清空全部筛选后恢复全部已选零件。
20. 筛选无结果时显示友好空状态，不是白屏或 JS 报错。
21. 表头已筛选状态清晰可见。
22. 属性工作区行取消勾选后，左侧设计树对应节点同步取消。
23. 属性工作区行重新勾选后，左侧设计树对应节点同步勾选。
24. 表头全选/全不选只影响当前筛选后的可见行。
25. 点击行复选框不会误触发行选择；点击行非复选框区域仍能选择/联动设计树。
26. 左侧设计树勾选变化后，属性工作区内容和复选框状态同步。
27. 属性审核按钮仍按当前勾选集合计算，取消勾选后的零件不进入审核任务。

### 回归验收

1. 页面能正常打开 `任务编排`。
2. 左侧设计树筛选仍可用。
3. `读取属性` 按钮仍可触发原流程。
4. `属性检查`、`属性审核`、`AI 对话`、`任务队列`区域不因本次修改报错。
5. 浏览器控制台或 WebView2 调试中无新增 JS runtime error。
6. 样式不破坏现有布局；表格在多列情况下可横向滚动。

## 测试要求：必须反复检查并修复

开发 agent 不得只改代码不测。至少执行以下检查；失败必须自行修改并重跑，直到通过或明确证明失败与本任务无关。

### 1. 静态语法检查

```powershell
cd D:\MechPilot\Cockpit
node --check .\src\SwAgentAddin\frontend\property-workbench\app.js
```

如果本机没有 `node`，用可用 JS 解析器替代；若完全不可用，必须说明并加强浏览器/WebView2 实测。

### 2. 文本完整性检查

```powershell
Select-String -Path .\src\SwAgentAddin\frontend\property-workbench\app.js -Pattern "propertyWorkbenchFilters|prop-filter-header|prop-row-check|prop-visible-check-all"
Select-String -Path .\src\SwAgentAddin\frontend\property-workbench\styles.css -Pattern "prop-filter|prop-check-col"
```

### 3. 构建

优先直接构建项目：

```powershell
cd D:\MechPilot\Cockpit
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build .\src\SwAgentAddin\SwAgentAddin.csproj -c Release -v:minimal
```

如果 `SW_HOME` 路径不存在，先查找本机 SolidWorks interop 位置，再设置正确 `SW_HOME`。可参考 `deploy\scripts\build.bat` 的路径探测逻辑。

也可以运行：

```powershell
cd D:\MechPilot\Cockpit\deploy\scripts
.\build.bat
```

### 4. 部署前确认 SolidWorks 状态

部署前必须确认 SolidWorks 是否运行；如果 DLL 或前端文件被占用，关闭 SolidWorks 后继续。可使用现有 MCP/SolidWorks 工具或 PowerShell：

```powershell
Get-Process SLDWORKS -ErrorAction SilentlyContinue
```

若需要关闭 SolidWorks，先保存用户工作风险提示；在自动化环境中可使用已有 `sw_force_close` 能力，但必须确保不是静默丢弃用户未保存工作。

### 5. 部署

本次涉及前端资源，不能只运行 `quick_update.bat`，因为 `quick_update.bat` 主要更新 DLL 和图标，未覆盖 `frontend\property-workbench`。

推荐部署方式 A：完整部署（需要管理员权限）

```powershell
cd D:\MechPilot\Cockpit\deploy
.\setup_deploy.bat
```

如果需要管理员权限/UAC，开发 agent 应提交授权请求并继续完成部署。

推荐部署方式 B：构建后手动刷新前端资源 + 必要时注册 DLL

```powershell
cd D:\MechPilot\Cockpit
xcopy /Y /E /I .\deploy\frontend\property-workbench D:\SWAgentAddin\frontend\property-workbench
```

如果本次只改前端，且 `D:\SWAgentAddin\SwAgentAddin.dll` 已存在并正常注册，手动刷新前端资源即可；但仍要验证 SolidWorks 重启后加载的是目标目录资源。

### 6. 部署结果验证

必须验证部署目标文件时间戳或哈希：

```powershell
Get-Item D:\SWAgentAddin\frontend\property-workbench\app.js, D:\SWAgentAddin\frontend\property-workbench\styles.css | Select-Object FullName,Length,LastWriteTime
Get-FileHash D:\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\app.js
Get-FileHash D:\SWAgentAddin\frontend\property-workbench\app.js
Get-FileHash D:\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\styles.css
Get-FileHash D:\SWAgentAddin\frontend\property-workbench\styles.css
```

源文件与部署文件 hash 必须一致。

### 7. SolidWorks / Cockpit 实测

部署后启动或重启 SolidWorks 2022，打开 MechPilot Cockpit，进入 `任务编排` 页，执行功能验收清单。

可用检查：

```powershell
Get-Process SLDWORKS -ErrorAction SilentlyContinue
```

如可用 MCP 工具，优先使用：

- `sw_start`
- `sw_get_status`
- `sw_window_control`

需要在真实 UI 中至少验证：

- 表头筛选浮层打开/确定/清空。
- 多列筛选叠加。
- 属性工作区复选框取消后左侧设计树取消。
- 左侧设计树重新勾选后属性工作区恢复。

## 交付要求

最终交付时必须包含：

1. Git 存档 commit hash。
2. 修改文件列表。
3. 关键实现说明：筛选状态、筛选逻辑、复选框联动逻辑。
4. 已执行测试命令和结果。
5. 部署方式和部署结果证据：目标路径、时间戳/hash、SolidWorks/Cockpit 实测结果。
6. 如果有授权请求，说明请求内容、原因和结果。
7. 如果有未解决问题，说明原因、影响范围、回滚方式；但原则上必须自行修复并部署完成后再交付。

## 给开发 agent 的直接执行 Prompt

你现在负责在 `D:\MechPilot\Cockpit` 仓库一次性交付“属性工作区每字段筛选 + 文件名称前复选框与设计树联动”。

请严格执行：

1. 先 `cd D:\MechPilot\Cockpit`，查看 `git status --short`，然后 `git add -A && git commit -m "archive: before property workbench field filters"` 做存档；不要丢弃任何现有改动。若 Git 身份缺失，用仓库级 `git config user.name "MechPilot Dev Agent"` 和 `git config user.email "dev-agent@mechpilot.local"` 后重试。
2. 主要修改 `D:\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\app.js` 和 `D:\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\styles.css`。
3. 在属性工作区每个字段表头增加筛选入口，覆盖 `文件名称、文件类型、实例数、文件路径、文件大小、物料编码、物料名称、规格型号、材质、表面处理、设计人、物料状态`。
4. 实现同列多值 OR、多列 AND、`(空白)` 筛选、单列清空、全部清空、筛选状态标记、筛选无结果空状态。
5. 在 `文件名称` 前增加复选框列；行复选框和左侧设计树 `state.checkedNodeIds` 双向联动；表头复选框只全选/全不选当前筛选后的可见行；复选框点击不要触发行选择。
6. 复用 `resolvePropValue`、`getWorkspaceItems`、`getNodeGroupKey`、`findNodesByGroupKey`、`isSubmittableNode` 等现有逻辑，不重写页面，不改后端。
7. 反复自测并修复：至少运行 `node --check .\src\SwAgentAddin\frontend\property-workbench\app.js`、文本完整性检查、`dotnet build .\src\SwAgentAddin\SwAgentAddin.csproj -c Release -v:minimal`。失败就改，改完重跑。
8. 构建通过后必须部署。由于本次改前端，不能只依赖 `quick_update.bat`；优先以管理员权限运行 `D:\MechPilot\Cockpit\deploy\setup_deploy.bat`，或至少把 `D:\MechPilot\Cockpit\deploy\frontend\property-workbench` 完整覆盖到 `D:\SWAgentAddin\frontend\property-workbench`。遇到管理员权限、UAC、COM 注册、SolidWorks 重启等授权，自己判断风险后提交授权请求并继续。
9. 部署后用 hash 验证源文件和 `D:\SWAgentAddin\frontend\property-workbench\app.js`、`styles.css` 一致，再启动/重启 SolidWorks，进入 Cockpit `任务编排` 页做真实 UI 验收。
10. 最终回复必须给出存档 commit、修改文件、测试结果、部署证据、真实 UI 验收结果。除非遇到不可恢复的外部阻塞，否则不要在部署前结束任务。
