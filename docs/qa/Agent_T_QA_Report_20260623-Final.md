# Agent T 集成 QA 报告（蓝图批次）

- **执行时间**: 2026-06-23
- **执行者**: Agent T
- **工程**: `E:\0B 软件开发\MechPilot\Cockpit`
- **运行部署**: `D:\SWAgentAddin`

> **声明**: 本报告区分「自动验证通过」与「待用户实机确认」。无真实 SolidWorks 会话时，不宣称实机通过。

---

## 1. 构建结果

| 项 | 结果 |
|----|------|
| 命令 | `dotnet build src\SwAgentAddin\SwAgentAddin.csproj -c Release` |
| SW_HOME | `D:\Program Files\SW\2022\SOLIDWORKS` |
| 错误 | **0** |
| 警告 | **0** |
| 输出 | `deploy\SwAgentAddin.dll` |
| app.js 语法 | `node --check` **通过** |

**结论**: 构建验证 **通过**。

---

## 2. 部署结果

### 2.1 deploy 目录

全部必需项存在：

- `SwAgentAddin.dll`
- `config\config.json` / `config\rules.local.json`
- `assets\icons\mechpilot-blueprint-icons-16-20.bmp`
- `assets\icons\mechpilot-blueprint-icons-16-32.bmp`
- `frontend\property-workbench\index.html|app.js|styles.css`
- `frontend\cockpit-contracts\context.schema.json`
- `Microsoft.Web.WebView2.Core.dll` / `WinForms.dll` / `Wpf.dll`
- `runtimes\*\native\WebView2Loader.dll`

### 2.2 D:\SWAgentAddin 运行目录

| 文件 | 状态 |
|------|------|
| SwAgentAddin.dll | ✅ |
| config.json / rules.local.json | ✅ |
| blueprint 图标 (16-20 / 16-32) | ✅ |
| frontend\property-workbench\* | ✅ |
| cockpit-contracts\context.schema.json | ✅ |
| WebView2 DLL | ✅ |
| outputs\ (bom/convert/drawing/backup) | ✅ 目录已创建 |

- deploy DLL: `2026-06-23 00:18:05`
- runtime DLL: `2026-06-23 00:18:41`（已同步，时差 < 1 分钟）

- 旧路径 `D:\SWAgentAddin\property-workbench` **仍存在**（历史遗留）；新结构 `frontend\property-workbench` 已就位并优先使用。

**结论**: 部署验证 **通过**（静态清单）。

---

## 3. SW 注册/加载结果

| 项 | 结果 | 说明 |
|----|------|------|
| COM GUID | ✅ | `E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1` 未变 |
| CodeBase | ✅ | `file:///D:/SWAgentAddin/SwAgentAddin.DLL` |
| SolidWorks AddIns 注册 | ✅ | Title=MechPilot, 默认启用=1 |
| 历史加载日志 | ⚠️ | 最近成功: `[2026-06-22 22:23:55] ConnectToSW completed successfully. Mode=local` |
| 新版 16 按钮加载 | ⏳ | 日志中仍为旧版 7 按钮 tab（ids 41714–41720），**未见** `3-zone built` / blueprint 日志 |

**结论**: 注册静态检查 **通过**；插件加载历史成功但为**旧版 DLL**。**待用户重启 SolidWorks 加载新 DLL 后复验。**

---

## 4. 工具栏按钮清单

源码定义 **16 按钮 / 3 分区**（Agent P）：

### AI 工具区 (index 0–5)

| Index | 名称 | 回调 |
|------:|------|------|
| 0 | 驾驶舱 | SwCmd_Cockpit |
| 1 | AI助手 | SwCmd_AIAssistant |
| 2 | 图纸审核 | SwCmd_AIDrawingReview |
| 3 | 快速选型 | SwCmd_AISelection |
| 4 | 物料检索 | SwCmd_MaterialSearch |
| 5 | 设计计算 | SwCmd_DesignCalc |

### 本地工程工具区 (index 6–12)

