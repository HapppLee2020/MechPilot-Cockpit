# Agent K Task: Agent驾驶舱前端原型

你负责实现 `property-workbench` 前端。它必须可以独立用浏览器打开 mock 数据，也可以被 WebView2 加载。

## 目标

做出一个现代 MechPilot Agent驾驶舱第一版：

- 左侧真实/模拟装配树。
- 右侧属性表。
- 顶部当前文件、模式、状态。
- 表头筛选。
- 搜索。
- 解析值/属性值切换。
- 树表联动。
- 为未来 Agent 任务、报告、队列留空间。

## 创建目录

`output\SW-Agent-Addin\property-workbench`

文件：

- `index.html`
- `styles.css`
- `app.js`
- `mock-data.js`
- `README.md`

可选：

- `libs\`

## 库选择

优先方案：

- 使用纯 HTML/CSS/JS 实现第一版，避免运行时依赖。

如果使用表格库：

- 可以使用 Tabulator Community，但必须本地 vendored 到 `libs/`。
- 禁止 CDN。
- 生产运行不能依赖互联网。

## UI 布局

```text
┌──────────────────────────────────────────────┐
│ 顶部：MechPilot Agent驾驶舱 | 当前文件 | 模式 │
├──────────────┬───────────────────────────────┤
│ 左：装配树    │ 右：属性表 + 筛选/搜索         │
│              │                               │
├──────────────┴───────────────────────────────┤
│ 底部：状态 / Agent 输出 / 后续任务进度         │
└──────────────────────────────────────────────┘
```

设计要求：

- 中文界面。
- 白底、清晰、工程工具风格。
- 不要传统 WinForms 味。
- 左侧树可折叠。
- 固有列固定显示：
  - 零部件名称
  - 数量
  - 文档类型
  - 文件路径
  - 文件大小
- 自定义属性列动态生成。

## JS API

必须暴露：

```javascript
window.MechPilot = {
  receiveContext(context) {},
  receiveResult(result) {},
  sendCommand(type, payload) {}
}
```

WebView2 发送命令：

```javascript
window.chrome.webview.postMessage(JSON.stringify(commandEnvelope))
```

如果不在 WebView2 中，使用 mock log，不报错。

## Mock Data

`mock-data.js` 提供 `window.MECHPILOT_MOCK_CONTEXT`。

必须覆盖：

- assembly root
- subassembly
- repeated part quantity
- several property columns
- raw/resolved values

## 功能第一版

必须完成：

- render tree
- render property table
- click tree selects table row
- click table row highlights tree node
- global search
- header filter or column filter
- raw/resolved toggle
- responsive layout

## 验收

- 双击 `index.html` 可用 mock 数据展示。
- WebView2 注入 context 后能替换 mock 数据。
- 无 CDN 依赖。
- 中文界面。

## 交付总结

说明：

- 是否使用第三方表格库。
- 支持的筛选/搜索能力。
- WebView2 bridge API。
- mock 数据覆盖场景。
