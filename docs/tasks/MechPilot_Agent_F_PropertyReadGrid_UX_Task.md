# Agent F Task: 读取属性表格体验与配置化改造

你负责改造 MechPilot 的 **读取属性** 表单展示方式和配置能力。当前实现已经能读取属性，但展示模型不符合工程师使用习惯：现在是“一条属性一行”的长表，应该改成“一个零部件一行、属性名作为列”的清单表。

本任务不要破坏已经通过的属性读取与属性写入主链路。

## 当前问题

用户反馈：

1. 属性展示方式不对。当前表头是固定字段：目标名称、属性名、属性值等，每个属性一行。应改为：每一行是一个零部件/目标对象，每一列是一个属性。
2. 表头显示不完整，表头字体需要加粗。
3. 多个零部件属性显示时要增加间隔区分，类似斑马纹。
4. 文件路径列往后放。
5. 插件设置要支持配置默认读取的属性清单，不应永远读取/显示全部属性。
6. 默认显示解析值，增加“显示属性值”开关，放在右上角。
7. 需要显示当前打开文件名称。
8. 窗体大小调整为当前 1.5 倍，或根据内容自适应。
9. 每一列属性名需要筛选功能：下拉内容为当前列所有数据集合，支持多选。

## 主要修改文件

- `output\SW-Agent-Addin\SwAgentAddin.cs`
- `output\SW-Agent-Addin\config.json`
- `output\SW-Agent-Addin\README.md`
- `output\SW-Agent-Addin\HANDOFF_2026-06-18.md`

如需说明配置，可同步更新：

- `output\SW-Agent-Addin\rules.local.json`

## 当前相关代码位置

在 `SwAgentAddin.cs` 中重点查看：

- `ExecuteReadProperties()`
- `ReadModelProperties(...)`
- `ReadDrawingProperties(...)`
- `ReadAssemblyAllComponents(...)`
- `ShowPropertyReadForm(...)`
- `PropertyReadRow`
- `AddinConfig`

当前 `ShowPropertyReadForm(...)` 大约在 `SwAgentAddin.cs` 第 1014 行附近，当前问题就在这里：

```csharp
grid.Columns.Add("TargetName", "目标名称");
grid.Columns.Add("LocalComponentName", "本地零部件名称");
grid.Columns.Add("FilePath", "文件路径");
grid.Columns.Add("ConfigurationName", "配置");
grid.Columns.Add("PropertyName", "属性名");
grid.Columns.Add("RawValue", "属性值");
grid.Columns.Add("ResolvedValue", "解析值");
grid.Columns.Add("Source", "来源");
grid.Columns.Add("Quantity", "数量");
```

这个结构要改，不要继续让 `PropertyName` 作为一列。

## 目标展示模型

将读取结果从长表：

| 目标名称 | 属性名 | 解析值 |
|----------|--------|--------|
| PartA | 物料名称 | 支架 |
| PartA | 图号 | A001 |
| PartB | 物料名称 | 螺母 |

改为透视表：

| 目标名称 | 本地零部件名称 | 数量 | 配置 | 物料名称 | 图号 | 材料 | 重量 | 文件路径 |
|----------|----------------|------|------|----------|------|------|------|----------|
| PartA | PartA-1 | 2 | 默认 | 支架 | A001 | Q235 | 1.2 | D:\... |
| PartB | PartB-1 | 8 | 默认 | 螺母 | B001 | 304 | 0.1 | D:\... |

注意：

- 每个目标对象/合并项一行。
- 属性名成为动态列。
- 文件路径放在靠后位置。
- 默认显示解析值。
- 如果开启“显示属性值”，则显示原始属性值。可以选择：
  - 直接把属性列内容切换为 RawValue；
  - 或新增隐藏/显示的 `属性值:xxx` 列。
- 推荐第一版使用“同一属性列内容在解析值/属性值之间切换”，界面更清爽。

## 配置能力

在 `config.json` 中新增配置：

```json
{
  "read_property_names": [
    "物料名称",
    "图号",
    "材料",
    "重量",
    "表面处理",
    "处理状态",
    "处理人",
    "处理日期"
  ],
  "read_show_all_properties": false,
  "read_show_raw_value_default": false,
  "read_form_width": 1500,
  "read_form_height": 900
}
```

语义：

- `read_property_names`：默认展示的属性清单和列顺序。
- `read_show_all_properties=false`：只展示 `read_property_names` 中配置的属性。
- `read_show_all_properties=true`：展示当前读取到的所有属性，属性列顺序为：配置清单优先，其余属性按名称排序追加。
- `read_show_raw_value_default=false`：默认显示解析值。
- `read_form_width/read_form_height`：读取属性窗体默认尺寸。

要求：

- 旧配置缺字段时必须安全默认。
- 配置字段写入 `AddinConfig.Save(...)`。
- README 说明这些字段。

## 表单布局要求

窗口标题：

```text
MechPilot - 读取属性
```

窗体尺寸：

- 当前约 `1000 x 600`。
- 本轮改为默认 `1500 x 900`。
- 同时限制不能超过当前屏幕工作区，例如最大为屏幕工作区的 90%。
- `StartPosition = CenterScreen`。

顶部区域：

左侧显示摘要：

```text
当前文件：xxx.SLDASM　|　文档类型：assembly　|　目标数量：12　|　属性列：8　|　零部件总数量：36
```

右上角放开关：

```text
[ ] 显示属性值
```

说明：

- 默认不勾选，表格显示解析值。
- 勾选后表格切换为原始属性值。
- 开关状态变化后刷新表格内容，但不重新读取 SolidWorks。

