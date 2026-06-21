# Agent M Task: Agent驾驶舱集成、部署与实机验收

你负责把 Agent I/J/K/L 的成果合并验证，形成可演示的 MechPilot Agent驾驶舱。

## 目标

确保新增 `Agent驾驶舱` 不破坏现有功能，并能在 SolidWorks 中打开现代 cockpit UI。

## 检查清单

### 文件

确认 deploy 中存在：

- `SwAgentAddin.dll`
- `config.json`
- `property-workbench\index.html`
- `property-workbench\app.js`
- `property-workbench\styles.css`
- `property-workbench\mock-data.js`
- `cockpit-contracts\*.json`

### 配置

`D:\SWAgentAddin\config.json` 必须包含：

- `cockpit_enabled`
- `cockpit_url_mode`
- `cockpit_entry`
- `cockpit_prefer_webview2`

### 构建

```powershell
$env:SW_HOME='D:\Program Files\SW\2022\SOLIDWORKS'
dotnet build output\SW-Agent-Addin\SwAgentAddin.csproj -c Release
```

必须 0 警告 0 错误。

### 部署

复制到：

`D:\SWAgentAddin`

不要漏掉子目录：

- `property-workbench`
- `cockpit-contracts`

### SolidWorks 实机

验证：

1. 插件可勾选。
2. 新按钮 `Agent驾驶舱` 可见。
3. 点击后打开 WebView2 cockpit。
4. cockpit 显示当前文件。
5. 零件属性表可显示。
6. 装配体树可显示。
7. 表格筛选/搜索可用。
8. 树表联动可用。
9. 现有 `属性填写` 仍正常。
10. 现有 `读取属性` WinForms fallback 仍正常。

### WebView2 缺失

如果目标机没有 WebView2 Runtime：

- 插件不能崩。
- 弹中文提示。
- 说明安装 WebView2 Runtime 或使用 fallback。

## 交付总结

必须输出：

- 构建结果。
- 部署文件清单。
- WebView2 是否可用。
- Cockpit 实机截图或描述。
- 已通过/未通过项。
- 下一轮建议。
