# Agent J Task: 新增 Agent驾驶舱按钮与 WebView2 宿主

你负责在 SolidWorks 插件中新增 `Agent驾驶舱` 按钮，并打开 WebView2 cockpit 窗口。不要重写前端 UI，不要重写属性采集逻辑。

## 目标

新增一个战略入口：

```text
Agent驾驶舱
```

点击后打开 WebView2 窗口，加载本地 `property-workbench/index.html` 或开发 URL。

## 修改文件

- `output\SW-Agent-Addin\SwAgentAddin.cs`
- `output\SW-Agent-Addin\SwAgentAddin.csproj`
- `output\SW-Agent-Addin\README.md`

## 新按钮

命令顺序建议：

| Index | 按钮 |
|------:|------|
| 0 | Agent驾驶舱 |
| 1 | 属性填写 |
| 2 | 读取属性 |
| 3 | 属性检查 |
| 4 | 图纸审核 |
| 5 | 任务面板 |
| 6 | 插件设置 |

如果图标条带暂时只有 6 个，第一版允许 `Agent驾驶舱` 复用主图标或复用第 0 个图标，但必须记录为后续图标任务。

新增回调：

```csharp
public void SwCmd_OpenCockpit()
```

## WebView2 依赖

添加 NuGet 包：

```xml
<PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2526.35" />
```

如版本不可用，使用当前 NuGet 可恢复的稳定版本，并在交付说明写清。

注意：

- .NET Framework 4.8 WinForms 支持 WebView2。
- 如果 WebView2 Runtime 缺失，弹中文提示，不得导致插件加载失败。
- Cockpit 打开失败时允许 fallback 到当前 WinForms `读取属性` 或提示用户安装 WebView2 Runtime。

## Cockpit 窗口

新增 `CockpitForm` 或内部方法：

- 标题：`MechPilot Agent驾驶舱`
- 默认接近全屏，最大化优先。
- 包含 `Microsoft.Web.WebView2.WinForms.WebView2`。
- 加载：
  - `cockpit_url_mode=local`：`D:\SWAgentAddin\property-workbench\index.html`
  - `cockpit_url_mode=dev`：`cockpit_dev_url`

## C# -> JS 初始化

页面加载完成后，调用 JS：

```javascript
window.MechPilot?.receiveContext(contextJson)
```

C# 侧第一版可发送 mock context，后续由 Agent L 接真实数据。

## JS -> C# 命令桥

WebView2 监听：

```csharp
webView.CoreWebView2.WebMessageReceived += ...
```

接收 `CockpitCommandEnvelope` JSON。

第一版支持：

- `cockpit.ping`
- `local.read_properties`

如果命令未实现，返回中文错误 result。

## 不能破坏

- `ConnectToSW` 不能因为 WebView2 不可用返回 false。
- 现有 `属性填写`、`读取属性`、`插件设置` 必须保留。
- COM GUID 不变。

## 验收

- 构建 0 警告 0 错误。
- SolidWorks 中出现 `Agent驾驶舱` 按钮。
- 点击按钮打开 WebView2 窗口。
- 本地 HTML 可以加载。
- 页面能显示 C# 传来的 context。
- WebView2 不可用时给中文提示且插件仍可用。

## 交付总结

说明：

- WebView2 包版本。
- 新按钮 Index。
- 本地/开发 URL 加载策略。
- 已支持的 JS 命令。
- 实机验证结果。
