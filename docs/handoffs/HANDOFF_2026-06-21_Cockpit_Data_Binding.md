# MechPilot Agent 驾驶舱数据绑定修复交接

生成时间：2026-06-21 23:40  
当前任务状态：已完成一次热修复部署，仍需要在真实 SolidWorks UI 中做最终验收。

## 1. 用户问题

用户反馈：

> SW 中 addin 插件已经能够打开 MechPilot Agent 驾驶舱了，但是里面的内容没有啊，都还是预设的一些值。

也就是说：

- SolidWorks Add-in 已经能加载。
- 命令栏/按钮已经能打开 `MechPilot Agent驾驶舱`。
- 驾驶舱页面能显示，但内容仍像 mock / demo / 预设数据，没有显示当前 SolidWorks 装配体的真实信息。

## 2. 环境与关键路径

运行部署目录：

- `D:\SWAgentAddin`

源码/构建目录：

- `F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin`

关键运行文件：

- `D:\SWAgentAddin\SwAgentAddin.dll`
- `D:\SWAgentAddin\property-workbench\index.html`
- `D:\SWAgentAddin\property-workbench\app.js`
- `D:\SWAgentAddin\property-workbench\mock-data.js`
- `D:\SWAgentAddin\addin-load.log`

关键源码文件：

- `F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin\SwAgentAddin.cs`
- `F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin\property-workbench\app.js`

## 3. 已确认的事实

从 `D:\SWAgentAddin\addin-load.log` 看到，SolidWorks 侧已经采集到了真实上下文，不是空数据：

```text
CockpitForm: navigating to file:///D:/SWAgentAddin/property-workbench/index.html
BuildCockpitAssemblyTree: 2516 nodes depth≤1 in 37 ms
BuildCockpitContext: type=assembly title=613三夹爪上模块.SLDASM rows=1416 treeNodes=59 warnings=1 elapsed=61ms
CockpitForm: context injected (2781515 chars)
```

结论：

- Add-in 能连接 SolidWorks。
- 当前活动装配体能被读取。
- C# 端能构建 cockpit context。
- WebView2 页面也触发了 context 注入。
- 问题集中在“注入后的真实数据如何被前端识别和渲染”。

## 4. 根因判断

本次发现两个主要问题。

### 4.1 C# 注入方式不理想

原代码在 `SwAgentAddin.cs` 的 `CockpitForm.OnNavigationCompleted` 中使用：

```csharp
string escaped = EscapeForJsInjection(contextJson);
string script = "if (window.MechPilot && window.MechPilot.receiveContext) { window.MechPilot.receiveContext('" + escaped + "'); }";
_webView.CoreWebView2.ExecuteScriptAsync(script);
```

这会把完整 JSON 当成字符串传给前端。

但前端 `receiveContext(context)` 原本直接把 `context` 当对象用：

```js
state.context = context;
renderAll();
```

所以真实数据虽然注入了，但前端收到的可能是字符串，不是对象。

### 4.2 前后端 cockpit context 字段契约不一致

前端 mock 数据结构类似：

```js
{
  fileName,
  filePath,
  mode,
  status,
  propertyDefs,
  tree
}
```

但 C# 真实数据由 .NET `JavaScriptSerializer` 生成，字段是：

```json
{
  "SchemaVersion": "...",
  "TimestampUtc": "...",
  "Client": {...},
  "ActiveDocument": {...},
  "AssemblyTree": [...],
  "PropertyTable": {
    "DynamicColumns": [...],
    "Rows": [...]
  },
  "Summary": {...},
  "Warnings": [...]
}
```

前端渲染函数主要读取：

- `context.tree`
- `context.propertyDefs`
- `context.fileName`
- `context.filePath`

真实 context 里没有这些小写原型字段，所以页面会继续表现得像 mock / 预设值。

## 5. 本次已完成的修复

### 5.1 前端增加 normalizeContext 适配层

已修改：

- 源码：`F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin\property-workbench\app.js`
- 运行目录：`D:\SWAgentAddin\property-workbench\app.js`

新增能力：

- 如果 `receiveContext` 收到字符串，先 `JSON.parse`。
- 如果收到的是原型 mock 结构，保持原样。
- 如果收到的是 C# 真实结构，把：
  - `ActiveDocument` 转成 `fileName` / `filePath`
  - `Client.ExecutionMode` 转成 `mode`
  - `PropertyTable.DynamicColumns` 转成 `propertyDefs`
  - `AssemblyTree` + `PropertyTable.Rows` 转成前端需要的 `tree`
- 若装配树为空，退化使用 `PropertyTable.Rows` 构造平铺树。

运行目录已验证存在：

```text
D:\SWAgentAddin\property-workbench\app.js
Length: 31458
LastWriteTime: 2026/6/21 23:31:04
```

并做过语法检查：

```text
node --check D:\SWAgentAddin\property-workbench\app.js
```

结果：无语法错误输出。

### 5.2 C# 注入方式改成直接传对象

已修改：

- 源码：`F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin\SwAgentAddin.cs`
- 运行 DLL：`D:\SWAgentAddin\SwAgentAddin.dll`

修改后逻辑：

