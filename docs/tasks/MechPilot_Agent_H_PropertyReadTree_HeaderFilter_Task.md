# Agent H Task: 读取属性表头筛选、真实装配树、左右联动与窗口体验修复

你负责修复 MechPilot 读取属性表单当前暴露出的 8 个体验问题，并结合代码层面的已知缺陷做一次收口。

本轮不要重写属性读取主链路，重点改 UI、数据映射和装配树。

## 当前用户反馈

1. 筛选是否能做到表头字段名称上，而不是额外一排按钮。
2. 装配树关闭后无法二次显示。
3. 整体窗口大小改成现在的 1.5 倍大。
4. 装配树要显示真实层级：子装配下的零部件应该是下一层级，现在没有实现。
5. 不要底部“关闭”按钮，右上角窗口关闭即可。
6. 零部件名称只显示文档名称，不要显示上一阶装配体名字或链式组件路径。
7. 设计树和表格需要联动：
   - 选中左侧树节点，右侧对应行也被选中。
   - 选中右侧表格行，左侧设计树对应节点也被选中。
8. 结合代码层面问题一起处理。

## 代码层面已知问题

当前 `SwAgentAddin.cs` 中：

- `ShowPropertyReadForm(...)` 约在第 1014 行。
- 筛选现在由 `filterPanel` + `Button("筛选：列名")` 实现，不在表头。
- `chkTree` 在 `split.Panel1` 内部；当 `split.Panel1Collapsed = true` 后，复选框也一起消失，所以无法二次显示。
- `BuildAssemblyTree(TreeView tree, List<PropertyReadPivotRow> pivotRows)` 用 pivot 行直接挂到根节点，是扁平树，不是真实装配层级。
- `GetIntrinsicPartName(...)` 优先用 `LocalComponentName`，而读取时 `comp.Name2` 可能是链式名，例如 `零件-1/子装配-1` 或带装配层级，用户不希望这样显示。
- `btnClose` 仍然存在：`var btnClose = new Button { Text = "关闭"... }`。
- `AutoFitPropertyGridColumns(...)` 只在现有列宽基础上扩展，最大化时可能不够稳定。

## 主要修改文件

- `output\SW-Agent-Addin\SwAgentAddin.cs`
- `output\SW-Agent-Addin\config.json`
- `output\SW-Agent-Addin\README.md`
- `output\SW-Agent-Addin\HANDOFF_2026-06-18.md`

## 任务 1：表头筛选

目标：筛选入口放到表头字段名称上。

WinForms `DataGridView` 没有原生 Excel 表头多选筛选。推荐实现方式：

### 推荐方案 A：表头右侧小箭头/标记 + HeaderMouseClick

1. 移除或隐藏当前 `filterPanel`。
2. 在表头文本中显示筛选提示：

```text
材料 ▼
材料 ●
```

- `▼` 表示可筛选。
- `●` 或红色表头表示当前列有筛选。

3. 绑定 `grid.ColumnHeaderMouseClick`。
4. 用户点击某列表头时，弹出该列的 `CheckedListBox` 多选筛选窗。
5. 筛选窗复用当前逻辑：
   - 全选
   - 清空
   - 确定
   - 取消
   - `(空白)`
6. 多列筛选 AND，同列多值 OR。
7. 筛选状态变化后刷新表格，并更新表头标记。

注意：

- 固有列和自定义属性列都可以筛选。
- 文件路径列也允许筛选，但值很多时可接受。
- 文件大小列也允许筛选。
- 不要使用额外筛选栏作为主入口。

### 备选方案 B：保留隐藏筛选栏，仅用于调试

如果实现表头点击后发现 WinForms 事件冲突，可以保留 `filterPanel.Visible=false`，但用户入口必须是表头点击。

验收：

- 点击 `材料` 表头弹出筛选窗。
- 筛选后表头显示已筛选状态。
- 再点表头可以修改筛选。
- 清空筛选后恢复全部。

## 任务 2：装配树关闭后可再次显示

当前问题：

`显示装配树` 复选框在被折叠的 Panel1 内部，所以关闭后控件一起消失。

要求：

- 把 `显示装配树` 开关放到顶部 `topPanel` 或表格上方工具条，不能放在可折叠的左侧 Panel 内。
- 开关始终可见。
- 取消勾选时：
  - `split.Panel1Collapsed = true`
- 再次勾选时：
  - `split.Panel1Collapsed = false`
  - 左侧树恢复显示

验收：

- 关闭装配树后，可以再次勾选显示。

## 任务 3：窗口尺寸再放大 1.5 倍

当前默认约 `1800 x 1000`。用户说“整体窗口大小改成现在的 1.5 倍大”。

因为很多屏幕无法容纳 2700x1500，实际策略：

- 配置默认可以设为：

```json
"read_form_width": 2700,
"read_form_height": 1500
```

- 运行时最大不超过屏幕工作区 `98%`。
- 打开时优先使用最大可用尺寸。
- 可考虑默认最大化：

```csharp
form.WindowState = FormWindowState.Maximized;
```

推荐：

- 如果屏幕工作区小于配置尺寸，直接最大化或使用 98%。
- 保留 `MinimumSize = 1200x700`。

验收：

- 表单打开明显比当前更大。
- 在常规显示器上尽可能接近全屏。

## 任务 4：真实装配层级树

当前 `BuildAssemblyTree(...)` 是扁平树，必须改成真实层级。

