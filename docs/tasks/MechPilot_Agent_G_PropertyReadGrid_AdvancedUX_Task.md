# Agent G Task: 读取属性高级表格 UX、固有属性列、筛选与装配树

你负责继续改造 MechPilot 的 **读取属性** 表单。当前版本已经改成透视表，但仍有布局和数据建模问题。本轮要把它推进到更接近工程师实际使用的清单视图。

不要破坏已通过的：

- `属性填写`
- `读取属性` 基础读取
- 6 个中文按钮
- 本地/远程模式配置

## 用户反馈与本轮目标

用户提出的问题：

1. 列宽改为自适应宽度。
2. 弹窗尺寸还可以再大一点。
3. `目标名称` 和 `本地零部件名称` 重复，合并为 `零部件名称`。
4. `来源` 和后面的 `文档类型` 应统一为 `文档类型`。
5. `文件路径` 后面增加 `文件大小`。
6. `零部件名称`、`数量`、`文档类型`、`文件路径`、`文件大小` 是固有属性，不可自定义；后面的自定义属性才由配置控制。
7. `文件路径` 字体缩小，适应单元格大小。
8. 行高增加到现在的 1.5 倍。
9. 全屏显示时自适应调整列宽，填充整个屏幕。
10. 考虑增加列筛选。
11. 最左侧增加装配体设计树展示，可开关。
12. DataGrid 背景不要灰色，使用白底。

## 主要修改文件

- `output\SW-Agent-Addin\SwAgentAddin.cs`
- `output\SW-Agent-Addin\config.json`
- `output\SW-Agent-Addin\README.md`
- `output\SW-Agent-Addin\HANDOFF_2026-06-18.md`

## 当前相关代码位置

重点看 `SwAgentAddin.cs`：

- `ShowPropertyReadForm(...)`
- `PropertyReadRow`
- `PropertyReadPivotRow`
- `AddinConfig`

当前 `ShowPropertyReadForm(...)` 中仍存在：

- `TargetName`
- `LocalComponentName`
- `Source`
- `FilePath`
- 固定列 + 动态属性列混在一起
- `grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None`
- `grid.RowTemplate.Height = 28`
- `grid.AlternatingRowsDefaultCellStyle.BackColor` 为灰色

这些都要调整。

## 数据模型要求

### 固有属性列

固有属性列固定存在，不受 `read_property_names` 控制：

1. `零部件名称`
2. `数量`
3. `文档类型`
4. `文件路径`
5. `文件大小`

说明：

- `零部件名称`：合并原来的 `目标名称` 和 `本地零部件名称`。
  - 优先使用 `LocalComponentName`。
  - 如果为空，使用 `TargetName`。
  - 如果仍为空，使用文件名。
- `文档类型`：使用原来的 `Source` 或 `DocType` 语义，但列名统一为 `文档类型`。
  - 不要再显示 `来源` 列。
- `文件大小`：由 `FileInfo(FilePath).Length` 得到。
  - 建议展示为 `KB` / `MB` 可读格式。
  - 文件不存在、虚拟件、未加载组件时显示空或 `不可用`。
- `文件路径`：固定列，放在靠后位置，排在 `文件大小` 前面或后面均可；用户明确说“文件路径后面增加文件大小”，所以推荐顺序：

```text
零部件名称 | 数量 | 文档类型 | 自定义属性列... | 文件路径 | 文件大小
```

### 自定义属性列

自定义属性列才受配置控制：

- `read_property_names`
- `read_show_all_properties`
- `read_show_raw_value_default`

重要：

- 不要把 `文档类型` 放进默认 `read_property_names`。
- 如果现有 `config.json` 里已经有 `"文档类型"`，读取配置时可以过滤掉，或者 Save 时去掉。
- 固有属性永远显示。

## 配置新增建议

可以新增这些配置字段：

```json
{
  "read_form_width": 1800,
  "read_form_height": 1000,
  "read_show_assembly_tree_default": true,
  "read_enable_column_filters": true,
  "read_auto_fit_columns": true
}
```

语义：

- `read_form_width/read_form_height`：默认更大。
- `read_show_assembly_tree_default`：读取装配体时默认显示左侧树。
- `read_enable_column_filters`：是否启用筛选栏。
- `read_auto_fit_columns`：是否自适应列宽填充可用区域。

旧配置缺字段时安全默认。

## 窗体尺寸要求

当前是 1500x900。本轮改为：

- 默认 1800x1000。
- 最大不超过当前屏幕工作区 96%。
- 支持用户最大化。
- 最大化时列宽重新自适应填充整个可用区域。

建议：

```csharp
form.Width = Math.Min(_config.ReadFormWidth, (int)(screen.Width * 0.96));
form.Height = Math.Min(_config.ReadFormHeight, (int)(screen.Height * 0.96));
form.MinimumSize = new Size(1200, 700);
form.WindowState = FormWindowState.Normal;
```

绑定：

- `form.Resize`
- `grid.SizeChanged`

触发列宽重新计算。

## 列宽自适应要求

不要简单用 `AllCells` 导致大量属性时横向巨大，也不要固定死导致全屏浪费空间。

推荐策略：

1. 固有列设置最小宽度：
   - `零部件名称`：180-260
   - `数量`：70
   - `文档类型`：90
   - `文件路径`：280-500
   - `文件大小`：90
2. 自定义属性列：
   - 最小 110
   - 默认 140-180
   - 根据表头和单元格内容测量后取合理宽度。
3. 如果所有列总宽小于表格可用宽度：
   - 按比例扩展自定义属性列或 `零部件名称` 列，使表格填满。
4. 如果所有列总宽大于表格可用宽度：
   - 保持横向滚动。

