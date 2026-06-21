# MechPilot Add-in

## Agent N：7 图标条带集成

本版本已切换到 7 图标条带，用于 CommandManager 的 7 个按钮图标。

- 图标源目录：`E:\0B 软件开发\MechPilot\Cockpit\src\icon-assets`
- 源码内图标：`mechpilot-icons-7-20.bmp`、`mechpilot-icons-7-32.bmp`
- 部署输出图标：`deploy\mechpilot-icons-7-20.bmp`、`deploy\mechpilot-icons-7-32.bmp`
- `ApplyCommandGroupIcons()` 使用 `mechpilot-icons-7-20.bmp` / `mechpilot-icons-7-32.bmp`
- 旧 `mechpilot-icons-6-*` 文件保留，便于回滚。

ImageListIndex 对应关系：

| Index | 按钮 | 图标语义 |
|------:|------|----------|
| 0 | Agent驾驶舱 | 现代驾驶舱 / 仪表盘 / Agent 节点 |
| 1 | 属性填写 | 文档 + 铅笔 + 加号 |
| 2 | 读取属性 | 文档 + 放大镜 |
| 3 | 属性检查 | 文档 + 勾选 |
| 4 | 图纸审核 | 蓝图 + 放大镜 |
| 5 | 任务面板 | 面板 / 任务列表 |
| 6 | 插件设置 | 齿轮 |

SolidWorks 2022 智能辅助插件 — 双模式（本地 / 远程）

## 功能

| 按钮 | 功能 | 说明 |
|------|------|------|
| Agent驾驶舱 | 打开驾驶舱窗口 | WebView2 嵌入 cockpit 页面，实时查看文档状态、属性表、装配树 |
| 属性填写 | 一键填写自定义属性 | 本地模式：直接写 SW 属性；远程模式：提交到 Agent Server |
| 读取属性 | 读取并展示自定义属性 | 零件/工程图/装配体选中/装配体全量展开，DataGridView 表单展示 |
| 属性检查 | 属性完整性检查 | 检查当前模型的自定义属性是否符合规范 |
| 图纸审核 | 工程图审核 | 提交当前工程图进行自动审核 |
| 任务面板 | 打开任务面板 | 显示插件状态和快捷操作 |
| 插件设置 | 打开配置文件 | 用系统默认编辑器打开 config.json |

## 执行模式

### 本地模式 (`execution_mode: "local"`)

插件直接在 SolidWorks 中写入自定义属性，**不需要 Agent Server**。

- 打开零件 → 写入零件属性
- 打开装配体（无选择） → 写入装配体属性
- 打开装配体（选中 1 个组件） → 写入该组件属性
- 打开装配体（选中多个组件） → 弹出确认后逐个写入

属性规则由 `rules.local.json` 定义，支持占位符：

| 占位符 | 说明 |
|--------|------|
| `{file_name}` | 文件名（含扩展名） |
| `{file_name_no_ext}` | 文件名（不含扩展名） |
| `{doc_type}` | 文档类型（part/assembly/drawing） |
| `{date}` | 当前日期 |
| `{engineer_id}` | 工程师 ID |
| `{mode}` | 执行模式 |

### 远程模式 (`execution_mode: "remote"`)

插件向 Agent Server 提交任务请求，后续由 MCP/Worker 执行。

```
MechPilot Add-in (瘦客户端)
    │  HTTP POST /api/v1/task
    ▼
Agent Server (FastAPI L1)
    ├── Rule Engine (80% 规则匹配)
    └── Hermes Agent (20% LLM 推理)
         ▼
    MCP Worker (L3 执行层)
```

## 架构

```
SwAgentAddin.cs
  ├── AddinConfig          — 配置加载（向后兼容旧格式）
  ├── LocalPropertyRules   — 本地规则 JSON 解析
  ├── RuleProvider         — 规则加载/自动创建 demo
  ├── ResolvedTarget       — 解析后的写入目标
  ├── ExecuteLocalTask     — 本地属性写入
  └── ExecuteRemoteTask    — 远程任务提交
```

## 快速部署

### 前置条件

- SolidWorks 2022 已安装
- .NET Framework 4.8（SW 2022 自带）
- .NET SDK（用于构建）

### 步骤