| Index | 名称 | 回调 |
|------:|------|------|
| 6 | 属性填写 | SwCmd_PropertyFill |
| 7 | 读取属性 | SwCmd_ReadProperties |
| 8 | 属性检查 | SwCmd_PropertyCheck |
| 9 | BOM导出 | SwCmd_BomExport |
| 10 | 批量转换 | SwCmd_BatchConvert |
| 11 | 图纸导出 | SwCmd_DrawingExport |
| 12 | 打包备份 | SwCmd_PackageBackup |

### 系统区 (index 13–15)

| Index | 名称 | 回调 |
|------:|------|------|
| 13 | 插件设置 | SwCmd_Settings |
| 14 | 规则配置 | SwCmd_RulesConfig |
| 15 | 关于 | SwCmd_About |

- 图标配置: `mechpilot-blueprint-main-*.bmp` + `mechpilot-blueprint-icons-16-*.bmp`
- CommandTab: 3 个 CommandTabBox 对应三分区

**实机可见性**: ⏳ **待用户确认**（需 SW 重启后查看 MechPilot 选项卡）

---

## 5. AICockpit 页面验收

| 验收项 | 代码/静态 | 实机 |
|--------|-----------|------|
| index.html Sidebar 8 页 | ✅ | ⏳ |
| app.js navigatePage / PAGES | ✅ | ⏳ |
| AI 侧栏展开/收起 | ✅ installAIPanel | ⏳ |
| receiveContext 真实数据 | ✅ normalizeContext `_isMock:false` | ⏳ |
| 历史注入真实装配体 | ✅ 日志: `接料固定机构.SLDASM` 11374 chars | ⏳ |
| 驾驶舱打开非空白 | ✅ 历史有导航+注入 | ⏳ |
| AI助手 → 展开 AI 面板 | ⚠️ SwCmd_AIAssistant 仅调 ShowCockpitOrStub，**未传 navigate_page=assistant** | ⏳ |
| 图纸审核等 AI 页快捷入口 | ⚠️ 同上，仅打开 Cockpit 未切页 | ⏳ |

**结论**: Shell 结构 **已实现**（Agent R）；工具栏快捷入口 **页面跳转未完全接线**。**待实机确认 UI 与跳转行为。**

---

## 6. 本地 P0 功能完成表

| 功能 | Action | 实现 | 工具栏 | 实机冒烟 |
|------|--------|------|--------|----------|
| 属性填写 | properties.fill | ✅ ExecuteTask 旧逻辑 | ✅ | ⏳ |
| 读取属性 | properties.read | ✅ ExecuteReadProperties + WinForms/Cockpit | ✅ | ⏳ |
| 属性检查 | properties.check | ✅ ExecuteTask | ✅ | ⏳ |
| 文件名拆分写属性 | filename.parse_to_properties | ✅ LocalToolbeltExecutor | 经协议 | ⏳ |
| BOM 导出 CSV | bom.export | ✅ 装配体→CSV | ✅ SwCmd_BomExport | ⏳ |
| 批量转换 | file.convert | ✅ pdf/step/dxf/dwg | ✅ SwCmd_BatchConvert | ⏳ |
| 图纸导出 PDF | drawing.export | ✅ 工程图/同名图 | ✅ SwCmd_DrawingExport | ⏳ |
| 打包备份 | package.backup | ✅ 时间戳目录复制 | ✅ SwCmd_PackageBackup | ⏳ |
| 未实现不崩溃 | — | ✅ FailResult + ShowResultMessage 中文提示 | — | ⏳ |

- `D:\SWAgentAddin\outputs\` 子目录已创建，**本次 QA 无 SW 实机运行，无产出 CSV/PDF 文件**。

**结论**: 代码层面 P0 **≥5 项可运行**；实机产出 **待确认**。

---

## 7. Hermes 通讯结果

| 项 | 结果 |
|----|------|
| config agent_server | ✅ 已配置 `http://127.0.0.1:8080` |
| HermesClient | ✅ invoke / submit / poll + ContextTrimmer |
| HandleCockpitCommand | ✅ ai.* / agent.task.* 已接线 |
| 当前 Hermes 在线 | ❌ `127.0.0.1:8080` 连接被拒绝 |
| 离线降级 | ✅ 代码: MakeOfflineResult 中文提示；前端 doSendAI 本地占位回复 |
| 本地工具阻断 | ✅ 静态：LocalToolbelt 独立于 Hermes |
| 在线 submit/poll | ⏳ 无 Hermes 服务，**未实测** |

