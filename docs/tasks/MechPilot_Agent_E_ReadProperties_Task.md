# Agent E Task: 新增“读取属性”功能、6 图标条带与四字按钮文案

你负责给 MechPilot 插件新增 **读取属性** 功能，并顺手修正按钮文案：`设置` 改为 `插件设置`，使工具栏按钮尽量统一为 4 个字。

本任务是体验和能力扩展任务。不要破坏已经通过的本地模式属性写入能力。

## 当前已验证状态

上轮本地模式测试已通过：

- 插件可以加载。
- `execution_mode=local` 生效。
- 零件中点击 `属性填写` 可写入当前零件自定义属性。
- 装配体无选择时可写入装配体属性。
- 装配体选中单个零部件时可写入该零部件属性。
- 装配体选中多个零部件时有确认弹窗。
- 中文化和新版 5 图标已经完成过一轮。

本轮新增第 6 个按钮：`读取属性`。

## 新图标资产

Codex 已经设计好包含 6 个功能图标的新图标条带，保存位置：

`F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\MechPilot-icon-assets`

新文件：

- `mechpilot-icons-6-20.bmp`
- `mechpilot-icons-6-32.bmp`
- `mechpilot-icons-6-20.png`
- `mechpilot-icons-6-32.png`
- `mechpilot-icons-6-preview.png`

预览图：

`F:\davis\Documents\WPS灵犀\20260617-22-20-29-842\output\MechPilot-icon-assets\mechpilot-icons-6-preview.png`

6 个图标顺序必须和 `ImageListIndex` 一致：

| Index | 按钮中文名 | 功能 | 图标含义 |
|------:|------------|------|----------|
| 0 | 属性填写 | 写入/更新属性 | 文档 + 铅笔 + 加号 |
| 1 | 读取属性 | 读取并展示属性 | 文档 + 放大镜 |
| 2 | 属性检查 | 检查属性完整性 | 文档 + 勾选 |
| 3 | 图纸审核 | 审核工程图 | 蓝图 + 放大镜 |
| 4 | 任务面板 | 打开任务面板 | 面板/任务列表 |
| 5 | 插件设置 | 打开配置文件 | 齿轮 |

## 文案要求

按钮文字统一调整为：

- `属性填写`
- `读取属性`
- `属性检查`
- `图纸审核`
- `任务面板`
- `插件设置`

注意：

- 原 `设置` 必须改成 `插件设置`。
- 新增按钮 `读取属性` 必须插在 `属性填写` 后面。
- 所有提示、弹窗、表单标题使用中文。

## 必须修改的文件

主要文件：

- `output\SW-Agent-Addin\SwAgentAddin.cs`
- `output\SW-Agent-Addin\SwAgentAddin.csproj`
- `output\SW-Agent-Addin\taskpane.html`
- `output\SW-Agent-Addin\README.md`
- `output\SW-Agent-Addin\HANDOFF_2026-06-18.md`

需要复制的新资产：

- 从 `output\MechPilot-icon-assets\mechpilot-icons-6-20.bmp`
- 到 `output\SW-Agent-Addin\mechpilot-icons-6-20.bmp`

- 从 `output\MechPilot-icon-assets\mechpilot-icons-6-32.bmp`
- 到 `output\SW-Agent-Addin\mechpilot-icons-6-32.bmp`

建议保留原主图标：

- `mechpilot-main-20.bmp`
- `mechpilot-main-32.bmp`

## 图标集成要求

当前 5 图标条带已经不够用。本轮必须切换到 6 图标条带。

推荐修改：

```csharp
cmdGroup.SmallIconList = Path.Combine(dir, "mechpilot-icons-6-20.bmp");
cmdGroup.LargeIconList = Path.Combine(dir, "mechpilot-icons-6-32.bmp");
```

`.csproj` 必须添加：

```xml
<None Include="mechpilot-icons-6-20.bmp" CopyToOutputDirectory="PreserveNewest" />
<None Include="mechpilot-icons-6-32.bmp" CopyToOutputDirectory="PreserveNewest" />
```

构建后确认 deploy 目录有：

```text
output\SW-Agent-Addin\deploy\mechpilot-icons-6-20.bmp
output\SW-Agent-Addin\deploy\mechpilot-icons-6-32.bmp
```

部署到 `D:\SWAgentAddin` 时也必须复制这两个文件。