```bash
# 1. 设置环境变量（仅首次）
set SW_HOME=D:\Program Files\SW\2022\SOLIDWORKS

# 2. 构建
build.bat

# 3. 安装（管理员身份）
deploy\install.bat

# 4. 编辑配置
notepad deploy\config.json
```

### 配置说明

`config.json` 字段：

| 字段 | 默认值 | 说明 |
|------|--------|------|
| execution_mode | local | 执行模式：`local` 或 `remote` |
| server_url | http://127.0.0.1:8080 | Agent Server 地址（远程模式） |
| engineer_id | 当前用户名 | 工程师 ID |
| confirm_before_write | true | 多选时是否弹确认框 |
| local_rules_file | rules.local.json | 本地规则文件名 |
| remote_task_endpoint | /api/v1/task | 远程任务提交路径 |
| request_timeout_seconds | 120 | 请求超时 |
| log_level | 2 | 日志级别: 0=关 1=Error 2=Info 3=Debug |
| cockpit_enabled | true | 是否启用 Agent驾驶舱按钮 |
| cockpit_url_mode | local | `local`=嵌入HTML，`dev`=开发服务器 |
| cockpit_entry | property-workbench/index.html | 本地 cockpit HTML 入口 |
| cockpit_dev_url | http://127.0.0.1:5173 | Vite 开发服务器 URL |
| cockpit_prefer_webview2 | true | 优先使用 WebView2 |
| cockpit_fallback_to_winforms | true | WebView2 不可用时回退 WinForms |
| cockpit_schema_version | mechpilot.cockpit.context.v1 | 上下文 JSON 合同版本 |

### 本地规则

`rules.local.json` 定义每种文档类型要写入的属性和值模板。

删除该文件后重新加载插件，会自动创建 demo 规则。

### 在 SolidWorks 中启用

1. 启动 SolidWorks 2022
2. 菜单：工具 → 插件
3. 勾选 **"MechPilot"**
4. 顶部菜单栏出现 **"MechPilot"** Tab

## 项目结构

```
SW-Agent-Addin/
├── SwAgentAddin.cs          # Add-in 核心代码（单文件，含所有类）
├── SwAgentAddin.csproj      # .NET Framework 4.8 项目文件
├── taskpane.html            # TaskPane 前端页面
├── config.json              # 配置文件
├── rules.local.json         # 本地属性规则
├── cockpit-contracts/       # Agent驾驶舱 JSON 合同
│   ├── context.schema.json  # 上下文快照合同
│   ├── command.schema.json  # 命令信封合同
│   ├── result.schema.json   # 结果信封合同
│   └── README.md            # 合同说明
├── build.bat                # 构建脚本
├── install_tabicons_v2.bat  # 推荐安装脚本
└── README.md                # 本文件
```

> **注意：** 内部程序集名 `SwAgentAddin` 和 DLL 文件名暂保留为历史兼容名。
> 用户可见品牌已统一为 **MechPilot**。界面已全面中文化。
> 新版功能图标（mechpilot-*.bmp）已集成，旧 agent-*.bmp 作为兼容副本保留。

## 故障排查

| 问题 | 解决方案 |
|------|---------|
| 构建失败：找不到 SW Interop DLL | 设置 `SW_HOME` 环境变量指向 SW 安装目录 |
| SW 中看不到插件 | 工具 → 插件 → 检查 "MechPilot" 是否勾选 |
| 本地模式不写属性 | 检查 `rules.local.json` 是否存在且格式正确 |
| 远程模式连接失败 | 检查 `config.json` 中的 `server_url` |
| 属性写入报错 | 检查文件是否已保存（未保存的文档无法写入） |
## Agent L: Cockpit 上下文采集 — 2026-06-21 修复与增强

### 修复内容

| 问题 | 修复 |
|------|------|
| SwAgentAddin.cs:2516 字符串转义 | `SerializeCockpitContext` 中 `"error"` 转义修复 |
| #region / #endregion 不平衡 | 补齐 Data Classes 缺失的 `#endregion` |
| SafeCompFileKey 缺失 | 新增静态方法定义 |

### CockpitContext 完整字段覆盖