**结论**: Hermes **离线降级路径静态通过**；在线通讯 **待 Hermes 服务启动后确认**。

---

## 8. 修改文件清单（本批次相关）

### 已修改 (git tracked)

- `src/SwAgentAddin/SwAgentAddin.cs`
- `src/SwAgentAddin/SwAgentAddin.csproj`
- `src/SwAgentAddin/config/config.json`
- `src/SwAgentAddin/frontend/property-workbench/app.js`
- `src/SwAgentAddin/frontend/property-workbench/index.html`
- `src/SwAgentAddin/frontend/property-workbench/styles.css`

### 新增 (untracked)

- `src/SwAgentAddin/ActionRouter.cs`
- `src/SwAgentAddin/LocalToolbeltExecutor.cs`
- `src/SwAgentAddin/MechPilotProtocol.cs`
- `src/SwAgentAddin/assets/icons/mechpilot-blueprint-*.{bmp,png}`
- `docs/tasks/MechPilot_Agent_{P,Q,R,S,T}_*.md`
- `docs/qa/`、`logs/`、`scripts/agent_t_watchdog.py`
- `deploy/` 全量部署产物

---

## 9. 剩余风险

1. **SW 未加载新 DLL**: 最近实机日志为 7 按钮旧版；16 按钮/蓝图图标需重启 SW 验证。
2. **AI 工具栏快捷入口未切页**: AI助手/图纸审核等仅打开 Cockpit，未 navigate 到对应 Sidebar 页。
3. **EnsureIconFiles 仍检查旧图标名** (`mechpilot-icons-7-*`)，可能产生误导性 WARNING（实际已用 blueprint）。
4. **TaskPane**: 历史日志 `CreateTaskpaneView3 returned null`（Cockpit 窗口路径仍可用）。
5. **旧 property-workbench 路径残留**，可能混淆部署文档。
6. **Hermes 未部署**，AI 页面仅能占位/降级，无法端到端验 task_id 轮询。

---

## 10. 需要用户实机确认的事项

请在本机 SolidWorks 2022 按序执行：

- [ ] **重启 SolidWorks**，确保加载 `D:\SWAgentAddin` 最新 DLL（日志应出现 `3-zone built` / `blueprint icons`）
- [ ] 工具 → 插件 → **MechPilot 勾选无报错**
- [ ] MechPilot 选项卡：**16 按钮 + 三分区 + 蓝图彩色图标**
- [ ] 点击 **驾驶舱**：WebView2 打开，显示当前文件名，状态为「真实数据」
- [ ] **Sidebar** 切换 8 个页面；**AI 侧栏** 展开/收起
- [ ] 点击 **AI助手**：是否自动切到 AI 助手页并展开侧栏（当前可能仅开 Cockpit）
- [ ] **属性填写 / 读取属性 / 属性检查** 各点一次
- [ ] 打开装配体 → **BOM导出**，检查 `D:\SWAgentAddin\outputs\bom\*.csv`
- [ ] 工程图 → **图纸导出** 或 **批量转换** → PDF
- [ ] **打包备份** → `outputs\backup\` 有时间戳目录
- [ ] **Hermes 离线**：AI 助手发消息，页面不崩溃，有中文提示
- [ ] **Hermes 在线**（如可用）：`ai.assistant.chat` / `agent.task.submit` / `agent.task.poll`

---

## 总评

| 维度 | 自动 QA | 实机 |
|------|---------|------|
| 构建 | ✅ 通过 | — |
| deploy / D:\SWAgentAddin | ✅ 通过 | — |
| COM 注册 | ✅ 通过 | — |
| P/Q/R/S 代码合入 | ✅ 通过 | — |
| SW 加载 + 16 按钮 UI | — | ⏳ 待确认 |
| AICockpit + 真实上下文 | 历史成功 | ⏳ 待确认 |
| 本地 P0 产出 | 代码就绪 | ⏳ 待确认 |
| Hermes | 离线降级 OK | 在线 ⏳ |

**批次状态**: 静态/构建/部署 **可交付**；实机冒烟 **待用户确认后闭环**。
