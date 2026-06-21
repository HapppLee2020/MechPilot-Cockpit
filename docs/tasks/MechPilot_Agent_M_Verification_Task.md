# Agent M Task: MechPilot Agent 驾驶舱实机验收

生成时间：2026-06-21 23:43  
前置条件：Agent J / K / L 交付完成，DLL + 前端文件已部署到 D:\SWAgentAddin

---

## 部署文件清单

| 文件 | 路径 | 作用 |
|------|------|------|
| SwAgentAddin.dll | D:\SWAgentAddin\ | C# Add-in，含 CockpitForm + BuildCockpitContext |
| app.js | D:\SWAgentAddin\property-workbench\ | 驾驶舱前端主逻辑，含 normalizeContext + Summary/Warnings 渲染 |
| styles.css | D:\SWAgentAddin\property-workbench\ | 样式表，含抑制/轻化标记 + Warnings 弹层 |
| index.html | D:\SWAgentAddin\property-workbench\ | 入口页面 |
| mock-data.js | D:\SWAgentAddin\property-workbench\ | Mock 数据（真实注入后会被覆盖） |

---

## 验收步骤

### 1. 清理旧状态

```powershell
# 关闭所有 SolidWorks 进程
# 可选：清除 WebView2 缓存
Remove-Item -LiteralPath "D:\SWAgentAddin\cockpit-cache\EBWebView" -Recurse -Force -ErrorAction SilentlyContinue
```

### 2. 启动验收

1. 启动 SolidWorks 2022
2. 确认 MechPilot Tab 可见，所有 7 个按钮正常显示
3. 打开测试装配体（如 613三夹爪上模块.SLDASM）
4. 点击 **Agent驾驶舱** 按钮

### 3. 功能验收清单

#### 基础连通性
- [ ] 驾驶舱窗口正常弹出（WebView2 渲染）
- [ ] 页面不显示白屏 / 404 / 脚本错误

#### 数据注入
- [ ] 顶栏显示当前 SW 文件名（不是 mock 数据）
- [ ] 状态 badge 显示"真实数据"（非 mock）
- [ ] 左侧装配树显示真实组件名（非示例数据）
- [ ] 右侧表格显示真实属性列和值

#### 联动功能
- [ ] 点击树节点 → 表格对应行高亮选中
- [ ] 点击表格行 → 树节点高亮定位
- [ ] 搜索框输入组件名 → 表格正确过滤
- [ ] 列筛选弹出并可用
- [ ] 解析值/原始值切换有效
- [ ] 全部展开/全部折叠按钮有效

#### 增强功能（Agent K 新增）
- [ ] 状态栏第2行显示：零部件数 / 装配体数 / 去重数
- [ ] 如有抑制/轻化组件，状态栏显示对应计数
- [ ] 如有 Warnings，状态栏显示"⚠ 警告：N"，点击弹出详情
- [ ] 弹层显示每个警告的级别(error/warning/info)、目标、消息
- [ ] 抑制/轻化节点在树中显示"抑制"/"轻化"红色/橙色标记

#### 日志验证

打开 `D:\SWAgentAddin\addin-load.log`，应看到：

```
BuildCockpitContext: type=... rows=... treeNodes=... warnings=... elapsed=...
CockpitForm: context injected (...)
```

不应出现：
```
CockpitForm: context injection script failed
BuildCockpitContext FATAL
```

---

## 如果仍显示 mock 数据

1. 完全关闭 SolidWorks（任务管理器确认 SLDWORKS.exe 已退出）
2. 清除 WebView2 缓存
3. 重新打开 SW → 打开装配体 → 点击 Agent驾驶舱
4. 检查 addin-load.log 是否有注入失败的 trace

---

## 如果驾驶舱打不开 / 崩溃

1. 检查 WebView2 Runtime 是否已安装
   - 下载：https://developer.microsoft.com/en-us/microsoft-edge/webview2/
2. 检查 addin-load.log 最后几行是否有异常
3. 检查 SOLIDWORKS → 工具 → 插件 → MechPilot 是否勾选

---

## 验收结论

- [ ] 基础连通性：通过 / 失败
- [ ] 数据注入：通过 / 失败
- [ ] 联动功能：通过 / 失败
- [ ] 增强功能（K 新增）：通过 / 失败
- [ ] 日志验证：通过 / 失败

**签署：** ________  
**日期：** ________
