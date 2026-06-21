# MechPilot Agent驾驶舱 — property-workbench

> Agent K 前端原型：纯 HTML/CSS/JS，零依赖，可独立浏览器打开，可被 WebView2 加载。

## 快速启动

直接双击 `index.html` 即可使用 mock 数据展示。

或用简易 HTTP 服务器：

```bash
# Python
python -m http.server 8080

# Node
npx serve .
```

浏览器访问 `http://localhost:8080`。

## 文件结构

```
property-workbench/
├── index.html      ← 入口页面
├── styles.css      ← 样式（白底工程风）
├── app.js          ← 核心逻辑（树、表、搜索、筛选、bridge）
├── mock-data.js    ← Mock 数据（开发/演示用）
├── README.md       ← 本文件
└── libs/           ← 预留：第三方库（当前版本未使用）
```

## UI 布局

```
┌──────────────────────────────────────────────────┐
│ 顶栏：MechPilot Agent驾驶舱 | 文件 | 模式 | 状态   │
├──────────────────────────────────────────────────┤
│ 工具栏：搜索 | 解析值/原始值切换 | 全部折叠/展开      │
├────────────────┬─────────────────────────────────┤
│ 左：装配树       │ 右：属性表 + 列筛选              │
│  （可拖拽宽度）   │                                 │
├────────────────┴─────────────────────────────────┤
│ 状态栏：连接状态 | Agent输出 | 统计                  │
└──────────────────────────────────────────────────┘
```

## 功能清单

| 功能 | 说明 |
|------|------|
| 装配树渲染 | 支持多层嵌套、折叠/展开、图标区分 |
| 属性表 | 固有列（名称/数量/类型/路径/大小）+ 动态属性列 |
| 树表联动 | 点击树节点 → 表格高亮；点击表格行 → 树高亮 |
| 全局搜索 | 实时模糊匹配名称、属性值 |
| 列筛选 | 点击列头漏斗图标，弹出多选筛选 |
| 原始值/解析值切换 | 工具栏一键切换，影响所有属性列 |
| 面板宽度调整 | 拖拽左右分隔条 |
| 全部折叠/展开 | 工具栏快捷按钮 |
| 响应式 | 900px 以下收窄树面板，640px 以下隐藏树 |
| WebView2 Bridge | `window.MechPilot` API，无缝对接 C# 后端 |

## WebView2 Bridge API

### C# → JS

```csharp
// 注入新数据（替换 mock）
webView.CoreWebView2.ExecuteScriptAsync(
    $"window.MechPilot.receiveContext({jsonContext})"
);

// 推送 Agent 结果
webView.CoreWebView2.ExecuteScriptAsync(
    $"window.MechPilot.receiveResult({jsonResult})"
);
```

### JS → C#

前端通过 `window.MechPilot.sendCommand(type, payload)` 向 WebView2 发送命令。

WebView2 监听：
```csharp
webView.WebMessageReceived += (s, e) => {
    var msg = e.WebMessageAsJson;
    // 解析 { type, payload, ts }
};
```

命令类型：
- `select` — 用户选中了某个节点（payload: `{ id }`）

### 非 WebView2 环境

`sendCommand` 自动降级为 `console.log`，不会报错。

## 第三方库

**当前版本未使用任何第三方库。** 纯 HTML/CSS/JS 实现。

`libs/` 目录预留，供未来版本选择性引入：
- Tabulator Community（如需虚拟滚动/高级表格功能）
- 其他轻量库

## Mock 数据覆盖场景

| 场景 | 示例 |
|------|------|
| 装配体根节点 | 阀体总成 V2.3 |
| 子装配体 | 阀芯组件、密封组件、法兰连接 |
| 重复零件数量 | 弹簧座 ×2、O型圈 ×3、双头螺柱 ×8、螺母 ×16 |
| 动态属性列 | 材质、重量、表面处理、供应商、零件号、公差等级、备注 |
| 原始值（SolidWorks 引用） | `SW-材料@阀芯.SLDPRT` |
| 解析值（实际结果） | `06Cr19Ni10 (304不锈钢)` |
| 顶层散件 | 阀体、填料压盖 |

## 设计风格

- 白底、清晰、工程工具风格
- 非 WinForms 味 — 现代 CSS 布局、圆角、阴影、过渡动画
- 中文界面
- 无动画干扰，仅用于状态反馈（选中高亮脉冲）

## 未来扩展预留

- 底部区域可扩展：Agent 任务队列、报告面板、进度条
- `receiveResult` API 支持 Agent 推送任意结果
- `sendCommand` 可扩展更多命令类型（编辑属性、导出、刷新等）
