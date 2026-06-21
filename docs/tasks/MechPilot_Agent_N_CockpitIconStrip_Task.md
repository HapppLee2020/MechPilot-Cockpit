# Agent N Task: Agent驾驶舱 7 图标条带集成

你只负责新版 7 图标条带集成。不要做 WebView2，不要做 cockpit 前端，不要改属性读取逻辑。

## 目标

为新增的 `Agent驾驶舱` 按钮集成独立图标，并把 CommandManager 切换到 7 图标条带。

## 新图标资产

Codex 已生成新版 7 图标条带，位置：

`F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\MechPilot-icon-assets`

文件：

- `mechpilot-icons-7-20.bmp`
- `mechpilot-icons-7-32.bmp`
- `mechpilot-icons-7-20.png`
- `mechpilot-icons-7-32.png`
- `mechpilot-icons-7-preview.png`

预览：

`F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\MechPilot-icon-assets\mechpilot-icons-7-preview.png`

## 图标顺序

必须严格匹配 ImageListIndex：

| Index | 按钮 | 图标语义 |
|------:|------|----------|
| 0 | `Agent驾驶舱` | 现代驾驶舱/仪表盘/Agent 节点 |
| 1 | `属性填写` | 文档 + 铅笔 + 加号 |
| 2 | `读取属性` | 文档 + 放大镜 |
| 3 | `属性检查` | 文档 + 勾选 |
| 4 | `图纸审核` | 蓝图 + 放大镜 |
| 5 | `任务面板` | 面板/任务列表 |
| 6 | `插件设置` | 齿轮 |

## 修改文件

- `output\SW-Agent-Addin\SwAgentAddin.cs`
- `output\SW-Agent-Addin\SwAgentAddin.csproj`
- `output\SW-Agent-Addin\README.md`
- `output\SW-Agent-Addin\HANDOFF_2026-06-18.md`

## 复制资产

复制：

```text
output\MechPilot-icon-assets\mechpilot-icons-7-20.bmp
output\MechPilot-icon-assets\mechpilot-icons-7-32.bmp
```

到：

```text
output\SW-Agent-Addin\mechpilot-icons-7-20.bmp
output\SW-Agent-Addin\mechpilot-icons-7-32.bmp
```

## csproj

加入：

```xml
<None Include="mechpilot-icons-7-20.bmp" CopyToOutputDirectory="PreserveNewest" />
<None Include="mechpilot-icons-7-32.bmp" CopyToOutputDirectory="PreserveNewest" />
```

## C# 图标引用

修改 `ApplyCommandGroupIcons()`：

```csharp
cmdGroup.SmallIconList = Path.Combine(dir, "mechpilot-icons-7-20.bmp");
cmdGroup.LargeIconList = Path.Combine(dir, "mechpilot-icons-7-32.bmp");
```

修改 `EnsureIconFiles()` 检查 7 图标文件：

```csharp
"mechpilot-icons-7-20.bmp"
"mechpilot-icons-7-32.bmp"
```

## CommandManager 按钮索引

如果 Agent J 已新增 `Agent驾驶舱` 按钮，请确保索引：

```text
Agent驾驶舱 -> 0
属性填写 -> 1
读取属性 -> 2
属性检查 -> 3
图纸审核 -> 4
任务面板 -> 5
插件设置 -> 6
```

如果 Agent J 尚未合并，先只集成 7 图标文件和 `.csproj`，并在交付中说明需要与 Agent J 合并后调整 ImageListIndex。

## 兼容复制

部署到 `D:\SWAgentAddin` 时建议：

- 保留旧 `mechpilot-icons-6-*` 文件。
- 新增 `mechpilot-icons-7-*` 文件。
- 不要删除旧图标，避免回滚困难。

## 验收

构建：

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build output\SW-Agent-Addin\SwAgentAddin.csproj -c Release
```

预期：

```text
0 个警告
0 个错误
```

文件验证：

```powershell
Get-ChildItem output\SW-Agent-Addin\deploy -Filter "mechpilot-icons-7-*.bmp"
```

必须看到两个 BMP。

实机验证：

- SolidWorks CommandManager 显示 `Agent驾驶舱` 图标。
- 7 个按钮图标和文字对应正确。

## 交付总结

必须说明：

- 新图标源路径。
- 复制到源码和 deploy 的文件路径。
- ImageListIndex 对应关系。
- 是否完成实机验证。