要求：

- 子装配体节点下显示其子零部件。
- 零件不要都直接挂在根节点下。
- 设计树节点应尽量对应 SolidWorks 装配结构。

实现建议：

1. 在读取装配体无选择时，不要只保留合并后的 pivot 行；同时收集组件层级。
2. 新增数据结构：

```csharp
public class AssemblyTreeNodeData
{
    public string NodeId { get; set; }
    public string DisplayName { get; set; }
    public string FilePath { get; set; }
    public string ConfigurationName { get; set; }
    public string DocType { get; set; }
    public string PivotKey { get; set; }
    public IComponent2 Component { get; set; }
    public List<AssemblyTreeNodeData> Children { get; set; }
}
```

3. 构建树时优先使用 `IComponent2.GetChildren()` 递归。
4. 根节点使用当前装配体文件名。
5. 对轻量化、抑制、未加载组件：
   - 不崩溃。
   - 节点可显示 `[未加载]` 或跳过，并写日志。

注意：

- 表格仍然可以是合并同类项后的清单。
- 树是实例层级，可能一个零件出现多个节点，但它们可以映射到同一个 pivot 行。

验收：

- 子装配体下面能看到下一层零部件。
- 普通零件挂在其直接父装配体下。
- 不是所有节点都平铺在根节点下。

## 任务 5：左右联动

目标：

- 选中左侧树节点，右侧表格选中对应行。
- 选中右侧表格行，左侧设计树选中对应节点。

实现建议：

### 映射关系

为每个 `PropertyReadPivotRow` 保留稳定 key：

```text
PivotKey = FilePath + "|" + ConfigurationName
```

树节点 `Tag` 放 `AssemblyTreeNodeData`，其中包含 `PivotKey`。

维护映射：

```csharp
Dictionary<string, int> pivotKeyToGridRowIndex;
Dictionary<string, List<TreeNode>> pivotKeyToTreeNodes;
```

注意：

- 同一个零件多次实例可能对应多个 TreeNode，但合并成一个表格行。
- 从表格选树时，选中第一个对应 TreeNode，并展开父节点。

### 防止事件互相递归

使用标志位：

```csharp
bool syncingSelection = false;
```

### 事件

- `treeView.AfterSelect`
- `grid.SelectionChanged`

验收：

- 点树节点，右侧对应行高亮。
- 点表格行，左侧对应节点高亮并展开。
- 不发生闪烁/死循环。

## 任务 6：零部件名称只显示文档名

当前可能显示链式组件名。改为只显示文档名称。

规则：

1. 如果有 `FilePath`：

```csharp
Path.GetFileNameWithoutExtension(FilePath)
```

2. 如果没有 `FilePath`，再使用 `TargetName` 或 `LocalComponentName`，但需要清理链式路径：

```text
xxx/yyy/zzz -> zzz
xxx\\yyy\\zzz -> zzz
```

3. 可以保留实例后缀，如 `零件-1`，但不要包含上级装配体路径。

建议新方法：

```csharp
private string GetDisplayDocumentName(PropertyReadPivotRow p)
private string CleanComponentDisplayName(string name)
```

替换当前 `GetIntrinsicPartName(...)`。

验收：

- 表格中的 `零部件名称` 不显示上一阶装配体。
- 树节点可以显示实例名，但也不应显示链式全路径。

## 任务 7：删除底部关闭按钮

当前有：

```csharp
var btnClose = new Button { Text = "关闭"... };
```

要求：

- 删除底部按钮和 `btnPanel`。
- 只使用窗口右上角关闭。
- 释放底部空间给表格。

验收：

- 表单底部没有“关闭”按钮。

## 任务 8：结合部署配置问题

之前发现过 `D:\SWAgentAddin\config.json` 未同步新字段的问题。

本轮交付时必须：

- 构建成功。
- 确认 `output\SW-Agent-Addin\deploy\config.json` 是新版配置。
- 部署到 `D:\SWAgentAddin\config.json` 后确认包含：

```json
"read_show_assembly_tree_default"
"read_enable_column_filters"
"read_auto_fit_columns"
```

如果 `.csproj` 没有复制 `config.json`，请补上：

```xml
<None Include="config.json" CopyToOutputDirectory="PreserveNewest" />
```

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

1. 打开读取属性表单。
   - 窗口比当前明显更大，接近全屏。
   - 没有底部“关闭”按钮。
2. 筛选。
   - 点击表头字段名即可筛选。
   - 不依赖额外筛选按钮栏。
   - 筛选后表头有状态标识。
3. 装配树开关。
   - 关闭后还能再次打开。
4. 装配树层级。
   - 子装配体下显示下一层级零部件。
   - 不是扁平列表。
5. 左右联动。
   - 点树节点，表格行选中。
   - 点表格行，树节点选中。
6. 零部件名称。
   - 只显示文档名，不显示上级装配体链式路径。
7. 回归。
   - `属性填写` 仍正常。
   - `读取属性` 零件、工程图、装配体选中、装配体无选择仍正常。
   - `插件设置` 仍打开配置文件。

## 交付总结必须包含

- 构建结果。
- 修改文件清单。
- 表头筛选实现方式。
- 装配树层级实现方式。
- 左右联动实现方式。
- 窗口尺寸策略。
- 是否删除底部关闭按钮。
- `config.json` 是否已同步到 deploy 和 `D:\SWAgentAddin`。
- 实机验证结果。