| 顶层字段 | 状态 | 说明 |
|----------|------|------|
| `schema_version` | ✓ | `mechpilot.cockpit.context.v1` |
| `timestamp_utc` | ✓ | ISO 8601 UTC |
| `client` | ✓ | engineer_id / sw_version / addin_version / execution_mode / machine_name |
| `active_document` | ✓ | title / file_path / doc_type / configuration_name / is_saved / is_modified / custom_property_count / file_size / last_modified |
| `selection` | ✓ | index / display_name / component_name / component_path / doc_type / configuration / is_suppressed / is_lightweight / pivot_key |
| `assembly_tree` | ✓ | 真实层级：node_id / parent_id / display_name / name / component_name / document_name / instance_path / doc_type / file_path / configuration / quantity / depth / children_count / pivot_key / is_assembly / is_part / is_suppressed / is_lightweight / children |
| `property_table` | ✓ | total_rows / total_instances / intrinsic_columns / dynamic_columns / rows (document_key / instance_key / row_key + 固有字段 + properties dict) |
| `summary` | ✓ | target_count / total_components / unique_doc_count / part_count / sub_assembly_count / drawing_count / suppressed_count / lightweight_count / read_failed_count / custom_property_column_count |
| `warnings` | ✓ | level (info/warning/error/fatal) / target / message |
| `capabilities` | ✓ | `["read", "write", "check", "review", "cockpit"]` |

### 稳定 ID 体系

| ID 类型 | 规则 | 用途 |
|---------|------|------|
| `document_key` | 规范化文件路径 `Path.GetFullPath(...).ToLowerInvariant()` | 同一文件多次出现时汇总；虚拟件返回 null |
| `instance_key` | `file_path\|config\|component_name` | 表格与树节点关联 |
| `row_key` | 同 instance_key | 表格行选择 |
| `pivot_key` | 同 instance_key | 属性表透视键 |
| `tree_node_id` | `"node-" + 自增序号` | 设计树节点唯一标识 |
| `instance_path` | `parent_display_name + "/" + display_name` | 前端定位组件层级路径 |

### 属性过滤规则

- `read_show_all_properties = false` → 仅返回 `read_property_names` 配置清单内属性 + 内置固有属性（文档类型、装配体名称、图纸名称）
- `read_show_all_properties = true` → 配置清单优先排序，其余属性追加
- 配置清单为空时 → 返回全部属性

### 场景支持矩阵

| 场景 | 状态 | 说明 |
|------|------|------|
| 未打开文档 | ✓ | 返回 skeleton context，title=`(none)` |
| 零件 | ✓ | 读取全部自定义属性，assembly_tree 为空列表 |
| 工程图 | ✓ | 通过 `ReadDrawingProperties` 获取主视图关联模型属性 |
| 装配体（无选择） | ✓ | 全量展开：`ReadAssemblyAllComponents` + 真实层级树 |
| 装配体（选中组件） | ✓ | Selection 字段包含选中组件信息；property_table 仍为全量（供前端联动过滤） |
| 大装配体 | ✓ | `Stopwatch` 计时 + 日志输出耗时；JavaScriptSerializer MaxJsonLength=int.MaxValue |

### 错误隔离

- 每个组件读取失败 → `warnings[]` 追加 warning，不中断
- 被抑制/轻化组件 → 跳过 + `warnings[]` 追加 info
- 严重异常 → `warnings[]` 追加 error，返回部分上下文（不崩溃）
- 致命异常 → `warnings[]` 追加 fatal，返回 skeleton context

### 剩余风险和待办

| 风险/待办 | 归属 | 说明 |
|-----------|------|------|
| WebView2 运行时依赖 | Agent J | 驾驶舱宿主需 Microsoft WebView2 Runtime；构建时可能有版本警告 |
| 虚拟件文件路径 | Agent K | 虚拟组件 `GetPathName()` 返回空 → document_key 为 null，前端需处理 |
| 大装配体性能 | Agent M | 当前同步读取；>5000 组件建议后续改为异步或分页 |
| 工程图多图纸 | Agent L (后续) | 当前仅读取主视图模型 |
| 属性写入联动 | Agent J/K | `property_table` 为只读快照；写入操作需走 `ExecuteLocalTask` 另外触发刷新 |
| 配置特定属性 | Agent K | `configuration` 字段已提供，前端可区分默认配置与派生配置 |