第一版可实现一个 `AutoFitPropertyGridColumns(DataGridView grid)` 方法。

## 文件路径列样式

`文件路径` 内容长，要求缩小字体，适应单元格大小。

建议：

- 文件路径列 `DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8f)`
- `DefaultCellStyle.ForeColor = Color.FromArgb(80, 80, 80)`
- `AutoEllipsis` DataGridView 不原生支持；可设置：
  - 宽度较大
  - Tooltip 显示完整路径
- 每个文件路径单元格设置 `ToolTipText = fullPath`。

## 行高与背景

当前行高 28。本轮改为约 1.5 倍：

```csharp
grid.RowTemplate.Height = 42;
```

表格背景：

```csharp
grid.BackgroundColor = Color.White;
grid.DefaultCellStyle.BackColor = Color.White;
```

斑马纹仍可保留，但不要灰得太重：

```csharp
grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 255);
```

如果用户坚持纯白，保留极浅色或提供配置开关；本轮优先用白底+极浅斑马纹。

## 列筛选要求

这次要开始实现列筛选。不要再只写“下一阶段”。

推荐实现：顶部筛选栏。

布局：

- 在摘要 topPanel 下方增加 `filterPanel`。
- `filterPanel` 可横向滚动。
- 每个可筛选列一个按钮：
  - 固有列也允许筛选：零部件名称、文档类型、文件大小。
  - 文件路径可选，建议也支持。
  - 自定义属性列必须支持。
- 按钮文字：

```text
筛选：材料
```

点击按钮：

- 弹出一个小 `Form`。
- 内部 `CheckedListBox`，值来自当前列所有数据集合。
- 包含：
  - `全选`
  - `清空`
  - `确定`
  - `取消`
- 支持 `(空白)`。
- 多选后刷新表格。

筛选逻辑：

- 多列筛选之间是 AND。
- 同一列内多选值是 OR。
- 清除筛选后恢复全部。
- 筛选不能重新读取 SolidWorks，只过滤已有 pivot 数据。

如果时间有限，最低要求：

- 至少实现自定义属性列筛选。
- 固有列筛选可作为后续增强。
- 交付时必须说明覆盖范围。

## 左侧装配体设计树

用户要求：最左侧增加装配体设计树展示，可开关。

目标：

- 当当前读取对象是装配体，表单左侧显示 TreeView。
- 非装配体可以隐藏树。
- 树用于辅助理解装配层级，不要求第一版点击树过滤表格，但建议预留。

布局：

```text
┌──────────────────────────────────────────────┐
│ 顶部摘要 + 显示属性值开关                     │
├──────────┬───────────────────────────────────┤
│ 装配树   │ 属性表格                           │
│ TreeView │ DataGridView                       │
└──────────┴───────────────────────────────────┘
```

建议使用：

- `SplitContainer`
- 左侧宽度 260-360
- 左侧顶部放 CheckBox：`显示装配树`

装配树数据来源：

- 优先复用读取装配体全量组件时的组件集合。
- 节点文本：组件名 + 数量/文档类型可选。
- 子装配体下显示子组件。

阶段要求：

- 第一版必须实现“可显示/隐藏左侧树”的 UI。
- 如果快速构建真实层级树有风险，可以先显示扁平树/一层树，但必须在交付中说明。
- 不能为了树影响属性读取主表。

## 表格构建建议

将 `ShowPropertyReadForm(...)` 拆成小方法，减少继续膨胀：

- `BuildPivotRows(...)`
- `BuildPropertyColumnNames(...)`
- `CreateReadPropertyForm(...)`
- `BuildPropertyGrid(...)`
- `RefreshPropertyGrid(...)`
- `ApplyGridFilters(...)`
- `AutoFitPropertyGridColumns(...)`
- `BuildAssemblyTree(...)`
- `FormatFileSize(...)`
- `GetIntrinsicPartName(...)`

仍可以保留在 `SwAgentAddin.cs` 单文件内，避免本轮修改 `.csproj` 过大。

## 固定列顺序

最终表格列顺序必须是：

```text
零部件名称
数量
文档类型
<自定义属性列，按配置顺序>
文件路径
文件大小
```

不要再出现：

- `目标名称`
- `本地零部件名称`
- `来源`

## 构建验证

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build output\SW-Agent-Addin\SwAgentAddin.csproj -c Release
```

预期：

```text
0 个警告
0 个错误
```

## 实机验收

1. 打开零件，点击 `读取属性`。
   - 固定列为：零部件名称、数量、文档类型、文件路径、文件大小。
   - 自定义属性列来自 `read_property_names`。
   - 不再显示目标名称/本地零部件名称/来源。
2. 打开装配体，无选择，点击 `读取属性`。
   - 左侧可显示装配树。
   - 表格合并同类项并显示数量。
   - 文件大小列有值。
3. 最大化窗口。
   - 列宽自动调整，尽量填满整个屏幕。
   - 横向滚动在列很多时仍可用。
4. 文件路径列。
   - 字体比其他列小。
   - 单元格工具提示显示完整路径。
5. 筛选。
   - 对至少一个自定义属性列打开筛选。
   - 多选值后表格过滤。
   - 清除筛选后恢复。
6. 回归。
   - `属性填写` 仍能写入。
   - `读取属性` 零件、工程图、装配体选中、装配体无选择仍能打开。
   - `插件设置` 仍能打开配置文件。

## 交付总结必须包含

- 构建结果。
- 修改文件清单。
- 固有属性列和自定义属性列边界说明。
- 列宽自适应策略。
- 窗体尺寸策略。
- 文件大小计算规则。
- 筛选功能完成范围。
- 装配树完成范围。
- 实机验证结果。