主体：

- `DataGridView`
- 表头字体加粗。
- 表头可换行或自动增高，避免显示不完整。
- 行高略增大，例如 26-30。
- `AlternatingRowsDefaultCellStyle` 设置斑马纹。
- `AutoSizeColumnsMode` 不要简单用 `AllCells` 导致窗口横向爆炸。建议：
  - 目标名称、本地零部件名称、配置、数量使用合理宽度。
  - 属性列默认宽度 120-160，可调整。
  - 文件路径列放最后，宽度 280-420。
  - 支持横向滚动。

底部：

- `关闭` 按钮。
- 可选：`复制表格` 按钮，第一版非必须。

## 数据结构建议

保留 `PropertyReadRow` 作为读取原始结果，不要破坏读取逻辑。

新增一个透视后的结构：

```csharp
public class PropertyReadPivotRow
{
    public string TargetKey { get; set; }
    public string TargetName { get; set; }
    public string LocalComponentName { get; set; }
    public string FilePath { get; set; }
    public string ConfigurationName { get; set; }
    public string Source { get; set; }
    public int Quantity { get; set; }
    public Dictionary<string, PropertyValuePair> Properties { get; set; }
}

public class PropertyValuePair
{
    public string RawValue { get; set; }
    public string ResolvedValue { get; set; }
}
```

Pivot key 推荐：

```text
FilePath + "|" + ConfigurationName + "|" + LocalComponentName
```

注意：

- 装配体无选择全量读取已经做了合并同类项和数量统计，不能在透视时把数量弄丢。
- 如果同一目标同一属性出现多次，以最后一次为准，并写日志。

## 属性列生成规则

步骤：

1. 从所有 `PropertyReadRow` 收集完整属性名集合。
2. 从 `_config.ReadPropertyNames` 读取默认列顺序。
3. 如果 `_config.ReadShowAllProperties == false`：
   - 只生成配置清单里的属性列。
   - 即使某个属性当前没有值，也保留列，方便工程师看缺失。
4. 如果 `_config.ReadShowAllProperties == true`：
   - 先生成配置清单里的属性列。
   - 再追加实际读取到但不在配置清单里的属性，按中文/字符串排序即可。
5. 固定列顺序推荐：

```text
目标名称
本地零部件名称
数量
配置
来源
动态属性列...
文件路径
```

文件路径必须靠后。

## “显示属性值”开关

新增右上角 `CheckBox`：

```text
显示属性值
```

默认：

- 读取 `_config.ReadShowRawValueDefault`
- 默认 `false`
- false 显示 `ResolvedValue`
- true 显示 `RawValue`

实现要求：

- 表单初始化时先构建 pivot 数据。
- `CheckBox.CheckedChanged` 时调用 `RefreshGrid(useRawValue)`。
- 不要重新访问 SolidWorks API。

## 筛选功能要求

用户要求：每一列属性名增加筛选功能，下拉内容为当前所有数据的集合，支持多选。

WinForms `DataGridView` 没有原生 Excel 式多选筛选。第一版允许自己实现一个轻量版本。

推荐实现：

- 在表格上方增加一行筛选栏，而不是硬改 DataGridView 表头。
- 对每个动态属性列创建一个小按钮或 ComboBox，显示 `筛选: 属性名`。
- 点击后弹出 `CheckedListBox` 小窗体，内容为该属性列所有非空值集合，外加 `(空白)`。
- 支持多选。
- 点击确定后，刷新 DataGridView，只保留满足所有列筛选条件的行。
- 支持清除筛选。

如果时间不够，分阶段：

阶段 1 必须完成：

- 透视表。
- 配置默认属性清单。
- 默认解析值 + 显示属性值开关。
- 当前文件名。
- 窗体变大。
- 表头加粗、斑马纹、文件路径后置。

阶段 2 再做：

- 多选筛选。

如果阶段 2 没完成，交付总结必须明确“筛选未完成”，并保留接口/结构以便下一轮实现。

## 验收标准

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

实机验证：

1. 打开零件，点击 `读取属性`。
   - 表格每行是当前零件。
   - 属性名是列。
   - 默认显示解析值。
   - 顶部显示当前文件名。
2. 打开装配体，选中多个零部件，点击 `读取属性`。
   - 每个零部件一行。
   - 斑马纹可见。
   - 文件路径在最后。
3. 打开装配体，不选对象，点击 `读取属性`。
   - 合并同类项后每类一行。
   - 数量列正确。
4. 修改 `config.json` 的 `read_property_names`。
   - 重启插件或 SolidWorks 后，表格列按配置显示。
5. 勾选 `显示属性值`。
   - 动态属性列从解析值切换为原始属性值。
6. 如果实现了筛选：
   - 对某个属性列打开筛选。
   - 多选 1-2 个值。
   - 表格只显示匹配行。
   - 清除筛选后恢复全部。

## 回归要求

必须回归：

- `属性填写` 仍然能写入本地属性。
- `读取属性` 的零件、工程图主视图、装配体选中、装配体无选择场景仍可打开表单。
- `插件设置` 仍可打开配置文件。

## 交付总结必须包含

- 构建结果。
- 修改文件清单。
- 新增配置字段说明。
- 表格是否已改为“零部件一行、属性名为列”。
- `显示属性值` 开关是否完成。
- 当前文件名显示是否完成。
- 窗体尺寸策略。
- 筛选功能是否完成；如果未完成，说明下一步最短路径。
- 实机验证结果。
