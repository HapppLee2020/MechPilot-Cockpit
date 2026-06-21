# Agent D Task: MechPilot 中文界面与新版功能图标集成

你负责 MechPilot 插件的两个收尾任务：

1. 集成新版功能图标，解决当前 SolidWorks 工具栏图标过小、内容不符合功能的问题。
2. 将所有用户可见界面、按钮、弹窗、提示、TaskPane 文案改为中文。

本轮不要改本地属性写入逻辑，不要改 COM GUID，不要改 SolidWorks 加载契约。

## 当前实机状态

刚刚本地模式测试项已全部通过：

- 插件加载通过。
- `execution_mode=local` 生效。
- 打开零件后可更新零件自定义属性。
- 打开装配体且未选组件时可更新装配体属性。
- 装配体中选中单个零部件时可更新该零部件属性。
- 选中多个零部件时会弹出确认框。

所以本轮重点是体验收口，不要破坏已通过链路。

## 新图标资产

新版图标已经由 Codex 设计并保存到：

`F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\MechPilot-icon-assets`

文件清单：

- `mechpilot-main-20.bmp`
- `mechpilot-main-32.bmp`
- `mechpilot-icons-20.bmp`
- `mechpilot-icons-32.bmp`
- `mechpilot-main-20.png`
- `mechpilot-main-32.png`
- `mechpilot-icons-20.png`
- `mechpilot-icons-32.png`
- `mechpilot-icons-preview.png`

图标条带顺序必须和当前命令 ImageListIndex 保持一致：

| Index | 当前命令 | 中文名称 | 图标含义 |
|------:|----------|----------|----------|
| 0 | `Property Fill` | 属性填写 | 文档 + 铅笔 + 加号 |
| 1 | `Property Check` | 属性检查 | 文档 + 绿色勾选 |
| 2 | `Drawing Review` | 图纸审核 | 蓝图 + 放大镜 |
| 3 | `Task Panel` | 任务面板 | 面板/任务列表 |
| 4 | `Settings` | 设置 | 齿轮 |

## 必须修改的文件

主要文件：

- `output\SW-Agent-Addin\SwAgentAddin.cs`
- `output\SW-Agent-Addin\taskpane.html`
- `output\SW-Agent-Addin\SwAgentAddin.csproj`
- `output\SW-Agent-Addin\README.md`
- `output\SW-Agent-Addin\HANDOFF_2026-06-18.md`

需要复制到源码目录的新资产：

- `output\SW-Agent-Addin\mechpilot-main-20.bmp`
- `output\SW-Agent-Addin\mechpilot-main-32.bmp`
- `output\SW-Agent-Addin\mechpilot-icons-20.bmp`
- `output\SW-Agent-Addin\mechpilot-icons-32.bmp`

是否保留 PNG 预览文件可自行决定，推荐复制到：

- `output\SW-Agent-Addin\assets\mechpilot-icons-preview.png`

## 图标集成要求

当前代码中 `EnsureIconFiles()` 会调用：

```csharp
CreateIconStrip(Path.Combine(dir, "agent-icons-20.bmp"), 20);
CreateIconStrip(Path.Combine(dir, "agent-icons-32.bmp"), 32);
CreateSingleIcon(Path.Combine(dir, "agent-main-20.bmp"), 20, 0);
CreateSingleIcon(Path.Combine(dir, "agent-main-32.bmp"), 32, 0);
```

并且 `CreateIconStrip/CreateSingleIcon` 只有文件不存在时才生成旧图标。因此你必须处理这个问题，否则 SolidWorks 仍可能继续加载旧图标。

推荐方案：

1. 把新版 BMP 复制进 `output\SW-Agent-Addin`。
2. 修改 `ApplyCommandGroupIcons()` 使用新文件名：

```csharp
cmdGroup.SmallMainIcon = Path.Combine(dir, "mechpilot-main-20.bmp");
cmdGroup.LargeMainIcon = Path.Combine(dir, "mechpilot-main-32.bmp");
cmdGroup.SmallIconList = Path.Combine(dir, "mechpilot-icons-20.bmp");
cmdGroup.LargeIconList = Path.Combine(dir, "mechpilot-icons-32.bmp");
```

3. 修改 `EnsureIconFiles()`：
   - 不再运行旧的 `DrawIcon()` 自动生成逻辑作为主路径。
   - 只检查新版文件是否存在。
   - 如果新版文件缺失，写日志并允许插件继续加载，不要让 `ConnectToSW` 失败。
