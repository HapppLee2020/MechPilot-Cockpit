# Agent O Task: Agent驾驶舱执行 M 前阻塞修复

你负责修复当前执行 Agent M 集成验收前的硬阻塞。不要新增大功能，目标是让工程重新达到可构建、可部署、可进入 M 的状态。

## 当前状态

已有明显进展：

- Agent I 合同与配置基本落地：
  - `cockpit-contracts\context.schema.json`
  - `cockpit-contracts\command.schema.json`
  - `cockpit-contracts\result.schema.json`
  - `config.json` 已含 `cockpit_enabled`、`cockpit_entry` 等字段
- Agent K 前端原型已存在：
  - `property-workbench\index.html`
  - `property-workbench\app.js`
  - `property-workbench\styles.css`
  - `property-workbench\mock-data.js`
- Agent N 7 图标已存在并进入 deploy：
  - `mechpilot-icons-7-20.bmp`
  - `mechpilot-icons-7-32.bmp`
- Agent J 部分落地：
  - CommandManager 中已有 `Agent驾驶舱`
  - `Microsoft.Web.WebView2` 包引用已加入

但当前不能执行 M，因为构建失败。

## 硬阻塞 1：构建失败，缺少 CockpitForm

当前构建错误：

```text
error CS0246: 未能找到类型或命名空间名“CockpitForm”
```

`SwAgentAddin.cs` 中已经引用：

```csharp
private CockpitForm _cockpitForm;
_cockpitForm = new CockpitForm(_config, BuildCockpitContext, HandleCockpitCommand);
```

但工程中没有 `CockpitForm` 类型。

你必须二选一：

### 推荐方案 A：实现 CockpitForm

在 `SwAgentAddin.cs` 末尾或新文件中实现：

```csharp
public class CockpitForm : Form
```

要求：

- 包含 `Microsoft.Web.WebView2.WinForms.WebView2`
- 构造函数匹配当前调用：

```csharp
public CockpitForm(
    AddinConfig config,
    Func<CockpitContext> contextProvider,
    Func<CockpitCommandEnvelope, CockpitResultEnvelope> commandHandler)
```

- 加载本地 HTML：

```text
D:\SWAgentAddin\property-workbench\index.html
```

或源码/deploy 同目录：

```csharp
Path.Combine(SwAgentAddin.GetAddinDirectory(), config.CockpitEntry)
```

- 页面加载后调用：

```javascript
window.MechPilot.receiveContext(...)
```

- 监听 WebView2 消息：

```csharp
CoreWebView2.WebMessageReceived
```

- WebView2 初始化失败时抛出可读中文错误，不能影响插件加载。

### 备选方案 B：临时移除 CockpitForm 引用

不推荐。只有在 WebView2 无法短期实现时才用。这样不能进入真正 M，只能恢复构建。

## 硬阻塞 2：缺少 BuildCockpitContext / HandleCockpitCommand

当前代码调用了：

```csharp
BuildCockpitContext
HandleCockpitCommand
```

但 `rg` 没找到方法定义。

必须实现：

```csharp
private CockpitContext BuildCockpitContext()
private CockpitResultEnvelope HandleCockpitCommand(CockpitCommandEnvelope command)
```

第一版允许返回简化上下文，但必须包含：

- schema version
- client
- active document
- capabilities
- property table
- assembly tree 可为空但字段存在

`HandleCockpitCommand` 第一版至少支持：

- `cockpit.ping`
- `refresh_context`
- `local.read_properties`

未实现命令返回：

```json
{ "success": false, "error": { "code": "not_implemented", "message": "暂未实现" } }
```

## 硬阻塞 3：property-workbench 没有复制到 deploy

当前：

```powershell
Test-Path output\SW-Agent-Addin\deploy\property-workbench
```

结果是 `False`。

必须修改 `.csproj`：

```xml
<None Include="property-workbench\**\*.*" CopyToOutputDirectory="PreserveNewest" />
```

构建后必须看到：

```text
output\SW-Agent-Addin\deploy\property-workbench\index.html
output\SW-Agent-Addin\deploy\property-workbench\app.js
output\SW-Agent-Addin\deploy\property-workbench\styles.css
output\SW-Agent-Addin\deploy\property-workbench\mock-data.js
```

## 阻塞 4：WebView2 包版本 warning

当前 restore 警告：

```text
NU1603: Microsoft.Web.WebView2 1.0.2526.35 未找到，解析为 1.0.2535.41
```

请将 `.csproj` 中版本改为实际可解析版本：

```xml
<PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2535.41" />
```

目标：构建 0 警告 0 错误。

## 验收标准

必须执行：

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build output\SW-Agent-Addin\SwAgentAddin.csproj -c Release
```

必须结果：

```text
0 个警告
0 个错误
```

文件验证：

```powershell
Test-Path output\SW-Agent-Addin\deploy\property-workbench\index.html
Test-Path output\SW-Agent-Addin\deploy\cockpit-contracts\context.schema.json
Test-Path output\SW-Agent-Addin\deploy\config.json
```

都必须为 `True`。

代码验证：

```powershell
Select-String output\SW-Agent-Addin\SwAgentAddin.cs -Pattern "class CockpitForm","BuildCockpitContext","HandleCockpitCommand"
```

三者都必须存在。

## 什么时候可以执行 Agent M

只有当本任务全部通过后，才能执行 Agent M。

## 交付总结必须包含

- 构建结果。
- 是否实现 `CockpitForm`。
- 是否实现 `BuildCockpitContext`。
- 是否实现 `HandleCockpitCommand`。
- `property-workbench` 是否已复制到 deploy。
- WebView2 包版本。
- 是否达到执行 Agent M 条件：是/否。