```csharp
string contextJson = _buildContext?.Invoke() ?? "{}";
string script = "if (window.MechPilot && window.MechPilot.receiveContext) { window.MechPilot.receiveContext(" + contextJson + "); }";
_webView.CoreWebView2.ExecuteScriptAsync(script).ContinueWith(task =>
{
    if (task.IsFaulted && task.Exception != null)
        SwAgentAddin.WriteTrace("CockpitForm: context injection script failed: " + task.Exception.GetBaseException().Message);
});
SwAgentAddin.WriteTrace("CockpitForm: context injected (" + contextJson.Length + " chars)");
```

同时增加了脚本执行失败时的 trace，后续如果 WebView2 注入失败，可以直接看日志，不需要靠猜。

构建命令：

```powershell
dotnet build "F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin\SwAgentAddin.csproj" -c Release
```

构建结果：

```text
SwAgentAddin -> F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin\deploy\SwAgentAddin.dll
已成功生成。
0 个警告
0 个错误
```

已复制到运行目录：

```text
D:\SWAgentAddin\SwAgentAddin.dll
Length: 104960
LastWriteTime: 2026/6/21 23:31:38
```

## 6. 当前未完成/需下一个 Agent 继续验证

虽然文件已部署，但当前 SolidWorks 进程如果已经加载旧 DLL，内存中可能仍是旧版本。下一个 Agent 应要求用户执行以下其中一种：

1. 先关闭驾驶舱窗口，再从 SW 插件按钮重新打开驾驶舱。
2. 如果仍显示旧内容，完全关闭 SolidWorks 后重新打开。
3. 打开目标装配体，再点击 `MechPilot Agent驾驶舱`。

然后查看页面是否显示真实文档：

- 顶部文件名应为当前 SW 文档，例如 `613三夹爪上模块.SLDASM`。
- 状态 badge 应显示 `真实数据`。
- 表格行数不应是 mock 里的少量演示行，应接近日志里的 `rows=1416` 或由装配树映射出的真实行。
- 左侧树应显示当前装配体组件，而不是 mock 示例装配。

## 7. 建议的下一步验收清单

### 7.1 UI 验收

- 关闭旧驾驶舱窗口，重新打开。
- 确认顶部文件名、路径为当前 SW 文件。
- 确认左侧树有真实组件名。
- 确认右侧表格有真实属性列。
- 切换 `解析值/原始值`，确认属性值随之变化。
- 搜索一个真实组件名，确认能过滤。
- 点击树节点，确认表格高亮联动。

### 7.2 日志验收

查看：

```text
D:\SWAgentAddin\addin-load.log
```

应看到：

```text
BuildCockpitContext: type=assembly ...
CockpitForm: context injected (...)
```

不应出现：

```text
CockpitForm: context injection script failed
CockpitForm: context injection failed
```

### 7.3 如果仍显示 mock 数据

优先检查：

1. `D:\SWAgentAddin\property-workbench\app.js` 是否是新版本，搜索：

```text
function normalizeContext
```

2. 是否 WebView2 缓存了旧脚本。可以尝试：

```powershell
Remove-Item -LiteralPath "D:\SWAgentAddin\cockpit-cache\EBWebView" -Recurse -Force
```

注意：执行前先关闭 SolidWorks 和驾驶舱窗口。

3. `index.html` 是否仍加载 mock：

```html
<script src="mock-data.js"></script>
<script src="app.js"></script>
```

这个目前不是致命问题，因为 `receiveContext` 会覆盖 mock。若后续想更干净，可在 WebView2 模式下改成不加载 `mock-data.js`，或通过 URL 参数控制。

## 8. 后续建议优化

### 8.1 统一正式 cockpit schema

当前是前端兼容后端真实结构。长期更好的是定义一个明确的 `mechpilot.cockpit.context.v1` JSON 契约，并让前后端都以它为准。

建议文档位置：

```text
D:\SWAgentAddin\cockpit-contracts
```

或源码目录：

```text
F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin\cockpit-contracts
```

### 8.2 去掉生产环境 mock 默认加载

当前 `index.html` 仍加载：

```html
<script src="mock-data.js"></script>
```

建议后续改为：

- 本地浏览器独立预览时加载 mock。
- WebView2/SolidWorks 模式默认不加载 mock。
- 若 2 秒内没有真实 context，再显示“等待数据注入”或“未连接 SolidWorks”。

### 8.3 增加前端调试状态

建议在页面状态栏显示：

- `mock` / `real` 数据源
- context schema version
- row count
- tree node count
- last injected time

这样用户一眼能知道当前是否仍是预设数据。

## 9. 本次修改文件清单

源码侧：

- `F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin\SwAgentAddin.cs`
- `F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\SW-Agent-Addin\property-workbench\app.js`

部署侧：

- `D:\SWAgentAddin\SwAgentAddin.dll`
- `D:\SWAgentAddin\property-workbench\app.js`

## 10. 给下一个 Agent 的一句话判断

这次不是“插件没读到 SW 数据”，而是“真实 cockpit context 已经注入，但前端仍按 mock 数据结构渲染”。已通过前端 normalizeContext 和 C# 对象注入做了热修复；下一步重点是在真实 SolidWorks 中重开驾驶舱验收，并处理可能的 WebView2 缓存或旧 DLL 进程占用问题。