4. `.csproj` 中增加 4 个 BMP 的 `CopyToOutputDirectory=PreserveNewest`。
5. 构建后确认 deploy 目录里有：

```text
output\SW-Agent-Addin\deploy\mechpilot-main-20.bmp
output\SW-Agent-Addin\deploy\mechpilot-main-32.bmp
output\SW-Agent-Addin\deploy\mechpilot-icons-20.bmp
output\SW-Agent-Addin\deploy\mechpilot-icons-32.bmp
```

6. 部署到 `D:\SWAgentAddin` 时也要复制这 4 个新版 BMP。

可选兼容方案：

- 同时把新版文件复制为旧文件名 `agent-main-20.bmp` 等，避免 SolidWorks 缓存或旧代码路径仍引用旧名。
- 如果采用兼容复制，请在交付总结中写清楚。

## 中文化要求

所有用户可见文本都改成中文。包括但不限于：

### CommandManager 按钮

| 英文 | 中文 |
|------|------|
| `Property Fill` | `属性填写` |
| `Property Check` | `属性检查` |
| `Drawing Review` | `图纸审核` |
| `Task Panel` | `任务面板` |
| `Settings` | `设置` |

Command hint / tooltip 也必须中文化：

- `Fill properties (...)` -> `按当前模式填写自定义属性`
- `Submit property check task` -> `检查当前模型自定义属性`
- `Submit drawing review task` -> `提交当前工程图审核`
- `Open MechPilot task panel` -> `打开 MechPilot 任务面板`
- `Open config.json` -> `打开配置文件`

### 弹窗提示

把这些英文提示改成中文：

- `No valid target found...`
- `About to update ... targets... Proceed?`
- `No property rules found...`
- `MechPilot local update completed...`
- `Targets / Succeeded / Failed / Changed / Details`
- `Please open and save a SolidWorks document...`
- `submitted successfully`
- `failed`
- `Server returned`
- `connection error`
- `Add-in load error`
- `Task panel error`

推荐中文风格：

```text
未找到可更新目标。请先打开并保存一个 SolidWorks 文档；如果在装配体中操作，可选择一个或多个零部件。
```

```text
即将更新 3 个目标：

- 零件A [part]
- 零件B [part]

是否继续？
```

```text
MechPilot 本地更新完成。

目标数量：3
成功：3
失败：0
已更新属性：
- 物料名称
- 图号
- 处理状态
```

### TaskPane HTML

`taskpane.html` 和 `TaskpaneHtml.DefaultHtml` 都要中文化。

推荐文案：

- 标题：`MechPilot`
- 状态：`插件已加载`
- 提示：`可通过上方 MechPilot 工具栏执行属性填写、属性检查和图纸审核。`
- 操作：`操作`
- `属性填写`
- `属性检查`
- `图纸审核`
- 配置：`配置`
- `本地模式会直接写入当前 SolidWorks 文件属性；远程模式会提交任务到 Agent Server。`

注意：当前代码里同时存在外部 `taskpane.html` 和内置 `TaskpaneHtml.DefaultHtml`，两处都要改。

### README/HANDOFF

README 和 HANDOFF 至少补充：

- 当前插件界面已中文化。
- 新版图标文件位置。
- 本地模式测试已通过。
- 若图标没有刷新，建议关闭 SolidWorks 后重新复制 BMP，再启动 SolidWorks。

## 验证要求

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
Get-ChildItem output\SW-Agent-Addin\deploy -Filter *.bmp
```

必须看到新版 `mechpilot-*` BMP 文件。

实机验证：

1. 关闭 SolidWorks。
2. 复制新版 DLL、config、rules、taskpane 和 4 个 BMP 到 `D:\SWAgentAddin`。
3. 重新启动 SolidWorks。
4. 工具 -> 插件 -> 勾选 `MechPilot`。
5. 确认 CommandManager 按钮文字为中文。
6. 确认 5 个图标分别对应属性填写、属性检查、图纸审核、任务面板、设置。
7. 点 `属性填写`，确认弹窗和结果摘要为中文。
8. 再次确认本地模式原有测试项仍通过。

## 交付总结必须包含

- 构建结果。
- 修改文件清单。
- 新图标文件最终部署位置。
- 是否保留旧 `agent-*` 文件名兼容。
- 中文化覆盖范围。
- 实机验证结果。
- 若未验证 SolidWorks，必须明确写“实机验证待确认”。