可选兼容：

- 把 6 图标条带同时复制为旧名 `mechpilot-icons-20.bmp` / `mechpilot-icons-32.bmp`，避免旧缓存路径。
- 如果做了兼容复制，请在交付总结中说明。

## 功能目标：读取属性

新增 CommandManager 按钮：

- 按钮名：`读取属性`
- 回调：建议 `SwCmd_ReadProperties`
- task/action 名称：建议 `read_properties`
- 图标索引：`1`

原有命令索引需要顺延：

- `属性填写`：0
- `读取属性`：1
- `属性检查`：2
- `图纸审核`：3
- `任务面板`：4
- `插件设置`：5

务必同步：

- `Cmd...` 常量
- `AddCommandItem2`
- `commandIds`
- `textStyles`
- `ImageListIndex`
- TaskPane 中的操作列表

## 读取属性展示方式

第一版使用 Windows Forms 表单展示，不要只用 MessageBox。

推荐新增一个 `PropertyReadForm` 或内部方法创建表单：

- 标题：`MechPilot - 读取属性`
- 顶部摘要：
  - 当前文档类型
  - 目标数量
  - 属性总数
  - 若为装配体汇总，显示零部件总数量
- 主体：`DataGridView`
- 列建议：
  - `目标名称`
  - `本地零部件名称`
  - `文件路径`
  - `配置`
  - `属性名`
  - `属性值`
  - `解析值`
  - `来源`
  - `数量`

`属性值` 和 `解析值` 的区别：

- 属性值：SolidWorks CustomPropertyManager 返回的原始表达式或存储值。
- 解析值：求值后的结果。

如果第一版拿不到解析值，允许为空，但必须保留列。

## 读取规则：零件

场景：

用户打开 `.SLDPRT`，点击 `读取属性`。

要求：

- 读取当前零件所有文档级自定义属性。
- 使用 `IModelDoc2.Extension.CustomPropertyManager("")`。
- 通过 `GetNames()` 获取全部属性名。
- 对每个属性读取值和解析值。
- 用表单展示。

验收：

- 打开测试零件。
- 点 `读取属性`。
- 表单中出现该零件所有自定义属性。

## 读取规则：工程图

场景：

用户打开 `.SLDDRW`，点击 `读取属性`。

要求：

- 获取工程图中主视图关联的零部件。
- 读取该关联模型的所有自定义属性。
- 表单展示方式同零件。
- 必须备注 `本地零部件名称`。

实现建议：

- 将 `IModelDoc2` 转为 `IDrawingDoc`。
- `GetFirstView()` 通常是图纸 Sheet，不是模型视图。
- 使用 `GetFirstView().GetNextView()` 获取第一个模型视图，作为第一版“主视图”。
- 从 `IView` 获取引用模型：
  - 优先尝试 `ReferencedDocument` 或可用的等价 API。
  - 如拿不到模型对象，可用引用路径做降级提示。

验收：

- 打开有模型视图的工程图。
- 点 `读取属性`。
- 表单显示主视图关联零部件属性。
- `本地零部件名称` 列不为空。

## 读取规则：装配体有选择

场景：

用户打开 `.SLDASM`，并在设计树或 3D 视图中选中零部件。

要求：

- 先检查当前选择集。
- 如果选中了一个或多个零部件，只读取选中零部件的属性。
- 默认不展开选中的子装配体下一阶级。
- 如果用户直接选中了下一阶级的具体零部件，仍然要读取该零部件。
- 表单展示。

实现建议：

- 复用/扩展现有 `ResolveTargets()`。
- 选择对象必须识别 `IComponent2`。
- 对 `IComponent2.GetModelDoc2()` 为空的情况给出中文提示，不要崩溃。

验收：

- 装配体中选中一个零件，读取该零件属性。
- 装配体中选中一个子装配体，只读取该子装配体自身属性，不展开其孩子。
- 装配体中选中多个零部件，表单展示多个目标的属性。

## 读取规则：装配体无选择

这是复杂任务，第一版要做出可用框架，后续再增强。

场景：

用户打开 `.SLDASM`，没有选中任何零部件，点击 `读取属性`。

要求：

- 获取当前装配体下所有子零部件。
- 展开所有子装配体。
- 合并同类项。
- 统计装配体中零部件数量。
- 读取每一类零部件的属性并表格展示。

合并同类项建议：

第一版 key：

