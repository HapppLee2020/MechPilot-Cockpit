# Agent U Prompt：AICockpit 最大化不遮挡 Windows 任务栏

任务书地址：

`E:\0B 软件开发\MechPilot\Cockpit\docs\tasks\MechPilot_Agent_U_Cockpit_Maximize_TaskbarFix_Task.md`

## 背景

用户反馈：点击 AICockpit（Agent 驾驶舱）顶栏 **最大化** 按钮后，窗口铺满屏幕，**挡住 Windows 任务栏**。

这是 WinForms **无边框窗口**（`FormBorderStyle.None`）+ `WindowState = FormWindowState.Maximized` 的经典问题：系统最大化用的是 **Monitor 全屏区域**，不是 **WorkingArea**（已扣除任务栏/停靠栏）。

本任务只做 **窗口几何修复**，不顺带改 AICockpit 业务逻辑。

## 重要路径

工程根目录：

`E:\0B 软件开发\MechPilot\Cockpit`

主代码（C# 窗口宿主）：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.cs`

- 类：`CockpitForm`（约 `#region CockpitForm`）
- 入口：`SwCmd_Cockpit()` 中也有 `WindowState = FormWindowState.Maximized`

前端（仅了解，通常不必改）：

`E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\frontend\property-workbench\app.js`

- 顶栏按钮发送：`window_maximize`（`ToggleMaximizeRestore` 触发）

运行部署：

`D:\SWAgentAddin`

日志：

`D:\SWAgentAddin\addin-load.log`

## 根因分析（请先读代码再改）

当前实现要点：

```csharp
// CockpitForm 构造函数
FormBorderStyle = FormBorderStyle.None;
WindowState = FormWindowState.Maximized;   // ← 初次打开即全屏，可能盖住任务栏

// ToggleMaximizeRestore()
_normalBounds = Bounds;
WindowState = FormWindowState.Maximized;   // ← 再次最大化同样问题
```

还原分支已部分使用 `Screen.FromControl(this).WorkingArea` 计算居中尺寸，但 **最大化分支未使用 WorkingArea**。

CSS `#app { height: 100vh; }` 只影响 WebView2 内部布局；**任务栏被挡主要是 WinForms 窗体 Bounds 问题**。优先修 C#，不要先改 CSS。

## 任务目标

修复后应满足：

1. 初次打开驾驶舱（默认最大化/大窗）时，窗口 **下边缘不超过任务栏上沿**。
2. 点击顶栏 **最大化** 后，窗口贴合 **当前屏幕 WorkingArea**，任务栏始终可见、可点击。
3. 点击 **还原** 后，回到合理的 `_normalBounds`（居中、约 1280×800 或上次非最大化尺寸）。
4. **多显示器**：窗口在哪个屏就使用该屏的 `Screen.FromControl(this).WorkingArea`。
5. **DPI 缩放**（125%/150%）下仍不遮挡任务栏。
6. 任务栏在 **顶部/左侧/右侧/自动隐藏** 常见布局下均可用（至少验证底部 + 顶部两种）。

## 推荐实现方案

### 方案 A（推荐）：自定义最大化，不用 `FormWindowState.Maximized`

在 `CockpitForm` 内：

- 增加字段：`bool _isCustomMaximized`、可选 `Rectangle _restoreBounds`
- 新增方法：`ApplyWorkingAreaBounds()` / `EnterCustomMaximize()` / `ExitCustomMaximize()`
- 最大化时：

```csharp
var area = Screen.FromControl(this).WorkingArea;
_restoreBounds = Bounds;          // 或沿用 _normalBounds
Bounds = area;
WindowState = FormWindowState.Normal;  // 保持 Normal，靠 Bounds 贴边
_isCustomMaximized = true;
```

- 还原时：恢复 `_restoreBounds`，`_isCustomMaximized = false`
- **构造函数** 初次展示：调用 `EnterCustomMaximize()` 或 `ApplyWorkingAreaBounds()`，**不要** `WindowState = Maximized`
- `SwCmd_Cockpit()` 里已有窗口时 `BringToFront` + Maximized 的逻辑，改为调用 CockpitForm 的公开方法（如 `EnsureCustomMaximized()`），避免散落 `WindowState.Maximized`

### 方案 B（备选）：Win32 `WM_GETMINMAXINFO`

