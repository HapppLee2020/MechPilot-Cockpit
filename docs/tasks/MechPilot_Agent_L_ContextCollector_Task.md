# Agent L Task: Cockpit 真实 SolidWorks 上下文采集与 JSON 输出

你负责把现有 SolidWorks 属性读取逻辑升级为 cockpit 可用的真实 JSON 上下文。不要做 WebView2 UI，不要做前端。

## 目标

将当前打开的 SolidWorks 文件转换为 `CockpitContext`：

- active document
- selection
- assembly tree
- property table
- intrinsic columns
- dynamic property columns
- capability list

## 修改文件

- `output\SW-Agent-Addin\SwAgentAddin.cs`
- `output\SW-Agent-Addin\README.md`

## 复用现有能力

当前已有：

- `ExecuteReadProperties`
- `ReadModelProperties`
- `ReadDrawingProperties`
- `ReadAssemblyAllComponents`
- `PropertyReadRow`
- `PropertyReadPivotRow`

不要删除这些。新增 cockpit collector，逐步复用。

## 新增方法建议

```csharp
private CockpitContext BuildCockpitContext()
private CockpitDocumentInfo BuildActiveDocumentInfo(IModelDoc2 model)
private CockpitTreeNode BuildCockpitAssemblyTree(IModelDoc2 model)
private CockpitPropertyTable BuildCockpitPropertyTable(List<PropertyReadRow> rows)
private List<CockpitPropertyRow> BuildCockpitRows(...)
private string SerializeCockpitContext(CockpitContext context)
```

## 装配树

必须是真实层级，不是扁平表格行。

优先使用：

- `IAssemblyDoc`
- root components
- `IComponent2.GetChildren()`

节点必须包含：

- `node_id`
- `parent_id`
- `name`
- `doc_type`
- `file_path`
- `configuration`
- `quantity`
- `pivot_key`
- `children`

## 属性表

表格行仍可以合并同类项：

- key: `file_path + configuration`
- quantity: instance count

每行包含：

- intrinsic fields
- `properties` dictionary
- raw/resolved values

## 选择集

采集当前选择：

- selected count
- selected component paths
- selected node/pivot keys if possible

## 性能和稳定性

- 大装配体第一版可以先接受同步读取，但要写日志耗时。
- 对 suppressed/lightweight/unloaded/virtual components 不崩溃。
- 缺失路径显示 `不可用`。
- 所有异常写日志并继续。

## 验收

- 构建 0 警告 0 错误。
- 零件生成 context。
- 工程图生成主视图 context。
- 装配体生成真实 assembly_tree。
- property_table rows 和 assembly_tree 可以通过 `pivot_key` 关联。
- 输出 JSON 可以被 Agent K 前端 mock 替换并渲染。

## 交付总结

说明：

- JSON 字段覆盖情况。
- 装配树层级实现方式。
- 边界组件处理策略。
- 实机验证结果。