```text
文件路径 + 引用配置名
```

如果引用配置名暂时拿不到，先用：

```text
文件路径
```

数量统计：

- 每出现一个组件实例，数量 +1。
- 被抑制组件可以跳过，但要在摘要中说明跳过数量。
- 虚拟件、轻量化组件、未加载组件第一版可给中文提示，后续专项处理。

实现建议：

- 新增 `AssemblyPropertyReadService` 或类似内部方法。
- 遍历 `IAssemblyDoc.GetComponents(...)`。
- 如果 API 参数语义不确定，优先选择能获得全层级组件的方式，并在代码注释中说明。
- 不要为了第一版手写复杂 BOM 引擎；先得到可验证的“展开、合并、计数、展示”。

表格中数量列：

- 零件/工程图/选中组件：数量可为 `1`。
- 装配体无选择全量读取：数量显示合并后的实例数。

验收：

- 打开装配体，不选任何对象。
- 点 `读取属性`。
- 表单显示展开后的零部件列表。
- 同一个零件出现多次时合并为一行或一组属性行，并显示数量。
- 摘要显示零部件总数量。

## 建议数据结构

可以在 `SwAgentAddin.cs` 中先增加内部类，后续再拆文件：

```csharp
public class PropertyReadTarget
{
    public string TargetName { get; set; }
    public string LocalComponentName { get; set; }
    public string FilePath { get; set; }
    public string ConfigurationName { get; set; }
    public string DocType { get; set; }
    public int Quantity { get; set; } = 1;
    public IModelDoc2 Model { get; set; }
    public IComponent2 Component { get; set; }
}

public class PropertyReadRow
{
    public string TargetName { get; set; }
    public string LocalComponentName { get; set; }
    public string FilePath { get; set; }
    public string ConfigurationName { get; set; }
    public string PropertyName { get; set; }
    public string RawValue { get; set; }
    public string ResolvedValue { get; set; }
    public string Source { get; set; }
    public int Quantity { get; set; }
}
```

## 推荐实现阶段

### 阶段 1：按钮和图标

- 增加 `读取属性` 命令。
- 切换到 6 图标条带。
- `设置` 改为 `插件设置`。
- 构建通过。

### 阶段 2：零件读取 + 表单展示

- 读取当前零件所有自定义属性。
- 用 `DataGridView` 展示。
- 构建通过并实机验证。

### 阶段 3：工程图主视图关联模型读取

- 找到第一个模型视图。
- 读取其引用模型属性。
- 增加本地零部件名称列。

### 阶段 4：装配体选中组件读取

- 复用选择集解析。
- 只读选中项，不展开子装配体。

### 阶段 5：装配体无选择全量展开/合并/计数

- 递归/全层级获取组件。
- 按路径+配置合并。
- 统计数量。
- 表单展示。

如果时间不够，至少交付到阶段 2，并明确后续阶段阻塞点。

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

## 实机验证清单

1. 关闭 SolidWorks。
2. 复制新版 DLL、config、rules、taskpane、6 图标条带到 `D:\SWAgentAddin`。
3. 启动 SolidWorks，勾选 `MechPilot`。
4. 确认工具栏出现 6 个按钮：
   - 属性填写
   - 读取属性
   - 属性检查
   - 图纸审核
   - 任务面板
   - 插件设置
5. 打开零件，点 `读取属性`，表单显示当前零件属性。
6. 打开工程图，点 `读取属性`，表单显示主视图关联零部件属性。
7. 打开装配体，选中一个零部件，点 `读取属性`，表单显示选中零部件属性。
8. 打开装配体，选中多个零部件，点 `读取属性`，表单显示多个目标属性。
9. 打开装配体，不选任何零部件，点 `读取属性`，表单显示全量展开、合并同类项和数量统计。
10. 回归测试 `属性填写`，确认原有本地写入能力仍通过。

## 交付总结必须包含

- 构建结果。
- 修改文件清单。
- 新按钮和 ImageListIndex 对应关系。
- 新图标文件部署位置。
- `设置` 改为 `插件设置` 是否完成。
- 读取属性支持了哪些场景：
  - 零件
  - 工程图主视图
  - 装配体选中组件
  - 装配体无选择全量展开/合并/计数
- 每个场景的实机验证结果。
- 如果 2.3B 只完成部分框架，必须明确未完成项和下一步最短路径。