若坚持用系统最大化，可在 `WndProc` 处理 `WM_GETMINMAXINFO`（0x24），设置 `ptMaxPosition` / `ptMaxSize` 为 WorkingArea。  
无边框窗体 + 自定义 hit-test（已有 `WM_NCHITTEST`）时，方案 A 通常更简单、可测。

### 必须同步调整

| 位置 | 现状 | 应改为 |
|------|------|--------|
| `CockpitForm` 构造函数 | `WindowState = Maximized` | WorkingArea 贴边 |
| `ToggleMaximizeRestore()` 最大化分支 | `WindowState = Maximized` | WorkingArea 贴边 |
| `ToggleMaximizeRestore()` 判断 | `WindowState == Maximized` | `_isCustomMaximized` 或 Bounds 比较 |
| `WndProc` 中 `WindowState != Maximized` | 控制 resize 热区 | 改为 `!_isCustomMaximized` |
| `SwCmd_Cockpit()` 复用窗口 | `WindowState = Maximized` | 调用 CockpitForm 统一方法 |

## 守护约束

- **不要**改 COM GUID、插件加载流程、`ConnectToSW` 行为。
- **不要**移除 WebView2、无边框顶栏拖拽、`WM_NCHITTEST` 标题栏拖动能力。
- **不要**把窗口改成 `FormBorderStyle.Sizable`（会破坏现有自定义 UI）。
- WebView2 / 驾驶舱打开失败 **不能** 导致插件加载失败（保持现有 try/catch）。
- 改动范围控制在 `CockpitForm` + `SwCmd_Cockpit` 相关几行；**不要**重构 ActionRouter / Hermes / LocalToolbelt。

## 构建与部署

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build "E:\0B 软件开发\MechPilot\Cockpit\src\SwAgentAddin\SwAgentAddin.csproj" -c Release
```

要求：**0 错误**。警告若有，须在交付说明中解释。

构建后同步到运行目录（若仓库有 install 脚本则执行，或复制 `deploy\SwAgentAddin.dll` 到 `D:\SWAgentAddin`）。

## 验收标准

### 自动

- [ ] Release 构建 0 错误
- [ ] 代码中 `CockpitForm` 最大化路径 **不再依赖** `WindowState = FormWindowState.Maximized`（或已配合 `WM_GETMINMAXINFO` 正确限制 WorkingArea）

### 实机（SolidWorks + 本机 Windows）

- [ ] 打开 **驾驶舱**：任务栏可见
- [ ] 顶栏 **最大化**：任务栏仍可见、可点击
- [ ] **还原**：窗口回到合理大小，不跑出屏幕
- [ ] **最小化 / 关闭**：行为与现版一致
- [ ] 顶栏 **拖拽**、双击最大化/还原：仍可用
- [ ] 任务栏在 **屏幕底部** 与 **屏幕顶部** 各测一次（若方便）
- [ ] 日志无新增未处理异常

## 交付物

请输出：

1. **修改说明**（根因 + 方案选择）
2. **修改文件清单**（路径 + 改动摘要）
3. **关键代码片段**（最大化/还原逻辑）
4. **实机验收结果**（通过/未测/失败 + 截图说明可选）
5. **剩余风险**（如多屏拖拽跨屏、自动隐藏任务栏等）

## 参考代码位置

| 符号 | 文件 | 说明 |
|------|------|------|
| `class CockpitForm` | `SwAgentAddin.cs` ~3127 行 | 窗口宿主 |
| `ToggleMaximizeRestore()` | ~3391 行 | 最大化/还原 |
| `WndProc` / `WM_NCHITTEST` | ~3215 行 | 无边框拖拽与缩放 |
| `OnWebMessageReceived` | ~3340 行 | `window_maximize` 入口 |
| `SwCmd_Cockpit()` | ~547 行 | 打开/前置驾驶舱 |
| `window_maximize` | `app.js` | 前端按钮 |

## 可选增强（非必须）

- 监听 `Microsoft.Win32.SystemEvents.DisplaySettingsChanged`，显示器/任务栏变化时若处于自定义最大化则重新 `ApplyWorkingAreaBounds()`
- 在 `addin-load.log` 写一行 debug：`CockpitForm: custom maximize bounds=... workingArea=...`

---

**指派说明**：本任务独立于 Agent P/Q/R/S/T，可在当前蓝图批次上 hotfix。完成后可交给 Agent T 补一条回归项：「驾驶舱最大化不遮挡任务栏」。
