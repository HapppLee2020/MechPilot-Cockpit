# Agent A Task: MechPilot Local Mode SolidWorks Property Updates

你负责实现 MechPilot 的 **本地模式 Local Mode**。目标是让插件在 SolidWorks 中直接驱动自定义属性更新，先形成可演示的真实效果。

## 你的边界

你主要改：

- `output\SW-Agent-Addin\SwAgentAddin.cs`

必要时少量改：

- `output\SW-Agent-Addin\taskpane.html`
- `output\SW-Agent-Addin\README.md`

尽量不要改：

- `install_tabicons_v2.bat`
- COM GUID
- `.csproj` 的程序集身份
- 远程 Server 协议细节

## 背景约束

这个插件之前出现过“SolidWorks 插件列表能显示但勾选不上”。不要破坏稳定加载路径：

- 保留 `[Guid("E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1")]`
- 保留 `public class SwAgentAddin : SolidWorks.Interop.swpublished.ISwAddin`
- 保留 `[ClassInterface(ClassInterfaceType.AutoDual)]`
- 不引入 `SolidWorksTools.dll`
- `ConnectToSW` 中 UI 或 Demo 功能失败只能记日志，不要导致返回 `false`

## 功能目标

当配置为本地模式时：

```json
{
  "execution_mode": "local"
}
```

点击 `Property Fill` 后：

1. 如果当前打开的是零件：更新当前零件自定义属性。
2. 如果当前打开的是装配体且没有选中组件：更新装配体文档自定义属性。
3. 如果当前打开的是装配体且选中 1 个组件：更新该组件对应模型文档的自定义属性。
4. 如果当前打开的是装配体且选中多个组件：弹出确认表单，列出组件，确认后逐个更新。

第一版 Demo 属性来自规则配置，不要写死在执行逻辑里。可以先消费 Agent B 提供的 `LocalPropertyRules` 数据结构；如果 Agent B 还没合并，先用一个很薄的临时读取函数，后续再对齐。

## 推荐代码结构

在 `SwAgentAddin.cs` 中增加这些内部类或方法，先不要拆多文件，降低 COM 插件构建风险：

- `ResolvedTarget`
  - `DisplayName`
  - `FilePath`
  - `DocType`
  - `IModelDoc2 Model`
  - `IComponent2 Component`
- `TargetResolver`
  - `ResolveTargets(ISldWorks swApp)`
  - 使用 `SelectionMgr` 判断装配体选择集
- `PropertyUpdatePlan`
  - `ResolvedTarget Target`
  - `Dictionary<string, string> Properties`
- `PropertyWriter`
  - `WriteProperties(PropertyUpdatePlan plan)`
  - 使用 `IModelDoc2.Extension.CustomPropertyManager("")`
- `LocalExecutionResult`
  - `Succeeded`
  - `Failed`
  - `Messages`
  - `ChangedProperties`

## 关键实现细节

### 1. 分流本地/远程模式

修改 `ExecuteTask(string taskType, string displayName)`：

```csharp
if (string.Equals(_config.ExecutionMode, "local", StringComparison.OrdinalIgnoreCase))
{
    ExecuteLocalTask(taskType, displayName);
    return;
}

ExecuteRemoteTask(taskType, displayName);
```

把现有 POST 逻辑移动到 `ExecuteRemoteTask`，不要删除。

### 2. 目标解析

零件：

- ActiveDoc 是 `swDocPART`
- 目标就是 active model

装配体无选择：

- ActiveDoc 是 `swDocASSEMBLY`
- 目标就是 active assembly model

装配体单选/多选：

- 从 `ISelectionMgr` 获取选择对象
- 只处理能解析为 `IComponent2` 的选择
- `component.GetModelDoc2()` 为空时，记录失败并跳过
- 多选时显示 Windows Forms 确认框，至少列出组件名和路径

### 3. 写属性

使用空配置名写入文档级自定义属性：

```csharp
CustomPropertyManager mgr = model.Extension.CustomPropertyManager("");
mgr.Add3(name, (int)swCustomInfoType_e.swCustomInfoText, value,
    (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
model.ForceRebuild3(false);
model.SetSaveFlag();
```

注意：如果枚举名在当前 interop 中不可用，用编译可通过的等价值，但要注释说明。

### 4. Demo 变量替换

支持最少这些占位符：

- `{file_name}`
- `{file_name_no_ext}`
- `{doc_type}`
- `{date}`
- `{engineer_id}`
- `{mode}`

示例输出：

- `物料名称 = bracket_left`
- `图号 = bracket_left`
- `处理状态 = MechPilot Local Demo`

### 5. 结果展示

第一版可以用 `SafeMessage` 汇总：

```text
MechPilot local update completed.

Targets: 3
Succeeded: 3
Failed: 0
Changed:
- 物料名称
- 图号
- 处理状态
```

如果有时间，再同步写入 TaskPane HTML 的状态区域。

## 验收

必须完成：

```powershell
dotnet build output\SW-Agent-Addin\SwAgentAddin.csproj -c Release
```

实机验证：

- 打开一个 `.SLDPRT`，点击 `Property Fill`，文件属性中能看到 Demo 属性。
- 打开一个 `.SLDASM`，不选组件，点击 `Property Fill`，装配体属性被更新。
- 打开一个 `.SLDASM`，选中一个组件，点击 `Property Fill`，该组件文件属性被更新。
- 选中多个组件时有确认步骤，不是静默批量写入。

交付说明必须写清：

- 改了哪些文件。
- 本地模式写入了哪些属性。
- 哪些行为仍是 Demo。
- 有没有做真实 SolidWorks 冒烟。
