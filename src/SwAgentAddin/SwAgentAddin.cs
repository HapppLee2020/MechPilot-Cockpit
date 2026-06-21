using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SwAgentAddin
{
    /// <summary>
    /// MechPilot — SolidWorks 2022 Add-in
    /// 双模式：本地模式直接写属性，远程模式提交任务到 Agent Server。
    /// </summary>
    [ComVisible(true)]
    [Guid("E8F5C9A2-3D14-4E7F-9A1B-C6D5E4F3A2B1")]
    [ProgId("SwAgentAddin.Connect")]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class SwAgentAddin : SolidWorks.Interop.swpublished.ISwAddin
    {
        #region Constants

        private const string AddinTitle = "MechPilot";
        private const string CommandTabName = "MechPilot";
        private const int CommandGroupId = 1001;
        private const int FirstMenuId = 0;

        // Command IDs — must match ImageListIndex in 7-icon strip
        private const int CmdCockpit = 0;
        private const int CmdPropertyFill = 1;
        private const int CmdReadProperties = 2;
        private const int CmdPropertyCheck = 3;
        private const int CmdDrawingReview = 4;
        private const int CmdShowPanel = 5;
        private const int CmdSettings = 6;

        #endregion

        #region Member Variables

        private ISldWorks _swApp;
        private ICommandManager _cmdMgr;
        private ITaskpaneView _taskpaneView;
        private AddinConfig _config;
        private LocalPropertyRules _rules;
        private int _addinCookie;
        private readonly HttpClient _httpClient = new HttpClient();

        // Document event handlers
        private Hashtable _openDocuments;

        #endregion

        #region ISwAddin Implementation

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            WriteTrace("ConnectToSW entered. Cookie=" + Cookie);

            try
            {
                _swApp = (ISldWorks)ThisSW;
                _addinCookie = Cookie;
                _openDocuments = new Hashtable();

                // Register callback object — SW uses this to route CommandManager events
                bool regResult = _swApp.SetAddinCallbackInfo2(0, this, Cookie);
                WriteTrace("SetAddinCallbackInfo2 result: " + regResult);

                // Get Command Manager
                _cmdMgr = _swApp.GetCommandManager(Cookie);
                if (_cmdMgr == null)
                {
                    WriteTrace("FATAL: GetCommandManager returned null.");
                    return false;
                }
                WriteTrace("CommandManager obtained.");

                LoadConfig();
                LoadRules();
                AddCommandMgr();
                AddTaskPane();

                // Attach event handlers
                AttachEventHandlers();

                _config.Log("Add-in loaded successfully. Mode=" + _config.ExecutionMode + " Cookie=" + Cookie);
                WriteTrace("ConnectToSW completed successfully. Mode=" + _config.ExecutionMode);
                return true;
            }
            catch (Exception ex)
            {
                WriteTrace("ConnectToSW EXCEPTION: " + ex.ToString());
                SafeMessage("插件加载失败：" + ex.Message, MessageBoxIcon.Warning);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            WriteTrace("DisconnectFromSW entered.");

            try
            {
                RemoveCommandMgr();
                RemoveTaskPane();
                DetachEventHandlers();
                _config?.Log("Add-in unloaded.");
            }
            catch (Exception ex)
            {
                WriteTrace("DisconnectFromSW exception: " + ex);
            }

            _taskpaneView = null;
            _cmdMgr = null;
            _swApp = null;
            return true;
        }

        #endregion

        #region UI — Command Manager

        private void AddCommandMgr()
        {
            WriteTrace("AddCommandMgr: creating command group...");

            int[] menuIds = new int[5];
            int cmdGroupErr = 0;
            _cmdMgr.CreateCommandGroup2(
                CommandGroupId,
                "MechPilot",
                "MechPilot tools",
                "MechPilot task tools",
                -1,
                true,
                ref cmdGroupErr);

            // Get the command group back
            ICommandGroup cmdGroup = _cmdMgr.GetCommandGroup(CommandGroupId);
            if (cmdGroup == null)
            {
                WriteTrace("AddCommandMgr: GetCommandGroup returned null. cmdGroupErr=" + cmdGroupErr);
                return;
            }

            // Add menu items — callback method names must be public void methods on this class
            EnsureIconFiles();
            ApplyCommandGroupIcons(cmdGroup);
            cmdGroup.ShowInDocumentType =
                (int)swDocTemplateTypes_e.swDocTemplateTypePART |
                (int)swDocTemplateTypes_e.swDocTemplateTypeASSEMBLY |
                (int)swDocTemplateTypes_e.swDocTemplateTypeDRAWING;

            cmdGroup.AddCommandItem2("Agent驾驶舱", -1,
                "打开 Agent 驾驶舱", "Agent驾驶舱",
                0, "SwCmd_Cockpit", "", CmdCockpit,
                (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);

            cmdGroup.AddCommandItem2(
                "属性填写",       // Name
                -1,                     // Position
                "按当前模式填写自定义属性",  // HintString
                "属性填写",       // ToolTip
                1,                      // ImageListIndex
                "SwCmd_PropertyFill",  // CallbackFunction
                "",                     // EnableMethod (empty = always enabled)
                CmdPropertyFill,       // UserID
                (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);

            cmdGroup.AddCommandItem2("读取属性", -1,
                "读取当前文档自定义属性", "读取属性",
                2, "SwCmd_ReadProperties", "", CmdReadProperties,
                (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);

            cmdGroup.AddCommandItem2("属性检查", -1,
                "检查当前模型自定义属性", "属性检查",
                3, "SwCmd_PropertyCheck", "", CmdPropertyCheck,
                (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);

            cmdGroup.AddCommandItem2("图纸审核", -1,
                "提交当前工程图审核", "图纸审核",
                4, "SwCmd_DrawingReview", "", CmdDrawingReview,
                (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);

            cmdGroup.AddCommandItem2("任务面板", -1,
                "打开 MechPilot 任务面板", "任务面板",
                5, "SwCmd_ShowPanel", "", CmdShowPanel,
                (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);

            cmdGroup.AddCommandItem2("插件设置", -1,
                "打开配置文件", "插件设置",
                6, "SwCmd_Settings", "", CmdSettings,
                (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);

            cmdGroup.HasMenu = true;
            cmdGroup.HasToolbar = true;
            cmdGroup.Activate();
            AddCommandTabs(cmdGroup);

            WriteTrace("AddCommandMgr: command group activated.");
        }

        private void ApplyCommandGroupIcons(ICommandGroup cmdGroup)
        {
            try
            {
                string dir = GetAddinDirectory();
                cmdGroup.SmallMainIcon = Path.Combine(dir, "assets/icons/mechpilot-main-20.bmp");
                cmdGroup.LargeMainIcon = Path.Combine(dir, "assets/icons/mechpilot-main-32.bmp");
                cmdGroup.SmallIconList = Path.Combine(dir, "assets/icons/mechpilot-icons-7-20.bmp");
                cmdGroup.LargeIconList = Path.Combine(dir, "assets/icons/mechpilot-icons-7-32.bmp");
                WriteTrace("ApplyCommandGroupIcons: icons assigned.");
            }
            catch (Exception ex)
            {
                WriteTrace("ApplyCommandGroupIcons exception: " + ex);
            }
        }

        private void AddCommandTabs(ICommandGroup cmdGroup)
        {
            int[] docTypes =
            {
                (int)swDocumentTypes_e.swDocPART,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swDocumentTypes_e.swDocDRAWING
            };

            foreach (int docType in docTypes)
            {
                try
                {
                    CommandTab existingTab = _cmdMgr.GetCommandTab(docType, CommandTabName);
                    if (existingTab != null)
                    {
                        _cmdMgr.RemoveCommandTab(existingTab);
                        WriteTrace("AddCommandTabs: removed existing tab. docType=" + docType);
                    }

                    CommandTab tab = _cmdMgr.AddCommandTab(docType, CommandTabName);
                    if (tab == null)
                    {
                        WriteTrace("AddCommandTabs: AddCommandTab returned null. docType=" + docType);
                        continue;
                    }

                    CommandTabBox box = tab.AddCommandTabBox();
                    if (box == null)
                    {
                        WriteTrace("AddCommandTabs: AddCommandTabBox returned null. docType=" + docType);
                        continue;
                    }

                    int[] commandIds =
                    {
                        cmdGroup.get_CommandID(CmdCockpit),
                        cmdGroup.get_CommandID(CmdPropertyFill),
                        cmdGroup.get_CommandID(CmdReadProperties),
                        cmdGroup.get_CommandID(CmdPropertyCheck),
                        cmdGroup.get_CommandID(CmdDrawingReview),
                        cmdGroup.get_CommandID(CmdShowPanel),
                        cmdGroup.get_CommandID(CmdSettings)
                    };

                    int[] textStyles =
                    {
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow
                    };

                    bool added = box.AddCommands(commandIds, textStyles);
                    tab.Visible = true;
                    tab.Active = true;
                    WriteTrace("AddCommandTabs: built. docType=" + docType + ", added=" + added + ", ids=" + string.Join(",", commandIds));
                }
                catch (Exception ex)
                {
                    WriteTrace("AddCommandTabs exception. docType=" + docType + ": " + ex);
                }
            }
        }

        private static void EnsureIconFiles()
        {
            string dir = GetAddinDirectory();
            string[] required = { "assets/icons/mechpilot-main-20.bmp", "assets/icons/mechpilot-main-32.bmp", "assets/icons/mechpilot-icons-7-20.bmp", "assets/icons/mechpilot-icons-7-32.bmp" };
            foreach (string f in required)
            {
                if (!File.Exists(Path.Combine(dir, f)))
                    WriteTrace("WARNING: icon file missing: " + f + " — toolbar icons may not display.");
            }
        }

        private static void CreateIconStrip(string path, int size)
        {
            if (File.Exists(path))
                return;

            using (var bmp = new Bitmap(size * 5, size))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);
                for (int i = 0; i < 5; i++)
                    DrawIcon(g, new Rectangle(i * size, 0, size, size), i);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
            }
        }

        private static void CreateSingleIcon(string path, int size, int iconIndex)
        {
            if (File.Exists(path))
                return;

            using (var bmp = new Bitmap(size, size))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);
                DrawIcon(g, new Rectangle(0, 0, size, size), iconIndex);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
            }
        }

        private static void DrawIcon(Graphics g, Rectangle bounds, int iconIndex)
        {
            Color[] colors =
            {
                Color.FromArgb(31, 79, 143),
                Color.FromArgb(11, 125, 112),
                Color.FromArgb(181, 101, 29),
                Color.FromArgb(79, 70, 229),
                Color.FromArgb(82, 82, 91)
            };

            int pad = Math.Max(2, bounds.Width / 8);
            Rectangle r = new Rectangle(bounds.X + pad, bounds.Y + pad, bounds.Width - pad * 2, bounds.Height - pad * 2);
            using (var bg = new SolidBrush(colors[iconIndex]))
            using (var white = new Pen(Color.White, Math.Max(2, bounds.Width / 12)))
            using (var whiteBrush = new SolidBrush(Color.White))
            {
                g.FillRectangle(bg, r);

                if (iconIndex == 0)
                {
                    g.DrawLine(white, r.Left + r.Width / 4, r.Top + r.Height / 2, r.Left + r.Width / 2, r.Bottom - r.Height / 4);
                    g.DrawLine(white, r.Left + r.Width / 2, r.Bottom - r.Height / 4, r.Right - r.Width / 4, r.Top + r.Height / 4);
                }
                else if (iconIndex == 1)
                {
                    g.DrawEllipse(white, r.Left + r.Width / 5, r.Top + r.Height / 5, r.Width / 2, r.Height / 2);
                    g.DrawLine(white, r.Left + r.Width * 3 / 5, r.Top + r.Height * 3 / 5, r.Right - r.Width / 6, r.Bottom - r.Height / 6);
                }
                else if (iconIndex == 2)
                {
                    Point[] pts =
                    {
                        new Point(r.Left + r.Width / 2, r.Top + r.Height / 5),
                        new Point(r.Right - r.Width / 5, r.Bottom - r.Height / 5),
                        new Point(r.Left + r.Width / 5, r.Bottom - r.Height / 5)
                    };
                    g.FillPolygon(whiteBrush, pts);
                }
                else if (iconIndex == 3)
                {
                    g.FillRectangle(whiteBrush, r.Left + r.Width / 4, r.Top + r.Height / 4, r.Width / 2, r.Height / 2);
                }
                else
                {
                    g.DrawEllipse(white, r.Left + r.Width / 4, r.Top + r.Height / 4, r.Width / 2, r.Height / 2);
                    g.DrawLine(white, r.Left + r.Width / 2, r.Top + r.Height / 6, r.Left + r.Width / 2, r.Bottom - r.Height / 6);
                    g.DrawLine(white, r.Left + r.Width / 6, r.Top + r.Height / 2, r.Right - r.Width / 6, r.Top + r.Height / 2);
                }
            }
        }

        private void RemoveCommandMgr()
        {
            try
            {
                if (_cmdMgr != null)
                {
                    _cmdMgr.RemoveCommandGroup(CommandGroupId);
                    WriteTrace("RemoveCommandMgr: removed.");
                }
            }
            catch (Exception ex)
            {
                WriteTrace("RemoveCommandMgr exception: " + ex);
            }
        }

        #endregion

        #region UI — Task Pane

        private void AddTaskPane()
        {
            try
            {
                WriteTrace("AddTaskPane: creating...");
                _taskpaneView = _swApp.CreateTaskpaneView3(null, "MechPilot");
                if (_taskpaneView != null)
                {
                    string htmlPath = Path.Combine(GetAddinDirectory(), "frontend/taskpane.html");
                    if (!File.Exists(htmlPath))
                        File.WriteAllText(htmlPath, TaskpaneHtml.DefaultHtml, Encoding.UTF8);

                    object control = _taskpaneView.AddControl("Shell.Explorer.2", "");
                    if (control != null)
                    {
                        dynamic browser = control;
                        browser.Navigate("file:///" + htmlPath.Replace("\\", "/"));
                    }
                    WriteTrace("AddTaskPane: created with HTML panel.");
                }
                else
                {
                    WriteTrace("AddTaskPane: CreateTaskpaneView3 returned null.");
                }
            }
            catch (Exception ex)
            {
                WriteTrace("AddTaskPane exception: " + ex);
            }
        }

        private void RemoveTaskPane()
        {
            try
            {
                if (_taskpaneView != null)
                {
                    _taskpaneView.DeleteView();
                    _taskpaneView = null;
                    WriteTrace("RemoveTaskPane: deleted.");
                }
            }
            catch (Exception ex)
            {
                WriteTrace("RemoveTaskPane exception: " + ex);
            }
        }

        private void ShowTaskPane()
        {
            try
            {
                if (_taskpaneView != null)
                {
                    _taskpaneView.ShowView();
                    WriteTrace("ShowTaskPane: shown.");
                }
                else
                {
                    AddTaskPane();
                }
            }
            catch (Exception ex)
            {
                WriteTrace("ShowTaskPane exception: " + ex);
                SafeMessage("任务面板错误：" + ex.Message, MessageBoxIcon.Warning);
            }
        }

        #endregion

        #region Event Handlers

        private void AttachEventHandlers()
        {
            try
            {
                AttachSWEvents();
                WriteTrace("Event handlers attached.");
            }
            catch (Exception ex)
            {
                WriteTrace("AttachEventHandlers exception: " + ex);
            }
        }

        private void DetachEventHandlers()
        {
            try
            {
                DetachSWEvents();
                WriteTrace("Event handlers detached.");
            }
            catch (Exception ex)
            {
                WriteTrace("DetachEventHandlers exception: " + ex);
            }
        }

        private void AttachSWEvents()
        {
            // SW event attachment — placeholder for document open/close events
        }

        private void DetachSWEvents()
        {
            // SW event detachment — placeholder
        }

        #endregion

        #region Command Callbacks — SW calls these by name via reflection

        private CockpitForm _cockpitForm;

        public void SwCmd_Cockpit()
        {
            try
            {
                if (!_config.CockpitEnabled)
                {
                    SafeMessage("Agent驾驶舱已在配置中禁用。", MessageBoxIcon.Information);
                    return;
                }

                // If already open, bring to front
                if (_cockpitForm != null && !_cockpitForm.IsDisposed)
                {
                    _cockpitForm.BringToFront();
                    _cockpitForm.WindowState = FormWindowState.Maximized;
                    return;
                }

                _cockpitForm = new CockpitForm(_config, BuildCockpitContext, HandleCockpitCommand);
                _cockpitForm.FormClosed += (s, e) => { _cockpitForm = null; };
                _cockpitForm.Show();
            }
            catch (Exception ex)
            {
                WriteTrace("SwCmd_Cockpit exception: " + ex);

                // WebView2 runtime missing fallback
                if (ex.Message.Contains("WebView2") || ex.InnerException != null &&
                    ex.InnerException.Message.Contains("WebView2"))
                {
                    SafeMessage(
                        "WebView2 运行时未安装。\n\n" +
                        "Agent驾驶舱需要 Microsoft WebView2 Runtime。\n" +
                        "请从 https://developer.microsoft.com/en-us/microsoft-edge/webview2/ 下载安装后重试。\n\n" +
                        "当前可继续使用其他 MechPilot 功能。",
                        MessageBoxIcon.Warning);
                }
                else
                {
                    SafeMessage("Agent驾驶舱打开失败：" + ex.Message, MessageBoxIcon.Error);
                }
            }
        }

        public void SwCmd_PropertyFill()
        {
            ExecuteTask("property_fill", "Property Fill");
        }

        public void SwCmd_PropertyCheck()
        {
            ExecuteTask("property_check", "Property Check");
        }

        public void SwCmd_DrawingReview()
        {
            ExecuteTask("drawing_review", "Drawing Review");
        }

        public void SwCmd_ShowPanel()
        {
            ShowTaskPane();
        }

        public void SwCmd_Settings()
        {
            OpenConfigFile();
        }

        public void SwCmd_ReadProperties()
        {
            ExecuteReadProperties();
        }

        /// <summary>
        /// WebView2 cockpit 命令桥 — 前端 JS 通过 WebMessageReceived 发送 JSON 命令
        /// </summary>
        private string HandleCockpitCommand(string commandJson)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var envelope = serializer.Deserialize<Dictionary<string, object>>(commandJson);
                if (envelope == null || !envelope.ContainsKey("command"))
                    return MakeCockpitResult(null, false, "NO_COMMAND", "缺少 command 字段");

                string command = Convert.ToString(envelope["command"]);
                string requestId = envelope.ContainsKey("request_id") ? Convert.ToString(envelope["request_id"]) : "";

                switch (command)
                {
                    case "cockpit.ping":
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object>
                        {
                            ["pong"] = true,
                            ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            ["engineer_id"] = _config?.EngineerId ?? ""
                        });

                    case "local.read_properties":
                        string contextJson = BuildCockpitContext();
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object>
                        {
                            ["context"] = serializer.DeserializeObject(contextJson)
                        });

                    case "refresh_context":
                        string ctx = BuildCockpitContext();
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object>
                        {
                            ["context"] = serializer.DeserializeObject(ctx)
                        });

                    case "window_close":
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object> { ["action"] = "close" });

                    case "window_minimize":
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object> { ["action"] = "minimize" });

                    case "window_maximize":
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object> { ["action"] = "maximize" });

                    default:
                        return MakeCockpitResult(requestId, false, "UNKNOWN_COMMAND",
                            "未实现的命令: " + command + "。当前支持: cockpit.ping, local.read_properties, refresh_context");
                }
            }
            catch (Exception ex)
            {
                WriteTrace("HandleCockpitCommand error: " + ex);
                return MakeCockpitResult(null, false, "EXCEPTION", ex.Message);
            }
        }

        private string MakeCockpitResult(string requestId, bool success, string errorCode, string errorMsg,
            Dictionary<string, object> data = null)
        {
            var result = new Dictionary<string, object>
            {
                ["request_id"] = requestId ?? "",
                ["success"] = success,
                ["timestamp_utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
            if (!success && errorCode != null)
            {
                result["error"] = new Dictionary<string, string>
                {
                    ["code"] = errorCode,
                    ["message"] = errorMsg ?? ""
                };
            }
            if (data != null)
                result["data"] = data;

            return new JavaScriptSerializer().Serialize(result);
        }

        #endregion

        #region Task Execution — Dispatch

        private void ExecuteTask(string taskType, string displayName)
        {
            try
            {
                ShowTaskPane();

                if (string.Equals(_config.ExecutionMode, "local", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteLocalTask(taskType, displayName);
                    return;
                }

                ExecuteRemoteTask(taskType, displayName);
            }
            catch (Exception ex)
            {
                WriteTrace("ExecuteTask failed: " + ex);
                SafeMessage(displayName + " failed: " + ex.Message, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Local Execution

        private void ExecuteLocalTask(string taskType, string displayName)
        {
            WriteTrace("ExecuteLocalTask: " + taskType);

            // Resolve targets
            List<ResolvedTarget> targets = ResolveTargets();
            if (targets == null || targets.Count == 0)
            {
                SafeMessage("未找到可更新目标。请先打开并保存一个 SolidWorks 文档；如果在装配体中操作，可选择一个或多个零部件。", MessageBoxIcon.Warning);
                return;
            }

            // Multi-select confirmation
            if (targets.Count > 1 && _config.ConfirmBeforeWrite)
            {
                string list = string.Join("\n", targets.Select(t => "  - " + t.DisplayName + " [" + t.DocType + "]"));
                var result = MessageBox.Show(
                    "即将更新 " + targets.Count + " 个目标：\n\n" + list + "\n\n是否继续？",
                    AddinTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                {
                    WriteTrace("ExecuteLocalTask: 用户取消了多目标更新。");
                    return;
                }
            }

            // Get property rules for the target doc type
            Dictionary<string, string> properties = _rules.GetPropertySet(taskType, targets[0].DocType);
            if (properties == null || properties.Count == 0)
            {
                SafeMessage("未找到属性规则：task='" + taskType + "' docType='" + targets[0].DocType + "'。\n请检查 rules.local.json。", MessageBoxIcon.Warning);
                return;
            }

            // Execute writes
            int succeeded = 0;
            int failed = 0;
            var messages = new List<string>();
            var allChanged = new HashSet<string>();

            foreach (ResolvedTarget target in targets)
            {
                // Substitute placeholders per target
                var resolved = new Dictionary<string, string>();
                foreach (var kv in properties)
                {
                    resolved[kv.Key] = SubstitutePlaceholders(kv.Value, target);
                }

                bool ok = WritePropertiesToTarget(target, resolved, messages);
                if (ok)
                {
                    succeeded++;
                    foreach (var kv in resolved)
                        allChanged.Add(kv.Key);
                }
                else
                {
                    failed++;
                }
            }

            // Report
            string changed = allChanged.Count > 0
                ? string.Join("\n", allChanged.Select(c => "  - " + c))
                : "  (none)";

            string report = string.Format(
                "MechPilot 本地更新完成。\n\n目标数量：{0}\n成功：{1}\n失败：{2}\n已更新属性：\n{3}",
                targets.Count, succeeded, failed, changed);

            if (messages.Count > 0)
                report += "\n\nDetails:\n" + string.Join("\n", messages.Select(m => "  " + m));

            _config.Log("Local " + taskType + ": " + succeeded + "/" + targets.Count + " succeeded");
            WriteTrace("ExecuteLocalTask done: " + succeeded + "/" + targets.Count);
            SafeMessage(report, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private List<ResolvedTarget> ResolveTargets()
        {
            try
            {
                IModelDoc2 activeDoc = _swApp?.ActiveDoc as IModelDoc2;
                if (activeDoc == null)
                    return null;

                int docType = activeDoc.GetType();
                string filePath = activeDoc.GetPathName();
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    WriteTrace("ResolveTargets: document not saved yet.");
                    return null;
                }

                string docTypeName = GetDocTypeName(docType);

                // Part or Drawing: target is the active document itself
                if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocPART ||
                    (swDocumentTypes_e)docType == swDocumentTypes_e.swDocDRAWING)
                {
                    return new List<ResolvedTarget>
                    {
                        new ResolvedTarget
                        {
                            DisplayName = activeDoc.GetTitle(),
                            FilePath = filePath,
                            DocType = docTypeName,
                            Model = activeDoc,
                            Component = null
                        }
                    };
                }

                // Assembly: check selection
                if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocASSEMBLY)
                {
                    ISelectionMgr selMgr = (ISelectionMgr)activeDoc.SelectionManager;
                    int selCount = selMgr.GetSelectedObjectCount2(-1); // -1 = all marks

                    if (selCount == 0)
                    {
                        // No selection — target is the assembly itself
                        return new List<ResolvedTarget>
                        {
                            new ResolvedTarget
                            {
                                DisplayName = activeDoc.GetTitle(),
                                FilePath = filePath,
                                DocType = "assembly",
                                Model = activeDoc,
                                Component = null
                            }
                        };
                    }

                    // Has selection — extract IComponent2 objects
                    var targets = new List<ResolvedTarget>();
                    for (int i = 1; i <= selCount; i++)
                    {
                        try
                        {
                            object selObj = selMgr.GetSelectedObject6(i, -1);
                            IComponent2 comp = selObj as IComponent2;
                            if (comp == null)
                            {
                                WriteTrace("ResolveTargets: selection " + i + " is not IComponent2, skipping.");
                                continue;
                            }

                            IModelDoc2 compModel = (IModelDoc2)comp.GetModelDoc2();
                            if (compModel == null)
                            {
                                WriteTrace("ResolveTargets: component " + i + " GetModelDoc2 returned null, skipping.");
                                continue;
                            }

                            string compPath = compModel.GetPathName();
                            string compTypeName = GetDocTypeName(compModel.GetType());

                            targets.Add(new ResolvedTarget
                            {
                                DisplayName = comp.Name2 ?? compModel.GetTitle(),
                                FilePath = compPath,
                                DocType = compTypeName,
                                Model = compModel,
                                Component = comp
                            });
                        }
                        catch (Exception ex)
                        {
                            WriteTrace("ResolveTargets: error processing selection " + i + ": " + ex.Message);
                        }
                    }

                    if (targets.Count == 0)
                    {
                        // Fallback to assembly itself
                        return new List<ResolvedTarget>
                        {
                            new ResolvedTarget
                            {
                                DisplayName = activeDoc.GetTitle(),
                                FilePath = filePath,
                                DocType = "assembly",
                                Model = activeDoc,
                                Component = null
                            }
                        };
                    }

                    return targets;
                }

                return null;
            }
            catch (Exception ex)
            {
                WriteTrace("ResolveTargets failed: " + ex);
                return null;
            }
        }

        private static string GetDocTypeName(int docType)
        {
            if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocPART) return "part";
            if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocASSEMBLY) return "assembly";
            if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocDRAWING) return "drawing";
            return "unknown";
        }

        private string SubstitutePlaceholders(string template, ResolvedTarget target)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            string fileName = Path.GetFileName(target.FilePath);
            string fileNameNoExt = Path.GetFileNameWithoutExtension(target.FilePath);

            return template
                .Replace("{file_name}", fileName ?? "")
                .Replace("{file_name_no_ext}", fileNameNoExt ?? "")
                .Replace("{doc_type}", target.DocType ?? "")
                .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"))
                .Replace("{engineer_id}", _config.EngineerId ?? "")
                .Replace("{mode}", _config.ExecutionMode ?? "local");
        }

        private bool WritePropertiesToTarget(ResolvedTarget target, Dictionary<string, string> properties, List<string> messages)
        {
            try
            {
                CustomPropertyManager mgr = target.Model.Extension.get_CustomPropertyManager("");
                if (mgr == null)
                {
                    messages.Add(target.DisplayName + ": CustomPropertyManager unavailable.");
                    return false;
                }

                foreach (var kv in properties)
                {
                    try
                    {
                        // swCustomPropertyAddOption_e.swCustomPropertyReplaceValue = 1
                        // swCustomInfoType_e.swCustomInfoText = 30
                        mgr.Add3(kv.Key, 30, kv.Value, 1);
                        WriteTrace("  WriteProperty: " + target.DisplayName + " / " + kv.Key + " = " + kv.Value);
                    }
                    catch (Exception ex)
                    {
                        messages.Add(target.DisplayName + " / " + kv.Key + ": " + ex.Message);
                        WriteTrace("  WriteProperty FAILED: " + kv.Key + " - " + ex.Message);
                    }
                }

                // Rebuild and mark dirty
                try { target.Model.ForceRebuild3(false); } catch { }
                try { target.Model.SetSaveFlag(); } catch { }

                messages.Add(target.DisplayName + ": " + properties.Count + " properties updated.");
                return true;
            }
            catch (Exception ex)
            {
                messages.Add(target.DisplayName + ": write failed - " + ex.Message);
                WriteTrace("WritePropertiesToTarget failed: " + ex);
                return false;
            }
        }

        #endregion

        #region Read Properties

        private void ExecuteReadProperties()
        {
            try
            {
                WriteTrace("ExecuteReadProperties entered.");

                IModelDoc2 activeDoc = _swApp?.ActiveDoc as IModelDoc2;
                if (activeDoc == null)
                {
                    SafeMessage("请先打开一个 SolidWorks 文档。", MessageBoxIcon.Warning);
                    return;
                }

                string filePath = activeDoc.GetPathName();
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    SafeMessage("请先保存文档后再读取属性。", MessageBoxIcon.Warning);
                    return;
                }

                int docType = activeDoc.GetType();
                var rows = new List<PropertyReadRow>();
                int totalCount = 0;

                if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocPART)
                {
                    totalCount = 1;
                    ReadModelProperties(activeDoc, activeDoc.GetTitle(), "", filePath, "part", 1, rows);
                }
                else if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocDRAWING)
                {
                    ReadDrawingProperties(activeDoc, rows, ref totalCount);
                }
                else if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocASSEMBLY)
                {
                    ISelectionMgr selMgr = (ISelectionMgr)activeDoc.SelectionManager;
                    int selCount = selMgr.GetSelectedObjectCount2(-1);

                    if (selCount > 0)
                    {
                        for (int i = 1; i <= selCount; i++)
                        {
                            try
                            {
                                object selObj = selMgr.GetSelectedObject6(i, -1);
                                IComponent2 comp = selObj as IComponent2;
                                if (comp == null) continue;
                                IModelDoc2 compModel = (IModelDoc2)comp.GetModelDoc2();
                                if (compModel == null) { WriteTrace("组件 " + i + " 模型不可用，跳过。"); continue; }
                                string compPath = compModel.GetPathName();
                                string compName = comp.Name2 ?? compModel.GetTitle();
                                string compDocType = GetDocTypeName(compModel.GetType());
                                totalCount++;
                                ReadModelProperties(compModel, compName, compName, compPath, compDocType, 1, rows);
                            }
                            catch (Exception ex) { WriteTrace("选择组件 " + i + " 错误: " + ex.Message); }
                        }
                    }
                    else
                    {
                        ReadAssemblyAllComponents(activeDoc, rows, ref totalCount);
                    }
                }

                if (rows.Count == 0) { SafeMessage("未读取到任何自定义属性。", MessageBoxIcon.Information); return; }

                string docTypeName = GetDocTypeName(docType);
                ShowPropertyReadForm(rows, docTypeName, totalCount, activeDoc.GetTitle());
            }
            catch (Exception ex)
            {
                WriteTrace("ExecuteReadProperties failed: " + ex);
                SafeMessage("读取属性失败：" + ex.Message, MessageBoxIcon.Error);
            }
        }

        private void ReadModelProperties(IModelDoc2 model, string targetName, string localCompName, string filePath, string docType, int quantity, List<PropertyReadRow> rows)
        {
            try
            {
                CustomPropertyManager mgr = model.Extension.get_CustomPropertyManager("");
                if (mgr == null) return;
                object propNames = mgr.GetNames();
                if (propNames == null) return;
                string[] names = (string[])propNames;
                foreach (string name in names)
                {
                    string rawVal = ""; string resolvedVal = "";
                    try { mgr.Get2(name, out rawVal, out resolvedVal); } catch { }
                    rows.Add(new PropertyReadRow
                    {
                        TargetName = targetName, LocalComponentName = localCompName,
                        FilePath = filePath, ConfigurationName = "(默认)",
                        PropertyName = name, RawValue = rawVal ?? "",
                        ResolvedValue = resolvedVal ?? "", Source = docType, Quantity = quantity
                    });
                }
            }
            catch (Exception ex) { WriteTrace("ReadModelProperties failed: " + ex.Message); }
        }

        private void ReadDrawingProperties(IModelDoc2 drawingDoc, List<PropertyReadRow> rows, ref int totalCount)
        {
            try
            {
                IDrawingDoc drawDoc = drawingDoc as IDrawingDoc;
                if (drawDoc == null) { SafeMessage("无法读取工程图文档。", MessageBoxIcon.Warning); return; }
                IView firstView = (IView)drawDoc.GetFirstView();
                if (firstView == null) { SafeMessage("工程图中未找到视图。", MessageBoxIcon.Warning); return; }
                IView modelView = (IView)firstView.GetNextView();
                if (modelView == null) { SafeMessage("工程图中未找到模型视图。", MessageBoxIcon.Warning); return; }
                IModelDoc2 refDoc = (IModelDoc2)modelView.ReferencedDocument;
                if (refDoc == null) { SafeMessage("无法获取主视图关联模型。", MessageBoxIcon.Warning); return; }
                totalCount = 1;
                string refTitle = refDoc.GetTitle();
                string refPath = refDoc.GetPathName();
                string refDocType = GetDocTypeName(refDoc.GetType());
                ReadModelProperties(refDoc, refTitle, refTitle, refPath, refDocType, 1, rows);
            }
            catch (Exception ex) { WriteTrace("ReadDrawingProperties failed: " + ex); SafeMessage("读取工程图属性失败：" + ex.Message, MessageBoxIcon.Error); }
        }

        private void ReadAssemblyAllComponents(IModelDoc2 asmDoc, List<PropertyReadRow> rows, ref int totalCount)
        {
            try
            {
                IAssemblyDoc asm = asmDoc as IAssemblyDoc;
                if (asm == null) return;
                object compObjs = asm.GetComponents(false);
                if (compObjs == null) return;
                object[] comps = compObjs as object[];
                if (comps == null) return;
                var groups = new Dictionary<string, List<IComponent2>>(StringComparer.OrdinalIgnoreCase);
                int suppressedCount = 0;
                foreach (object c in comps)
                {
                    IComponent2 comp = c as IComponent2;
                    if (comp == null) continue;
                    try { if (comp.GetSuppression() == (int)swComponentSuppressionState_e.swComponentSuppressed) { suppressedCount++; continue; } } catch { }
                    IModelDoc2 compModel = null;
                    try { compModel = (IModelDoc2)comp.GetModelDoc2(); } catch { }
                    if (compModel == null) continue;
                    string compPath = compModel.GetPathName();
                    if (string.IsNullOrWhiteSpace(compPath)) continue;
                    if (!groups.ContainsKey(compPath)) groups[compPath] = new List<IComponent2>();
                    groups[compPath].Add(comp);
                }
                totalCount = comps.Length - suppressedCount;
                foreach (var kv in groups)
                {
                    IComponent2 firstComp = kv.Value[0];
                    IModelDoc2 model = null;
                    try { model = (IModelDoc2)firstComp.GetModelDoc2(); } catch { }
                    if (model == null) continue;
                    string compName = firstComp.Name2 ?? model.GetTitle();
                    string docType = GetDocTypeName(model.GetType());
                    ReadModelProperties(model, compName, compName, kv.Key, docType, kv.Value.Count, rows);
                }
                if (suppressedCount > 0) WriteTrace("跳过 " + suppressedCount + " 个被抑制的组件。");
            }
            catch (Exception ex) { WriteTrace("ReadAssemblyAllComponents failed: " + ex); }
        }

        private void ShowPropertyReadForm(List<PropertyReadRow> rows, string docType, int targetCount, string currentFileName)
        {
            try
            {
                var pivotRows = BuildPivotRows(rows);
                var allPropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows) allPropertyNames.Add(row.PropertyName);
                var orderedPropNames = BuildPropertyColumnNames(allPropertyNames);

                // === Form ===
                var form = new Form();
                form.Text = "MechPilot - 读取属性";
                form.StartPosition = FormStartPosition.CenterScreen;
                form.Font = new Font("Microsoft YaHei UI", 9f);
                form.MinimumSize = new Size(1200, 700);
                var screen = Screen.PrimaryScreen.WorkingArea;
                form.Width = Math.Min(_config.ReadFormWidth > 0 ? _config.ReadFormWidth : 2700, (int)(screen.Width * 0.98));
                form.Height = Math.Min(_config.ReadFormHeight > 0 ? _config.ReadFormHeight : 1500, (int)(screen.Height * 0.98));

                // === Top panel: summary + tree toggle + raw toggle ===
                int uniqueTargets = pivotRows.Count;
                int totalQty = pivotRows.Sum(r => r.Quantity);
                string summaryText = string.Format("当前文件：{0}　|　文档类型：{1}　|　目标数量：{2}　|　属性列：{3}",
                    currentFileName ?? "(未知)", docType, uniqueTargets, orderedPropNames.Count);
                if (totalQty > uniqueTargets) summaryText += string.Format("　|　零部件总数量：{0}", totalQty);

                var topPanel = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.FromArgb(240, 240, 245) };
                var lblSummary = new Label { Text = summaryText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
                var chkRaw = new CheckBox { Text = "显示属性值", Dock = DockStyle.Right, Width = 120, Checked = _config.ReadShowRawValueDefault, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 8, 0) };
                bool isAssembly = docType == "assembly";
                var chkTree = new CheckBox { Text = "装配树", Dock = DockStyle.Right, Width = 80, Checked = isAssembly && _config.ReadShowAssemblyTreeDefault, TextAlign = ContentAlignment.MiddleRight, Enabled = isAssembly, Padding = new Padding(0, 0, 4, 0) };
                topPanel.Controls.Add(lblSummary);
                topPanel.Controls.Add(chkRaw);
                topPanel.Controls.Add(chkTree);

                // === DataGridView ===
                var grid = new DataGridView();
                grid.Dock = DockStyle.Fill; grid.ReadOnly = true;
                grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
                grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                grid.AllowUserToResizeRows = false; grid.RowTemplate.Height = 42;
                grid.Font = new Font("Microsoft YaHei UI", 9f);
                grid.BackgroundColor = Color.White; grid.DefaultCellStyle.BackColor = Color.White;
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 255);
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(200, 215, 240);
                grid.DefaultCellStyle.SelectionForeColor = Color.Black;
                grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
                grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.ColumnHeadersHeight = 36; grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                grid.EnableHeadersVisualStyles = false;

                // Build columns
                grid.Columns.Add("col_Name", "零部件名称"); grid.Columns["col_Name"].MinimumWidth = 180;
                grid.Columns.Add("col_Qty", "数量"); grid.Columns["col_Qty"].Width = 70; grid.Columns["col_Qty"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns.Add("col_DocType", "文档类型"); grid.Columns["col_DocType"].Width = 90;
                foreach (string pn in orderedPropNames) { string cn = "prop_" + pn; grid.Columns.Add(cn, pn); grid.Columns[cn].MinimumWidth = 110; grid.Columns[cn].Width = 150; grid.Columns[cn].Tag = pn; }
                grid.Columns.Add("col_FilePath", "文件路径"); grid.Columns["col_FilePath"].MinimumWidth = 280;
                grid.Columns["col_FilePath"].DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8f);
                grid.Columns["col_FilePath"].DefaultCellStyle.ForeColor = Color.FromArgb(80, 80, 80);
                grid.Columns.Add("col_FileSize", "文件大小"); grid.Columns["col_FileSize"].Width = 90; grid.Columns["col_FileSize"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

                // === Filter state + header markers ===
                var activeFilters = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                Action updateHeaders = () =>
                {
                    foreach (DataGridViewColumn col in grid.Columns)
                    {
                        string baseName = GetColumnDisplayName(col);
                        bool filtered = activeFilters.ContainsKey(baseName) && activeFilters[baseName].Count > 0;
                        col.HeaderText = filtered ? baseName + " ●" : baseName + " ▼";
                    }
                };
                // Header click for filtering
                grid.ColumnHeaderMouseClick += (s, e) =>
                {
                    DataGridViewColumn col = grid.Columns[e.ColumnIndex];
                    string colDisplayName = GetColumnDisplayName(col);
                    // Collect unique values
                    var valueSet = new SortedSet<string>();
                    foreach (var p in pivotRows)
                    {
                        string val = GetPivotDisplayValue(p, colDisplayName, orderedPropNames, chkRaw.Checked);
                        valueSet.Add(string.IsNullOrEmpty(val) ? "(空白)" : val);
                    }
                    // Popup
                    var popForm = new Form { Text = "筛选：" + colDisplayName, Width = 300, Height = 420, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedToolWindow };
                    var clb = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
                    bool hasActive = activeFilters.ContainsKey(colDisplayName) && activeFilters[colDisplayName].Count > 0;
                    foreach (string v in valueSet) clb.Items.Add(v, !hasActive || activeFilters[colDisplayName].Contains(v));
                    var popBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.RightToLeft };
                    var btnOk = new Button { Text = "确定", Width = 70 }; var btnCancel = new Button { Text = "取消", Width = 70 };
                    var btnAll = new Button { Text = "全选", Width = 60 }; var btnNone = new Button { Text = "清空", Width = 60 };
                    btnAll.Click += (s2, e2) => { for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, true); };
                    btnNone.Click += (s2, e2) => { for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, false); };
                    btnCancel.Click += (s2, e2) => popForm.Close();
                    btnOk.Click += (s2, e2) =>
                    {
                        var selected = new HashSet<string>();
                        for (int i = 0; i < clb.Items.Count; i++) if (clb.GetItemChecked(i)) selected.Add(clb.Items[i].ToString());
                        if (selected.Count == clb.Items.Count) activeFilters.Remove(colDisplayName);
                        else activeFilters[colDisplayName] = selected;
                        updateHeaders();
                        RefreshPropertyGrid(grid, pivotRows, orderedPropNames, chkRaw.Checked, activeFilters);
                        popForm.Close();
                    };
                    popBtnPanel.Controls.AddRange(new Control[] { btnCancel, btnOk, btnNone, btnAll });
                    popForm.Controls.Add(clb); popForm.Controls.Add(popBtnPanel);
                    popForm.ShowDialog(form);
                };

                // === Mapping tables for tree-grid sync ===
                var pivotKeyToRowIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var pivotKeyToTreeNodes = new Dictionary<string, List<TreeNode>>(StringComparer.OrdinalIgnoreCase);

                // === Assembly tree ===
                var treeView = new TreeView { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 9f) };
                var leftPanel = new Panel { Dock = DockStyle.Fill };
                leftPanel.Controls.Add(treeView);

                // Build real tree if assembly
                IModelDoc2 asmDocForTree = null;
                if (isAssembly)
                {
                    try
                    {
                        asmDocForTree = _swApp?.ActiveDoc as IModelDoc2;
                        if (asmDocForTree != null)
                            BuildAssemblyTree(treeView, asmDocForTree, pivotRows, pivotKeyToTreeNodes);
                    }
                    catch (Exception ex) { WriteTrace("BuildAssemblyTree failed: " + ex.Message); BuildFlatAssemblyTree(treeView, pivotRows, pivotKeyToTreeNodes); }
                }
                else leftPanel.Visible = false;

                var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 320, Panel1MinSize = 200 };
                split.Panel1.Controls.Add(leftPanel);
                split.Panel2.Controls.Add(grid);
                split.Panel1Collapsed = !isAssembly || !chkTree.Checked;
                chkTree.CheckedChanged += (s, e) => { split.Panel1Collapsed = !chkTree.Checked; };

                // === Fill grid + build index ===
                bool syncingSelection = false;
                Action<bool> doRefresh = (useRaw) =>
                {
                    RefreshPropertyGrid(grid, pivotRows, orderedPropNames, useRaw, activeFilters);
                    pivotKeyToRowIndex.Clear();
                    for (int i = 0; i < grid.Rows.Count; i++)
                    {
                        string key = GetPivotKeyFromGridRow(grid.Rows[i], pivotRows);
                        if (!string.IsNullOrEmpty(key) && !pivotKeyToRowIndex.ContainsKey(key))
                            pivotKeyToRowIndex[key] = i;
                    }
                };
                doRefresh(chkRaw.Checked);
                updateHeaders();
                chkRaw.CheckedChanged += (s, e) => doRefresh(chkRaw.Checked);

                // === Left-right sync ===
                treeView.AfterSelect += (s, e) =>
                {
                    if (syncingSelection) return;
                    syncingSelection = true;
                    try
                    {
                        var nodeData = e.Node?.Tag as AssemblyTreeNodeData;
                        if (nodeData != null && !string.IsNullOrEmpty(nodeData.PivotKey))
                        {
                            int rowIdx; if (pivotKeyToRowIndex.TryGetValue(nodeData.PivotKey, out rowIdx) && rowIdx >= 0 && rowIdx < grid.Rows.Count)
                            {
                                grid.ClearSelection();
                                grid.Rows[rowIdx].Selected = true;
                                grid.FirstDisplayedScrollingRowIndex = rowIdx;
                            }
                        }
                    }
                    finally { syncingSelection = false; }
                };
                grid.SelectionChanged += (s, e) =>
                {
                    if (syncingSelection) return;
                    if (grid.SelectedRows.Count == 0) return;
                    syncingSelection = true;
                    try
                    {
                        int rowIdx = grid.SelectedRows[0].Index;
                        // Find pivot key for this row
                        string pivotKey = GetPivotKeyFromGridRow(grid.Rows[rowIdx], pivotRows);
                        if (!string.IsNullOrEmpty(pivotKey))
                        {
                            List<TreeNode> nodes;
                            if (pivotKeyToTreeNodes.TryGetValue(pivotKey, out nodes) && nodes.Count > 0)
                            {
                                treeView.SelectedNode = nodes[0];
                                nodes[0].EnsureVisible();
                            }
                        }
                    }
                    finally { syncingSelection = false; }
                };

                // === Column auto-fit ===
                Action autoFit = () => AutoFitPropertyGridColumns(grid, orderedPropNames);
                grid.SizeChanged += (s, e) => { if (_config.ReadAutoFitColumns) autoFit(); };

                // === Assemble form ===
                form.Controls.Add(split);
                form.Controls.Add(topPanel);
                form.Shown += (s, e) => { if (_config.ReadAutoFitColumns) autoFit(); };

                _config.Log("ReadProperties v3: " + pivotRows.Count + " targets, " + orderedPropNames.Count + " prop cols");
                form.ShowDialog();
            }
            catch (Exception ex) { WriteTrace("ShowPropertyReadForm failed: " + ex); SafeMessage("显示属性表单失败：" + ex.Message, MessageBoxIcon.Error); }
        }

        // === Helper: get base display name from column (strips filter markers) ===
        private static string GetColumnDisplayName(DataGridViewColumn col)
        {
            string h = col.HeaderText;
            if (h.EndsWith(" ▼")) return h.Substring(0, h.Length - 2);
            if (h.EndsWith(" ●")) return h.Substring(0, h.Length - 2);
            return h;
        }

        // === Helper: get pivot key from grid row ===
        private string GetPivotKeyFromGridRow(DataGridViewRow row, List<PropertyReadPivotRow> pivotRows)
        {
            try
            {
                string name = row.Cells["col_Name"].Value as string ?? "";
                string qty = row.Cells["col_Qty"].Value as string ?? "";
                string filePath = row.Cells["col_FilePath"].Value as string ?? "";
                foreach (var p in pivotRows)
                {
                    string dn = GetDisplayDocumentName(p);
                    if (dn == name && p.Quantity.ToString() == qty && (p.FilePath ?? "") == filePath)
                        return p.TargetKey;
                }
            }
            catch { }
            return null;
        }

        private List<PropertyReadPivotRow> BuildPivotRows(List<PropertyReadRow> rows)
        {
            var pivotGroups = new Dictionary<string, PropertyReadPivotRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                string key = (row.FilePath ?? "") + "|" + (row.ConfigurationName ?? "") + "|" + (row.LocalComponentName ?? "");
                if (!pivotGroups.ContainsKey(key))
                    pivotGroups[key] = new PropertyReadPivotRow { TargetKey = key, TargetName = row.TargetName, LocalComponentName = row.LocalComponentName, FilePath = row.FilePath, ConfigurationName = row.ConfigurationName, Source = row.Source, Quantity = row.Quantity, Properties = new Dictionary<string, PropertyValuePair>(StringComparer.OrdinalIgnoreCase) };
                var pivot = pivotGroups[key];
                if (!pivot.Properties.ContainsKey(row.PropertyName))
                    pivot.Properties[row.PropertyName] = new PropertyValuePair { RawValue = row.RawValue, ResolvedValue = row.ResolvedValue };
                else { pivot.Properties[row.PropertyName].RawValue = row.RawValue; pivot.Properties[row.PropertyName].ResolvedValue = row.ResolvedValue; }
            }
            return pivotGroups.Values.ToList();
        }

        private List<string> BuildPropertyColumnNames(HashSet<string> allPropertyNames)
        {
            var intrinsic = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "文档类型", "装配体名称", "图纸名称" };
            var configPropNames = (_config.ReadPropertyNames ?? new List<string>()).Where(n => !intrinsic.Contains(n)).ToList();
            var ordered = new List<string>();
            if (_config.ReadShowAllProperties)
            {
                foreach (string name in configPropNames) if (allPropertyNames.Contains(name) && !ordered.Contains(name, StringComparer.OrdinalIgnoreCase)) ordered.Add(name);
                foreach (string name in allPropertyNames.Where(n => !ordered.Contains(n, StringComparer.OrdinalIgnoreCase) && !intrinsic.Contains(n)).OrderBy(n => n)) ordered.Add(name);
            }
            else { foreach (string name in configPropNames) if (!ordered.Contains(name, StringComparer.OrdinalIgnoreCase)) ordered.Add(name); }
            return ordered;
        }

        private string GetDisplayDocumentName(PropertyReadPivotRow p)
        {
            if (!string.IsNullOrEmpty(p.FilePath)) return Path.GetFileNameWithoutExtension(p.FilePath) ?? "";
            return CleanComponentDisplayName(p.LocalComponentName ?? p.TargetName ?? "");
        }

        private static string CleanComponentDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int idx = name.LastIndexOf('/');
            if (idx >= 0 && idx < name.Length - 1) return name.Substring(idx + 1);
            idx = name.LastIndexOf('\\');
            if (idx >= 0 && idx < name.Length - 1) return name.Substring(idx + 1);
            return name;
        }

        private string FormatFileSize(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return "";
            try { var fi = new FileInfo(filePath); if (!fi.Exists) return "不可用"; long bytes = fi.Length; if (bytes < 1024) return bytes + " B"; if (bytes < 1048576) return (bytes / 1024.0).ToString("F1") + " KB"; return (bytes / 1048576.0).ToString("F1") + " MB"; }
            catch { return "不可用"; }
        }

        private string GetPivotDisplayValue(PropertyReadPivotRow p, string colName, List<string> orderedPropNames, bool useRaw)
        {
            if (colName == "零部件名称") return GetDisplayDocumentName(p);
            if (colName == "数量") return p.Quantity.ToString();
            if (colName == "文档类型") return p.Source ?? "";
            if (colName == "文件路径") return p.FilePath ?? "";
            if (colName == "文件大小") return FormatFileSize(p.FilePath);
            PropertyValuePair pv; if (p.Properties.TryGetValue(colName, out pv)) return useRaw ? pv.RawValue : pv.ResolvedValue;
            return "";
        }

        private void RefreshPropertyGrid(DataGridView grid, List<PropertyReadPivotRow> pivotRows, List<string> orderedPropNames, bool useRaw, Dictionary<string, HashSet<string>> activeFilters)
        {
            grid.Rows.Clear();
            foreach (var p in pivotRows)
            {
                bool pass = true;
                foreach (var kv in activeFilters)
                {
                    string val = GetPivotDisplayValue(p, kv.Key, orderedPropNames, useRaw);
                    string checkVal = string.IsNullOrEmpty(val) ? "(空白)" : val;
                    if (!kv.Value.Contains(checkVal)) { pass = false; break; }
                }
                if (!pass) continue;
                var cells = new List<object>();
                cells.Add(GetDisplayDocumentName(p));
                cells.Add(p.Quantity.ToString());
                cells.Add(p.Source ?? "");
                foreach (string propName in orderedPropNames) { PropertyValuePair pv; cells.Add(p.Properties.TryGetValue(propName, out pv) ? (useRaw ? pv.RawValue : pv.ResolvedValue) : ""); }
                cells.Add(p.FilePath ?? "");
                cells.Add(FormatFileSize(p.FilePath));
                int rowIdx = grid.Rows.Add(cells.ToArray());
                if (grid.Columns.Contains("col_FilePath") && !string.IsNullOrEmpty(p.FilePath))
                    grid.Rows[rowIdx].Cells["col_FilePath"].ToolTipText = p.FilePath;
            }
        }

        private void AutoFitPropertyGridColumns(DataGridView grid, List<string> orderedPropNames)
        {
            int available = grid.DisplayRectangle.Width - (grid.RowHeadersVisible ? grid.RowHeadersWidth : 0) - 4;
            if (available < 400) return;
            var fixedWidths = new Dictionary<string, int> { { "col_Name", 200 }, { "col_Qty", 70 }, { "col_DocType", 90 }, { "col_FilePath", 350 }, { "col_FileSize", 90 } };
            int fixedTotal = 0;
            foreach (var kv in fixedWidths) { if (grid.Columns.Contains(kv.Key)) { grid.Columns[kv.Key].Width = kv.Value; fixedTotal += kv.Value; } }
            int propCount = orderedPropNames.Count; int propTotal = 0;
            foreach (string pn in orderedPropNames) { string cn = "prop_" + pn; if (grid.Columns.Contains(cn)) { grid.Columns[cn].Width = 150; propTotal += 150; } }
            int total = fixedTotal + propTotal;
            if (total < available && propCount > 0)
            {
                int extra = available - total; int perCol = extra / propCount;
                foreach (string pn in orderedPropNames) { string cn = "prop_" + pn; if (grid.Columns.Contains(cn)) grid.Columns[cn].Width += perCol; }
                int remainder = extra - perCol * propCount;
                if (grid.Columns.Contains("col_Name")) grid.Columns["col_Name"].Width += remainder / 2;
                if (grid.Columns.Contains("col_FilePath")) grid.Columns["col_FilePath"].Width += remainder - remainder / 2;
            }
        }

        // === Real assembly tree with hierarchy ===
        private void BuildAssemblyTree(TreeView tree, IModelDoc2 asmDoc, List<PropertyReadPivotRow> pivotRows, Dictionary<string, List<TreeNode>> pivotKeyToTreeNodes)
        {
            tree.Nodes.Clear(); tree.BeginUpdate();
            string rootName = Path.GetFileNameWithoutExtension(asmDoc.GetPathName()) ?? "装配体";
            var rootNode = new TreeNode(rootName);

            IAssemblyDoc asm = asmDoc as IAssemblyDoc;
            if (asm == null) { tree.EndUpdate(); return; }

            // Get top-level components
            object topObjs = asm.GetComponents(false);
            if (topObjs == null) { tree.EndUpdate(); return; }
            object[] allComps = topObjs as object[];
            if (allComps == null) { tree.EndUpdate(); return; }

            // Build parent-child map: parentPath -> children
            var childrenMap = new Dictionary<string, List<IComponent2>>(StringComparer.OrdinalIgnoreCase);
            var allCompList = new List<IComponent2>();
            foreach (object c in allComps)
            {
                IComponent2 comp = c as IComponent2;
                if (comp == null) continue;
                allCompList.Add(comp);
                // Get parent: if component has a parent component, it's nested
                IComponent2 parent = (IComponent2)comp.GetParent();
                string parentKey = parent != null ? GetCompFilePathKey(parent) : "__ROOT__";
                if (!childrenMap.ContainsKey(parentKey)) childrenMap[parentKey] = new List<IComponent2>();
                childrenMap[parentKey].Add(comp);
            }

            // Recursive build
            Action<TreeNode, string> buildChildren = null;
            buildChildren = (parentNode, parentKey) =>
            {
                List<IComponent2> children;
                if (!childrenMap.TryGetValue(parentKey, out children)) return;
                foreach (var comp in children)
                {
                    string compPath = GetCompFilePathKey(comp);
                    string compName = CleanComponentDisplayName(comp.Name2 ?? "");
                    if (string.IsNullOrEmpty(compName))
                    {
                        try { IModelDoc2 m = (IModelDoc2)comp.GetModelDoc2(); if (m != null) compName = Path.GetFileNameWithoutExtension(m.GetPathName()) ?? ""; } catch { }
                    }
                    string docType = "";
                    try { IModelDoc2 m = (IModelDoc2)comp.GetModelDoc2(); if (m != null) docType = GetDocTypeName(m.GetType()); } catch { }

                    // Find matching pivot row
                    string pivotKey = "";
                    try { IModelDoc2 m = (IModelDoc2)comp.GetModelDoc2(); if (m != null) { string fp = m.GetPathName(); string cfg = comp.ReferencedConfiguration ?? ""; pivotKey = fp + "|" + cfg + "|" + (comp.Name2 ?? ""); } } catch { }

                    string label = compName;
                    if (!string.IsNullOrEmpty(docType)) label += " [" + docType + "]";
                    var node = new TreeNode(label) { Tag = new AssemblyTreeNodeData { DisplayName = compName, PivotKey = pivotKey, Component = comp } };
                    parentNode.Nodes.Add(node);

                    if (!string.IsNullOrEmpty(pivotKey))
                    {
                        if (!pivotKeyToTreeNodes.ContainsKey(pivotKey)) pivotKeyToTreeNodes[pivotKey] = new List<TreeNode>();
                        pivotKeyToTreeNodes[pivotKey].Add(node);
                    }

                    // Recurse into children if sub-assembly
                    buildChildren(node, compPath);
                }
            };

            buildChildren(rootNode, "__ROOT__");
            tree.Nodes.Add(rootNode);
            rootNode.Expand();
            tree.EndUpdate();
        }

        private string GetCompFilePathKey(IComponent2 comp)
        {
            try { IModelDoc2 m = (IModelDoc2)comp.GetModelDoc2(); if (m != null) return m.GetPathName() ?? comp.Name2 ?? ""; } catch { }
            return comp.Name2 ?? "";
        }

        // Fallback flat tree
        private void BuildFlatAssemblyTree(TreeView tree, List<PropertyReadPivotRow> pivotRows, Dictionary<string, List<TreeNode>> pivotKeyToTreeNodes)
        {
            tree.Nodes.Clear(); tree.BeginUpdate();
            var rootNode = new TreeNode("装配体");
            foreach (var p in pivotRows)
            {
                string name = GetDisplayDocumentName(p);
                string label = p.Quantity > 1 ? string.Format("{0} (×{1})", name, p.Quantity) : name;
                if (!string.IsNullOrEmpty(p.Source)) label += " [" + p.Source + "]";
                var node = new TreeNode(label) { Tag = new AssemblyTreeNodeData { DisplayName = name, PivotKey = p.TargetKey } };
                rootNode.Nodes.Add(node);
                if (!pivotKeyToTreeNodes.ContainsKey(p.TargetKey)) pivotKeyToTreeNodes[p.TargetKey] = new List<TreeNode>();
                pivotKeyToTreeNodes[p.TargetKey].Add(node);
            }
            tree.Nodes.Add(rootNode); rootNode.Expand(); tree.EndUpdate();
        }

        #endregion

        #region Remote Execution

                private void ExecuteRemoteTask(string taskType, string displayName)
                {
            WriteTrace("ExecuteRemoteTask: " + taskType);

            ModelInfo model = CollectModelInfo();
            if (model == null)
            {
                SafeMessage("请先打开并保存一个 SolidWorks 文档后再执行" + displayName + "。", MessageBoxIcon.Warning);
                return;
            }

            // Build enhanced request body per remote contract
            var targets = ResolveTargets();
            int selectedCount = 0;
            var targetList = new List<Dictionary<string, object>>();
            string scopeMode = "active_document";

            if (targets != null && targets.Count > 0)
            {
                bool hasComponent = targets.Any(t => t.Component != null);
                if (hasComponent)
                {
                    scopeMode = "selected_components";
                    selectedCount = targets.Count(t => t.Component != null);
                    foreach (var t in targets.Where(t => t.Component != null))
                    {
                        targetList.Add(new Dictionary<string, object>
                        {
                            ["display_name"] = t.DisplayName,
                            ["filepath"] = t.FilePath,
                            ["doc_type"] = t.DocType
                        });
                    }
                }
            }

            var payload = new Dictionary<string, object>
            {
                ["task_type"] = taskType,
                ["execution_mode"] = "remote",
                ["engineer_id"] = _config.EngineerId,
                ["client"] = new Dictionary<string, object>
                {
                    ["name"] = "MechPilot",
                    ["version"] = "1.0.0",
                    ["machine"] = System.Environment.MachineName
                },
                ["active_document"] = new Dictionary<string, object>
                {
                    ["filename"] = Path.GetFileName(model.FilePath),
                    ["filepath"] = model.FilePath,
                    ["doc_type"] = model.DocType,
                    ["title"] = model.Title
                },
                ["target_scope"] = new Dictionary<string, object>
                {
                    ["mode"] = scopeMode,
                    ["selected_count"] = selectedCount,
                    ["targets"] = targetList
                },
                ["local_rules_version"] = _rules != null ? _rules.Version : "",
                ["priority"] = 3
            };

            string json = new JavaScriptSerializer().Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            _httpClient.Timeout = TimeSpan.FromSeconds(_config.RequestTimeoutSeconds);
            string endpoint = string.IsNullOrEmpty(_config.RemoteTaskEndpoint) ? "/api/v1/task" : _config.RemoteTaskEndpoint;
            string url = _config.ServerUrl.TrimEnd('/') + endpoint;
            WriteTrace("Submitting task to " + url + ": " + taskType);

            try
            {
                HttpResponseMessage response = _httpClient.PostAsync(url, content).GetAwaiter().GetResult();
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    _config.Log(displayName + " submitted: " + body);
                    SafeMessage(displayName + " 提交成功。\n\n" + Shorten(body, 700), MessageBoxIcon.Information);
                }
                else
                {
                    _config.Log(displayName + " failed: " + body);
                    SafeMessage(displayName + " 失败。\n\n服务器返回 " + (int)response.StatusCode + "：" + Shorten(body, 700), MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _config.Log(displayName + " connection error: " + ex.Message);
                SafeMessage(displayName + " 连接失败。\n\n" + ex.Message + "\n\n请检查 config.json 中的 server_url（" + _config.ServerUrl + "）", MessageBoxIcon.Error);
            }
        }

        private ModelInfo CollectModelInfo()
        {
            try
            {
                IModelDoc2 model = _swApp?.ActiveDoc as IModelDoc2;
                if (model == null)
                    return null;

                string path = model.GetPathName();
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                int docType = model.GetType();
                string typeName = GetDocTypeName(docType);

                return new ModelInfo
                {
                    FilePath = path,
                    Title = model.GetTitle(),
                    DocType = typeName
                };
            }
            catch (Exception ex)
            {
                WriteTrace("CollectModelInfo failed: " + ex);
                return null;
            }
        }

        #endregion

        #region Config

        private void OpenConfigFile()
        {
            string configPath = Path.Combine(GetAddinDirectory(), "config/config.json");
            try
            {
                if (!File.Exists(configPath))
                    AddinConfig.Defaults().Save(configPath);
                Process.Start(configPath);
            }
            catch
            {
                SafeMessage("Config file: " + configPath, MessageBoxIcon.Information);
            }
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(GetAddinDirectory(), "config/config.json");
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath, Encoding.UTF8);
                    _config = AddinConfig.FromJson(json);
                }
                else
                {
                    _config = AddinConfig.Defaults();
                    _config.Save(configPath);
                }
            }
            catch (Exception ex)
            {
                WriteTrace("LoadConfig failed, using defaults: " + ex);
                _config = AddinConfig.Defaults();
            }
        }

        private void LoadRules()
        {
            try
            {
                string rulesPath = Path.Combine(GetAddinDirectory(), _config.LocalRulesFile);
                if (!File.Exists(rulesPath) && !Path.IsPathRooted(_config.LocalRulesFile))
                    rulesPath = Path.Combine(GetAddinDirectory(), "config", _config.LocalRulesFile);
                _rules = RuleProvider.LoadLocalRules(rulesPath);
                WriteTrace("LoadRules: version=" + _rules.Version + " sets=" + _rules.PropertySets.Count);
            }
            catch (Exception ex)
            {
                WriteTrace("LoadRules failed, using demo defaults: " + ex);
                _rules = RuleProvider.CreateDemoRules();
            }
        }

        #endregion

        #region Utility

        internal static string GetAddinDirectory()
        {
            string location = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(location) ?? AppDomain.CurrentDomain.BaseDirectory;
        }

        internal static void WriteTrace(string message)
        {
            try
            {
                string logPath = Path.Combine(GetAddinDirectory(), "addin-load.log");
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(logPath, line + System.Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        private static string Shorten(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text ?? "";
            return text.Substring(0, maxLength) + "...";
        }

        private static void SafeMessage(string message, MessageBoxIcon icon)
        {
            try
            {
                MessageBox.Show(message, AddinTitle, MessageBoxButtons.OK, icon);
            }
            catch { }
        }

        #endregion

    #region Cockpit Context Builder (Agent L) — Enhanced

    // ═══════════════════════════════════════════════════════
    //  Stable Key Helpers
    // ═══════════════════════════════════════════════════════

    private static string BuildDocumentKey(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || filePath == "不可用")
            return null;
        try { return Path.GetFullPath(filePath).ToLowerInvariant(); }
        catch { return filePath.ToLowerInvariant(); }
    }

    private static string BuildInstanceKey(IComponent2 comp, string parentPath)
    {
        try
        {
            string compName = comp?.Name2 ?? "";
            return string.IsNullOrEmpty(parentPath) ? compName : (parentPath + "/" + compName);
        }
        catch { return ""; }
    }

    private static string BuildPivotKey(string filePath, string config, string compName)
    {
        return (filePath ?? "") + "|" + (config ?? "") + "|" + (compName ?? "");
    }

    // ═══════════════════════════════════════════════════════
    //  Safe Value Readers
    // ═══════════════════════════════════════════════════════

    private static string SafeGetFilePath(IModelDoc2 model)
    {
        try
        {
            if (model == null) return "不可用";
            string p = model.GetPathName();
            return string.IsNullOrWhiteSpace(p) ? "不可用" : p;
        }
        catch { return "不可用"; }
    }

    private static string SafeGetFileSize(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || filePath == "不可用")
            return "";
        try
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists) return "不可用";
            long bytes = fi.Length;
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024.0).ToString("F1") + " KB";
            return (bytes / 1048576.0).ToString("F1") + " MB";
        }
        catch { return "不可用"; }
    }

    private static string SafeGetLastModified(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || filePath == "不可用")
            return "";
        try
        {
            var fi = new FileInfo(filePath);
            return fi.Exists ? fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") : "";
        }
        catch { return ""; }
    }

    private static bool SafeIsSuppressed(IComponent2 comp)
    {
        try { return comp != null && comp.GetSuppression() == (int)swComponentSuppressionState_e.swComponentSuppressed; }
        catch { return false; }
    }

    private static bool SafeIsLightweight(IComponent2 comp)
    {
        try { return comp != null && comp.GetSuppression() == (int)swComponentSuppressionState_e.swComponentLightweight; }
        catch { return false; }
    }

    private static string SafeCompFileKey(IComponent2 comp)
    {
        try
        {
            IModelDoc2 m = (IModelDoc2)comp.GetModelDoc2();
            if (m != null)
            {
                string path = m.GetPathName();
                if (!string.IsNullOrEmpty(path)) return path;
            }
        }
        catch { }
        return comp?.Name2 ?? Guid.NewGuid().ToString();
    }

    // ═══════════════════════════════════════════════════════
    //  BuildCockpitContext — 主入口
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 构建完整 CockpitContext JSON 快照。由 CockpitForm 的 Func&lt;string&gt; 委托调用。
    /// </summary>
    private string BuildCockpitContext()
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<CockpitWarning>();

        try
        {
            IModelDoc2 activeDoc = _swApp?.ActiveDoc as IModelDoc2;

            var context = new CockpitContext
            {
                SchemaVersion = "mechpilot.cockpit.context.v1",
                TimestampUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Client = new CockpitClientInfo
                {
                    EngineerId = _config?.EngineerId ?? System.Environment.UserName,
                    SwVersion = _swApp?.RevisionNumber() ?? "unknown",
                    AddinVersion = "1.0.0",
                    ExecutionMode = _config?.ExecutionMode ?? "local",
                    MachineName = System.Environment.MachineName
                },
                Capabilities = new List<string> { "read", "write", "check", "review", "cockpit" }
            };

            // ── 无文档场景 ──
            if (activeDoc == null)
            {
                WriteTrace("BuildCockpitContext: no active document.");
                context.ActiveDocument = new CockpitDocumentInfo { Title = "(none)", DocType = "none" };
                context.Summary = new CockpitSummaryInfo();
                context.Warnings = warnings;
                sw.Stop();
                WriteTrace(string.Format("BuildCockpitContext: empty context in {0:F0} ms", sw.Elapsed.TotalMilliseconds));
                return SerializeCockpitContext(context);
            }

            // ── 活动文档信息 ──
            context.ActiveDocument = BuildActiveDocumentInfo(activeDoc, warnings);
            int docType = activeDoc.GetType();
            string docTypeName = GetDocTypeName(docType);
            bool isAssembly = (swDocumentTypes_e)docType == swDocumentTypes_e.swDocASSEMBLY;
            bool isDrawing = (swDocumentTypes_e)docType == swDocumentTypes_e.swDocDRAWING;

            // ── 选择集 ──
            context.Selection = BuildCockpitSelection(activeDoc, warnings);

            // ── 属性行采集 (复用现有 reader) ──
            var rows = new List<PropertyReadRow>();
            int totalCount = 0;
            int suppressedCount = 0;
            int lightweightCount = 0;
            int readFailedCount = 0;

            if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocPART)
            {
                totalCount = 1;
                string fp = activeDoc.GetPathName() ?? "";
                ReadModelProperties(activeDoc, activeDoc.GetTitle(), "", fp, "part", 1, rows);
            }
            else if (isDrawing)
            {
                try
                {
                    ReadDrawingProperties(activeDoc, rows, ref totalCount);
                }
                catch (Exception ex)
                {
                    warnings.Add(new CockpitWarning { Level = "error", Target = "drawing", Message = "工程图属性读取失败: " + ex.Message });
                }
            }
            else if (isAssembly)
            {
                try
                {
                    IAssemblyDoc asm = activeDoc as IAssemblyDoc;
                    if (asm != null)
                    {
                        object compObjs = asm.GetComponents(false);
                        if (compObjs != null)
                        {
                            object[] comps = compObjs as object[];
                            if (comps != null)
                            {
                                totalCount = comps.Length;
                                foreach (object c in comps)
                                {
                                    IComponent2 comp = c as IComponent2;
                                    if (comp == null) continue;
                                    if (SafeIsSuppressed(comp)) { suppressedCount++; continue; }
                                    if (SafeIsLightweight(comp)) { lightweightCount++; continue; }
                                }
                            }
                        }
                    }
                    ReadAssemblyAllComponents(activeDoc, rows, ref totalCount);
                }
                catch (Exception ex)
                {
                    warnings.Add(new CockpitWarning { Level = "error", Target = "assembly", Message = "装配体属性读取失败: " + ex.Message });
                }
            }

            // ── 属性表 ──
            context.PropertyTable = BuildCockpitPropertyTable(rows, context.ActiveDocument.Title, warnings);

            // ── 装配树 ──
            if (isAssembly)
                context.AssemblyTree = BuildCockpitAssemblyTree(activeDoc, rows, warnings);

            // ── 汇总统计 ──
            int uniqueDocs = context.PropertyTable?.Rows?.Count ?? 0;
            int partCountT = 0, subAsmCount = 0;
            if (context.PropertyTable?.Rows != null)
            {
                foreach (var r in context.PropertyTable.Rows)
                {
                    if (r.DocType == "part") partCountT++;
                    else if (r.DocType == "assembly") subAsmCount++;
                }
            }
            context.Summary = new CockpitSummaryInfo
            {
                TargetCount = totalCount,
                TotalComponents = totalCount,
                UniqueDocCount = uniqueDocs,
                PartCount = partCountT,
                SubAssemblyCount = subAsmCount,
                DrawingCount = isDrawing ? 1 : 0,
                SuppressedCount = suppressedCount,
                LightweightCount = lightweightCount,
                ReadFailedCount = readFailedCount,
                CustomPropertyColumnCount = context.PropertyTable?.DynamicColumns?.Count ?? 0
            };

            context.Warnings = warnings;

            sw.Stop();
            WriteTrace(string.Format(
                "BuildCockpitContext: type={0} title={1} rows={2} treeNodes={3} warnings={4} elapsed={5:F0}ms",
                docTypeName, context.ActiveDocument.Title, rows.Count,
                context.AssemblyTree?.Count ?? 0, warnings.Count, sw.Elapsed.TotalMilliseconds));

            return SerializeCockpitContext(context);
        }
        catch (Exception ex)
        {
            WriteTrace("BuildCockpitContext FATAL: " + ex.ToString());
            return SerializeCockpitContext(new CockpitContext
            {
                SchemaVersion = "mechpilot.cockpit.context.v1",
                TimestampUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Client = new CockpitClientInfo { EngineerId = "error", SwVersion = "unknown" },
                ActiveDocument = new CockpitDocumentInfo { Title = "(致命错误)", DocType = "error" },
                Warnings = new List<CockpitWarning>
                {
                    new CockpitWarning { Level = "fatal", Target = "context", Message = ex.Message }
                }
            });
        }
    }

    // ═══════════════════════════════════════════════════════
    //  BuildActiveDocumentInfo
    // ═══════════════════════════════════════════════════════

    private CockpitDocumentInfo BuildActiveDocumentInfo(IModelDoc2 model, List<CockpitWarning> warnings)
    {
        var info = new CockpitDocumentInfo();
        try
        {
            string path = model.GetPathName();
            int docType = model.GetType();

            info.Title = model.GetTitle() ?? "";
            info.FilePath = string.IsNullOrWhiteSpace(path) ? "" : path;
            info.DocType = GetDocTypeName(docType);
            info.IsSaved = !string.IsNullOrWhiteSpace(path);
            info.IsModified = model.GetSaveFlag() == true;
            info.FileSize = SafeGetFileSize(path);
            info.LastModified = SafeGetLastModified(path);

            // Configuration name
            try
            {
                IConfigurationManager cfgMgr = model.ConfigurationManager;
                if (cfgMgr != null)
                {
                    IConfiguration activeCfg = cfgMgr.ActiveConfiguration;
                    if (activeCfg != null)
                        info.ConfigurationName = activeCfg.Name ?? "(默认)";
                }
            }
            catch { info.ConfigurationName = "(默认)"; }
            if (string.IsNullOrEmpty(info.ConfigurationName))
                info.ConfigurationName = "(默认)";

            // Custom property count
            try
            {
                CustomPropertyManager mgr = model.Extension.get_CustomPropertyManager("");
                if (mgr != null)
                {
                    object names = mgr.GetNames();
                    if (names != null) info.CustomPropertyCount = ((string[])names).Length;
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            warnings.Add(new CockpitWarning { Level = "error", Target = "active_document", Message = "文档信息采集失败: " + ex.Message });
            info.Title = "(error)";
        }
        return info;
    }

    // ═══════════════════════════════════════════════════════
    //  BuildCockpitSelection
    // ═══════════════════════════════════════════════════════

    private List<CockpitSelectionInfo> BuildCockpitSelection(IModelDoc2 activeDoc, List<CockpitWarning> warnings)
    {
        var list = new List<CockpitSelectionInfo>();
        try
        {
            ISelectionMgr selMgr = (ISelectionMgr)activeDoc.SelectionManager;
            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    object selObj = selMgr.GetSelectedObject6(i, -1);
                    IComponent2 comp = selObj as IComponent2;
                    if (comp == null)
                    {
                        // Non-component selection (feature, face, etc.)
                        list.Add(new CockpitSelectionInfo
                        {
                            Index = i,
                            DisplayName = selObj != null ? selObj.ToString() : "(unknown)",
                            DocType = "unknown"
                        });
                        continue;
                    }

                    IModelDoc2 compModel = null;
                    try { compModel = (IModelDoc2)comp.GetModelDoc2(); } catch { }

                    string compPath = SafeGetFilePath(compModel);
                    string compName = comp.Name2 ?? "";
                    string displayName = CleanComponentDisplayName(compName);
                    string docType = compModel != null ? GetDocTypeName(compModel.GetType()) : "";
                    string cfg = "";
                    try { cfg = comp.ReferencedConfiguration ?? ""; } catch { }
                    if (string.IsNullOrEmpty(cfg)) cfg = "(默认)";

                    bool isSuppressed = SafeIsSuppressed(comp);
                    bool isLightweight = SafeIsLightweight(comp);

                    list.Add(new CockpitSelectionInfo
                    {
                        Index = i,
                        DisplayName = displayName,
                        ComponentName = compName,
                        ComponentPath = compPath,
                        DocType = docType,
                        Configuration = cfg,
                        IsSuppressed = isSuppressed,
                        IsLightweight = isLightweight,
                        PivotKey = BuildPivotKey(compPath, cfg, compName)
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add(new CockpitWarning { Level = "warning", Target = "selection[" + i + "]", Message = "读取失败: " + ex.Message });
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add(new CockpitWarning { Level = "error", Target = "selection", Message = "选择集采集失败: " + ex.Message });
        }
        return list;
    }

    // ═══════════════════════════════════════════════════════
    //  BuildCockpitAssemblyTree — 真实层级装配树
    // ═══════════════════════════════════════════════════════

    private List<CockpitTreeNode> BuildCockpitAssemblyTree(IModelDoc2 asmDoc, List<PropertyReadRow> propertyRows, List<CockpitWarning> warnings)
    {
        var swTree = Stopwatch.StartNew();
        var result = new List<CockpitTreeNode>();
        int nodeCounter = 0;

        try
        {
            IAssemblyDoc asm = asmDoc as IAssemblyDoc;
            if (asm == null)
            {
                warnings.Add(new CockpitWarning { Level = "warning", Target = "assembly_tree", Message = "无法获取 IAssemblyDoc 接口" });
                return result;
            }

            // Build quantity lookup from property rows
            var pivotQty = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in propertyRows)
            {
                string key = BuildPivotKey(row.FilePath, row.ConfigurationName, row.LocalComponentName);
                pivotQty[key] = row.Quantity;
            }

            object compObjs = asm.GetComponents(false);
            if (compObjs == null) return result;
            object[] allComps = compObjs as object[];
            if (allComps == null) return result;

            // parentKey → children list
            var childrenMap = new Dictionary<string, List<IComponent2>>(StringComparer.OrdinalIgnoreCase);
            int skippedSuppressed = 0;
            int skippedLightweight = 0;

            foreach (object c in allComps)
            {
                IComponent2 comp = c as IComponent2;
                if (comp == null) continue;

                if (SafeIsSuppressed(comp)) { skippedSuppressed++; continue; }
                if (SafeIsLightweight(comp)) { skippedLightweight++; continue; }

                IComponent2 parent = null;
                try { parent = (IComponent2)comp.GetParent(); } catch { }
                string parentKey = parent != null ? SafeCompFileKey(parent) : "__ROOT__";
                if (!childrenMap.ContainsKey(parentKey))
                    childrenMap[parentKey] = new List<IComponent2>();
                childrenMap[parentKey].Add(comp);
            }

            if (skippedSuppressed > 0)
                warnings.Add(new CockpitWarning { Level = "info", Target = "assembly_tree", Message = string.Format("跳过 {0} 个被抑制的组件", skippedSuppressed) });
            if (skippedLightweight > 0)
                warnings.Add(new CockpitWarning { Level = "info", Target = "assembly_tree", Message = string.Format("跳过 {0} 个轻化的组件", skippedLightweight) });

            // Recursive node builder
            Func<IComponent2, string, int, string, CockpitTreeNode> buildNode = null;
            buildNode = (comp, parentId, depth, parentPath) =>
            {
                nodeCounter++;
                string nodeId = "node-" + nodeCounter;

                // Safe reads
                string compName = "";
                try { compName = comp.Name2 ?? ""; } catch { }
                string displayName = CleanComponentDisplayName(compName);

                string compPath = "不可用";
                string docType = "";
                bool isAssemblyComp = false;
                try
                {
                    IModelDoc2 cm = (IModelDoc2)comp.GetModelDoc2();
                    if (cm != null)
                    {
                        string p = cm.GetPathName();
                        if (!string.IsNullOrEmpty(p)) compPath = p;
                        int dt = cm.GetType();
                        docType = GetDocTypeName(dt);
                        isAssemblyComp = (swDocumentTypes_e)dt == swDocumentTypes_e.swDocASSEMBLY;
                    }
                }
                catch { }

                string config = "(默认)";
                try { string c = comp.ReferencedConfiguration; if (!string.IsNullOrEmpty(c)) config = c; } catch { }

                string documentName = (compPath != "不可用") ? Path.GetFileNameWithoutExtension(compPath) : displayName;
                string instancePath = parentPath + "/" + displayName;

                string pivotKey = BuildPivotKey(compPath, config, compName);
                int quantity = 1;
                if (pivotQty.ContainsKey(pivotKey)) quantity = pivotQty[pivotKey];

                bool isPart = docType == "part";
                bool isSuppressed = SafeIsSuppressed(comp);
                bool isLightweight = SafeIsLightweight(comp);

                var node = new CockpitTreeNode
                {
                    NodeId = nodeId,
                    ParentId = parentId,
                    DisplayName = displayName,
                    Name = displayName,
                    ComponentName = compName,
                    DocumentName = documentName,
                    InstancePath = instancePath,
                    DocType = docType,
                    FilePath = compPath,
                    Configuration = config,
                    FileSize = SafeGetFileSize(compPath),
                    Quantity = quantity,
                    Depth = depth,
                    PivotKey = pivotKey,
                    IsExpanded = depth < 2,
                    IsAssembly = isAssemblyComp,
                    IsPart = isPart,
                    IsSuppressed = isSuppressed,
                    IsLightweight = isLightweight,
                    Children = new List<CockpitTreeNode>()
                };

                // Recurse into children
                string myKey = SafeCompFileKey(comp);
                List<IComponent2> children;
                if (childrenMap.TryGetValue(myKey, out children))
                {
                    foreach (var child in children)
                        node.Children.Add(buildNode(child, nodeId, depth + 1, instancePath));
                }
                node.ChildrenCount = node.Children.Count;

                return node;
            };

            // Build root-level nodes
            List<IComponent2> rootChildren;
            if (childrenMap.TryGetValue("__ROOT__", out rootChildren))
            {
                string rootDocName = Path.GetFileNameWithoutExtension(asmDoc.GetPathName() ?? "装配体");
                foreach (var child in rootChildren)
                    result.Add(buildNode(child, "root", 1, rootDocName));
            }

            swTree.Stop();
            WriteTrace(string.Format("BuildCockpitAssemblyTree: {0} nodes depth≤{1} in {2:F0} ms",
                nodeCounter, result.MaxOrDefault(n => n.Depth, 0), swTree.Elapsed.TotalMilliseconds));
        }
        catch (Exception ex)
        {
            warnings.Add(new CockpitWarning { Level = "error", Target = "assembly_tree", Message = "装配树构建失败: " + ex.Message });
            WriteTrace("BuildCockpitAssemblyTree failed: " + ex.ToString());
        }

        return result;
    }

    /// <summary>
    /// 构建属性表。支持 config.json 中的 read_property_names 过滤。
    /// </summary>
    private CockpitPropertyTable BuildCockpitPropertyTable(List<PropertyReadRow> rows, string targetLabel, List<CockpitWarning> warnings)
    {
        try
        {
            // Determine which property names to include (config-driven)
            var configuredProps = _config?.ReadPropertyNames ?? new List<string>();
            bool showAll = _config?.ReadShowAllProperties ?? false;
            var intrinsicSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "文档类型", "装配体名称", "图纸名称" };

            var pivotMap = new Dictionary<string, CockpitPropertyRow>(StringComparer.OrdinalIgnoreCase);
            int totalInstances = 0;

            foreach (var row in rows)
            {
                string key = BuildPivotKey(row.FilePath, row.ConfigurationName, row.LocalComponentName);
                CockpitPropertyRow crow;
                if (!pivotMap.TryGetValue(key, out crow))
                {
                    string docKey = BuildDocumentKey(row.FilePath);
                    string displayName = CleanComponentDisplayName(row.LocalComponentName ?? row.TargetName ?? "");
                    crow = new CockpitPropertyRow
                    {
                        PivotKey = key,
                        DocumentKey = docKey,
                        InstanceKey = key,
                        RowKey = key,
                        DisplayName = displayName,
                        FilePath = row.FilePath ?? "",
                        Configuration = string.IsNullOrEmpty(row.ConfigurationName) ? "(默认)" : row.ConfigurationName,
                        DocType = row.Source ?? "",
                        Quantity = row.Quantity,
                        FileSize = SafeGetFileSize(row.FilePath)
                    };
                    pivotMap[key] = crow;
                }
                totalInstances += row.Quantity;

                // Property filtering
                string propName = row.PropertyName ?? "";
                bool include = showAll;
                if (!include && configuredProps.Count > 0)
                {
                    // configuredProps first, then fallback to intrinsic
                    include = configuredProps.Contains(propName, StringComparer.OrdinalIgnoreCase) || intrinsicSet.Contains(propName);
                }
                if (!include) include = true; // showAll by default when configuredProps is empty

                if (include && !crow.Properties.ContainsKey(propName))
                {
                    crow.Properties[propName] = new CockpitPropertyValue
                    {
                        RawValue = row.RawValue ?? "",
                        ResolvedValue = row.ResolvedValue ?? "",
                        Type = 30  // swCustomInfoText
                    };
                }
            }

            var intrinsicCols = new List<string> { "display_name", "doc_type", "quantity", "file_path", "configuration", "file_size", "document_key", "row_key" };
            var dynamicCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in pivotMap)
                foreach (var pk in kv.Value.Properties.Keys)
                    dynamicCols.Add(pk);

            return new CockpitPropertyTable
            {
                TargetLabel = targetLabel ?? "",
                TotalRows = pivotMap.Count,
                TotalInstances = totalInstances,
                IntrinsicColumns = intrinsicCols,
                DynamicColumns = dynamicCols.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList(),
                Rows = pivotMap.Values.ToList()
            };
        }
        catch (Exception ex)
        {
            warnings.Add(new CockpitWarning { Level = "error", Target = "property_table", Message = "属性表构建失败: " + ex.Message });
            WriteTrace("BuildCockpitPropertyTable failed: " + ex.ToString());
            return new CockpitPropertyTable { TargetLabel = "(error)" };
        }
    }

    /// <summary>
    /// JSON 序列化。MaxJsonLength 设为 int.MaxValue 以支持大装配体。
    /// </summary>
    private static string SerializeCockpitContext(CockpitContext context)
    {
        try
        {
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            return serializer.Serialize(context);
        }
        catch (Exception ex)
        {
            return "{\"error\":\"" + ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
        }
    }

    #endregion



    
    }

    #region CockpitForm — WebView2 Agent驾驶舱窗口

    /// <summary>
    /// Agent驾驶舱窗口 — WebView2 宿主，加载 cockpit HTML 并与 C# 双向通信。
    /// WebView2 Runtime 缺失时弹中文提示，不导致插件加载失败。
    /// </summary>
    public class CockpitForm : Form
    {
        private readonly AddinConfig _config;
        private readonly Func<string> _buildContext;
        private readonly Func<string, string> _handleCommand;
        private WebView2 _webView;
        private Rectangle _normalBounds = Rectangle.Empty;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public CockpitForm(AddinConfig config, Func<string> buildContext, Func<string, string> handleCommand)
        {
            _config = config;
            _buildContext = buildContext;
            _handleCommand = handleCommand;

            Text = "MechPilot Agent驾驶舱";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 800);
            MinimumSize = new Size(800, 500);
            Padding = new Padding(6);
            BackColor = Color.FromArgb(217, 221, 228);
            _normalBounds = Bounds;
            WindowState = FormWindowState.Maximized;

            // === WebView2 (fills remaining space) ===
            try
            {
                _webView = new WebView2
                {
                    Dock = DockStyle.Fill,
                    CreationProperties = new CoreWebView2CreationProperties
                    {
                        UserDataFolder = Path.Combine(
                            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                            "cockpit-cache")
                    }
                };

                Controls.Add(_webView);

                _webView.CoreWebView2InitializationCompleted += OnWebView2Initialized;
                _ = InitializeWebView2Async();
            }
            catch (Exception ex)
            {
                SwAgentAddin.WriteTrace("CockpitForm constructor: WebView2 control creation failed: " + ex);
                throw;
            }
        }

        // Let Windows treat the HTML top bar like a native title bar.
        private const int WM_NCHITTEST = 0x84;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int DragTitleBarHeight = 44;
        private const int WindowControlsWidth = 144;
        private const int ResizeGrip = 8;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST)
            {
                var pos = PointToClient(Cursor.Position);
                if (WindowState != FormWindowState.Maximized)
                {
                    bool left = pos.X >= 0 && pos.X < ResizeGrip;
                    bool right = pos.X <= Width && pos.X > Width - ResizeGrip;
                    bool top = pos.Y >= 0 && pos.Y < ResizeGrip;
                    bool bottom = pos.Y <= Height && pos.Y > Height - ResizeGrip;

                    if (left && top) { m.Result = (IntPtr)HTTOPLEFT; return; }
                    if (right && top) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                    if (left && bottom) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                    if (right && bottom) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                    if (left) { m.Result = (IntPtr)HTLEFT; return; }
                    if (right) { m.Result = (IntPtr)HTRIGHT; return; }
                    if (top) { m.Result = (IntPtr)HTTOP; return; }
                    if (bottom) { m.Result = (IntPtr)HTBOTTOM; return; }
                }

                if (pos.Y >= 0 &&
                    pos.Y < DragTitleBarHeight &&
                    pos.X >= 0 &&
                    pos.X < Width - WindowControlsWidth &&
                    m.Result == (IntPtr)HTCLIENT)
                {
                    m.Result = (IntPtr)HTCAPTION;
                }
            }
        }

        private async System.Threading.Tasks.Task InitializeWebView2Async()
        {
            try
            {
                await _webView.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                SwAgentAddin.WriteTrace("CockpitForm: EnsureCoreWebView2Async failed: " + ex);
                // Will be caught by SwCmd_Cockpit
                throw;
            }
        }

        private void OnWebView2Initialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                SwAgentAddin.WriteTrace("CockpitForm: WebView2 init failed: " + e.InitializationException);
                MessageBox.Show(
                    "WebView2 初始化失败：\n" + e.InitializationException?.Message,
                    "MechPilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // JS → C# bridge
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // Navigate to cockpit URL
            string url = GetCockpitUrl();
            SwAgentAddin.WriteTrace("CockpitForm: navigating to " + url);
            _webView.CoreWebView2.Navigate(url);

            // Wait for page load, then inject context
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        }

        private void OnNavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) return;

            try
            {
                // C# → JS: inject initial context
                string contextJson = _buildContext?.Invoke() ?? "{}";
                string script = "if (window.MechPilot && window.MechPilot.receiveContext) { window.MechPilot.receiveContext(" + contextJson + "); }";
                _webView.CoreWebView2.ExecuteScriptAsync(script).ContinueWith(task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                        SwAgentAddin.WriteTrace("CockpitForm: context injection script failed: " + task.Exception.GetBaseException().Message);
                });
                SwAgentAddin.WriteTrace("CockpitForm: context injected (" + contextJson.Length + " chars)");
            }
            catch (Exception ex)
            {
                SwAgentAddin.WriteTrace("CockpitForm: context injection failed: " + ex.Message);
            }
        }

        private void OnWebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(message)) return;

                SwAgentAddin.WriteTrace("CockpitForm: received JS message (" + message.Length + " chars)");

                // Handle window commands locally (form-level actions)
                if (message.Contains("window_close"))
                {
                    Close(); return;
                }
                if (message.Contains("window_minimize"))
                {
                    WindowState = FormWindowState.Minimized; return;
                }
                if (message.Contains("window_drag"))
                {
                    BeginNativeDrag(); return;
                }
                if (message.Contains("window_maximize"))
                {
                    ToggleMaximizeRestore();
                    return;
                }

                // Dispatch other commands to handler
                string result = _handleCommand?.Invoke(message);
                if (!string.IsNullOrEmpty(result))
                {
                    // Send result back to JS
                    string escaped = EscapeForJsInjection(result);
                    string script = "if (window.MechPilot && window.MechPilot.receiveResult) { window.MechPilot.receiveResult('" + escaped + "'); }";
                    _webView.CoreWebView2.ExecuteScriptAsync(script);
                }
            }
            catch (Exception ex)
            {
                SwAgentAddin.WriteTrace("CockpitForm: WebMessageReceived error: " + ex.Message);
            }
        }

        private void BeginNativeDrag()
        {
            if (WindowState == FormWindowState.Normal)
                _normalBounds = Bounds;

            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        private void ToggleMaximizeRestore()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                if (_normalBounds.Width < MinimumSize.Width || _normalBounds.Height < MinimumSize.Height)
                {
                    Rectangle area = Screen.FromControl(this).WorkingArea;
                    Size size = new Size(Math.Min(1280, area.Width - 80), Math.Min(800, area.Height - 80));
                    _normalBounds = new Rectangle(
                        area.Left + (area.Width - size.Width) / 2,
                        area.Top + (area.Height - size.Height) / 2,
                        size.Width,
                        size.Height);
                }
                Bounds = _normalBounds;
                return;
            }

            _normalBounds = Bounds;
            WindowState = FormWindowState.Maximized;
        }

        /// <summary>
        /// 将字符串转义为安全的 JS 字符串注入值（单引号包裹）
        /// </summary>
        private static string EscapeForJsInjection(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new System.Text.StringBuilder(input.Length + 64);
            foreach (char c in input)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '\'') sb.Append("\\'");
                else if (c == '\n') sb.Append("\\n");
                else if (c == (char)13) sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private string GetCockpitUrl()
        {
            if (string.Equals(_config?.CockpitUrlMode, "dev", StringComparison.OrdinalIgnoreCase))
            {
                return _config.CockpitDevUrl ?? "http://127.0.0.1:5173";
            }

            // Local mode: load HTML from addin directory
            string entry = _config?.CockpitEntry ?? "frontend/property-workbench/index.html";
            string addinDir = SwAgentAddin.GetAddinDirectory();
            string fullPath = Path.Combine(addinDir, entry);

            if (File.Exists(fullPath))
                return "file:///" + fullPath.Replace("\\", "/");

            // Fallback: try property-workbench subfolder
            string altPath = Path.Combine(addinDir, "frontend/property-workbench", "index.html");
            if (File.Exists(altPath))
                return "file:///" + altPath.Replace("\\", "/");

            SwAgentAddin.WriteTrace("CockpitForm: HTML not found at " + fullPath + " or " + altPath);
            return "about:blank";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (_webView?.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                }
                _webView?.Dispose();
            }
            catch { }
            base.OnFormClosing(e);
        }
    }

    #endregion

    #region Data Classes

    public class ModelInfo
    {
        public string FilePath { get; set; }
        public string Title { get; set; }
        public string DocType { get; set; }
    }

    public class ResolvedTarget
    {
        public string DisplayName { get; set; }
        public string FilePath { get; set; }
        public string DocType { get; set; }
        public IModelDoc2 Model { get; set; }
        public IComponent2 Component { get; set; }
    }

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

    public class PropertyValuePair
    {
        public string RawValue { get; set; }
        public string ResolvedValue { get; set; }
    }

    public class PropertyReadPivotRow
    {
        public string TargetKey { get; set; }
        public string TargetName { get; set; }
        public string LocalComponentName { get; set; }
        public string FilePath { get; set; }
        public string ConfigurationName { get; set; }
        public string Source { get; set; }
        public int Quantity { get; set; }
        public Dictionary<string, PropertyValuePair> Properties { get; set; }
            = new Dictionary<string, PropertyValuePair>(StringComparer.OrdinalIgnoreCase);
    }

    public class AssemblyTreeNodeData
    {
        public string DisplayName { get; set; }
        public string PivotKey { get; set; }
        public IComponent2 Component { get; set; }
    }




    public class AddinConfig
    {
        // Legacy fields
        public string ServerUrl { get; set; } = "http://127.0.0.1:8080";
        public string EngineerId { get; set; } = System.Environment.UserName;
        public int PollIntervalSeconds { get; set; } = 3;
        public int RequestTimeoutSeconds { get; set; } = 120;
        public bool EnableFeishuNotify { get; set; } = false;
        public int LogLevel { get; set; } = 2;
        public bool AutoShowTaskPane { get; set; } = true;

        // Mode and behavior (Agent B)
        public string ExecutionMode { get; set; } = "local";
        public bool ConfirmBeforeWrite { get; set; } = true;
        public string MultiSelectBehavior { get; set; } = "show_form";

        // Local rules (Agent B)
        public string LocalRulesFile { get; set; } = "rules.local.json";

        // Remote endpoints (Agent B)
        public string RemoteTaskEndpoint { get; set; } = "/api/v1/task";
        public string RemoteStatusEndpointTemplate { get; set; } = "/api/v1/task/{task_id}";
        public string RemoteResultEndpointTemplate { get; set; } = "/api/v1/task/{task_id}/result";

        // Read properties display (Agent F)
        public List<string> ReadPropertyNames { get; set; } = new List<string>
        {
            "物料名称", "图号", "材料", "重量", "表面处理", "处理状态", "处理人", "处理日期",
            "装配体名称", "图纸名称", "文档类型"
        };
        public bool ReadShowAllProperties { get; set; } = false;
        public bool ReadShowRawValueDefault { get; set; } = false;
        public int ReadFormWidth { get; set; } = 1800;
        public int ReadFormHeight { get; set; } = 1000;

        // Assembly tree and filters (Agent G)
        public bool ReadShowAssemblyTreeDefault { get; set; } = true;
        public bool ReadEnableColumnFilters { get; set; } = true;
        public bool ReadAutoFitColumns { get; set; } = true;

        // Cockpit (Agent I)
        public bool CockpitEnabled { get; set; } = true;
        public string CockpitUrlMode { get; set; } = "local";
        public string CockpitEntry { get; set; } = "frontend/property-workbench/index.html";
        public string CockpitDevUrl { get; set; } = "http://127.0.0.1:5173";
        public bool CockpitPreferWebview2 { get; set; } = true;
        public bool CockpitFallbackToWinforms { get; set; } = true;
        public string CockpitSchemaVersion { get; set; } = "mechpilot.cockpit.context.v1";

        public static AddinConfig Defaults() => new AddinConfig();

        public static AddinConfig FromJson(string json)
        {
            var serializer = new JavaScriptSerializer();
            var dict = serializer.Deserialize<Dictionary<string, object>>(json);
            var config = new AddinConfig();
            if (dict == null) return config;

            // Legacy fields
            if (dict.ContainsKey("server_url"))
                config.ServerUrl = Convert.ToString(dict["server_url"]);
            if (dict.ContainsKey("engineer_id"))
                config.EngineerId = Convert.ToString(dict["engineer_id"]);
            if (dict.ContainsKey("poll_interval_seconds"))
                config.PollIntervalSeconds = Convert.ToInt32(dict["poll_interval_seconds"]);
            if (dict.ContainsKey("request_timeout_seconds"))
                config.RequestTimeoutSeconds = Convert.ToInt32(dict["request_timeout_seconds"]);
            if (dict.ContainsKey("enable_feishu_notify"))
                config.EnableFeishuNotify = Convert.ToBoolean(dict["enable_feishu_notify"]);
            if (dict.ContainsKey("log_level"))
                config.LogLevel = Convert.ToInt32(dict["log_level"]);
            if (dict.ContainsKey("auto_show_taskpane"))
                config.AutoShowTaskPane = Convert.ToBoolean(dict["auto_show_taskpane"]);

            // New fields — with safe defaults for backward compat
            if (dict.ContainsKey("execution_mode"))
            {
                string mode = Convert.ToString(dict["execution_mode"]);
                if (string.Equals(mode, "local", StringComparison.OrdinalIgnoreCase))
                    config.ExecutionMode = "local";
                else if (string.Equals(mode, "remote", StringComparison.OrdinalIgnoreCase))
                    config.ExecutionMode = "remote";
                // else keep default "local"
            }
            if (dict.ContainsKey("confirm_before_write"))
                config.ConfirmBeforeWrite = Convert.ToBoolean(dict["confirm_before_write"]);
            if (dict.ContainsKey("multi_select_behavior"))
                config.MultiSelectBehavior = Convert.ToString(dict["multi_select_behavior"]);
            if (dict.ContainsKey("local_rules_file"))
                config.LocalRulesFile = Convert.ToString(dict["local_rules_file"]);
            if (dict.ContainsKey("remote_task_endpoint"))
                config.RemoteTaskEndpoint = Convert.ToString(dict["remote_task_endpoint"]);
            if (dict.ContainsKey("remote_status_endpoint_template"))
                config.RemoteStatusEndpointTemplate = Convert.ToString(dict["remote_status_endpoint_template"]);
            if (dict.ContainsKey("remote_result_endpoint_template"))
                config.RemoteResultEndpointTemplate = Convert.ToString(dict["remote_result_endpoint_template"]);

            // Read properties display
            if (dict.ContainsKey("read_property_names"))
            {
                var arr = dict["read_property_names"] as System.Collections.ArrayList;
                if (arr != null)
                {
                    config.ReadPropertyNames = new List<string>();
                    foreach (var item in arr)
                        config.ReadPropertyNames.Add(Convert.ToString(item));
                }
            }
            if (dict.ContainsKey("read_show_all_properties"))
                config.ReadShowAllProperties = Convert.ToBoolean(dict["read_show_all_properties"]);
            if (dict.ContainsKey("read_show_raw_value_default"))
                config.ReadShowRawValueDefault = Convert.ToBoolean(dict["read_show_raw_value_default"]);
            if (dict.ContainsKey("read_form_width"))
                config.ReadFormWidth = Convert.ToInt32(dict["read_form_width"]);
            if (dict.ContainsKey("read_form_height"))
                config.ReadFormHeight = Convert.ToInt32(dict["read_form_height"]);

            if (dict.ContainsKey("read_show_assembly_tree_default"))
                config.ReadShowAssemblyTreeDefault = Convert.ToBoolean(dict["read_show_assembly_tree_default"]);
            if (dict.ContainsKey("read_enable_column_filters"))
                config.ReadEnableColumnFilters = Convert.ToBoolean(dict["read_enable_column_filters"]);
            if (dict.ContainsKey("read_auto_fit_columns"))
                config.ReadAutoFitColumns = Convert.ToBoolean(dict["read_auto_fit_columns"]);

            // Cockpit fields — safe defaults for backward compat
            if (dict.ContainsKey("cockpit_enabled"))
                config.CockpitEnabled = Convert.ToBoolean(dict["cockpit_enabled"]);
            if (dict.ContainsKey("cockpit_url_mode"))
                config.CockpitUrlMode = Convert.ToString(dict["cockpit_url_mode"]);
            if (dict.ContainsKey("cockpit_entry"))
                config.CockpitEntry = Convert.ToString(dict["cockpit_entry"]);
            if (dict.ContainsKey("cockpit_dev_url"))
                config.CockpitDevUrl = Convert.ToString(dict["cockpit_dev_url"]);
            if (dict.ContainsKey("cockpit_prefer_webview2"))
                config.CockpitPreferWebview2 = Convert.ToBoolean(dict["cockpit_prefer_webview2"]);
            if (dict.ContainsKey("cockpit_fallback_to_winforms"))
                config.CockpitFallbackToWinforms = Convert.ToBoolean(dict["cockpit_fallback_to_winforms"]);
            if (dict.ContainsKey("cockpit_schema_version"))
                config.CockpitSchemaVersion = Convert.ToString(dict["cockpit_schema_version"]);

            return config;
        }

        public void Save(string path)
        {
            var dict = new Dictionary<string, object>
            {
                ["execution_mode"] = ExecutionMode,
                ["server_url"] = ServerUrl,
                ["engineer_id"] = EngineerId,
                ["poll_interval_seconds"] = PollIntervalSeconds,
                ["request_timeout_seconds"] = RequestTimeoutSeconds,
                ["enable_feishu_notify"] = EnableFeishuNotify,
                ["log_level"] = LogLevel,
                ["auto_show_taskpane"] = AutoShowTaskPane,
                ["confirm_before_write"] = ConfirmBeforeWrite,
                ["multi_select_behavior"] = MultiSelectBehavior,
                ["local_rules_file"] = LocalRulesFile,
                ["remote_task_endpoint"] = RemoteTaskEndpoint,
                ["remote_status_endpoint_template"] = RemoteStatusEndpointTemplate,
                ["remote_result_endpoint_template"] = RemoteResultEndpointTemplate
            };
            dict["read_property_names"] = ReadPropertyNames;
            dict["read_show_all_properties"] = ReadShowAllProperties;
            dict["read_show_raw_value_default"] = ReadShowRawValueDefault;
            dict["read_form_width"] = ReadFormWidth;

            dict["read_show_assembly_tree_default"] = ReadShowAssemblyTreeDefault;
            dict["read_enable_column_filters"] = ReadEnableColumnFilters;
            dict["read_auto_fit_columns"] = ReadAutoFitColumns;
            dict["read_form_height"] = ReadFormHeight;

            // Cockpit
            dict["cockpit_enabled"] = CockpitEnabled;
            dict["cockpit_url_mode"] = CockpitUrlMode;
            dict["cockpit_entry"] = CockpitEntry;
            dict["cockpit_dev_url"] = CockpitDevUrl;
            dict["cockpit_prefer_webview2"] = CockpitPreferWebview2;
            dict["cockpit_fallback_to_winforms"] = CockpitFallbackToWinforms;
            dict["cockpit_schema_version"] = CockpitSchemaVersion;

            string json = new JavaScriptSerializer().Serialize(dict);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public void Log(string message)
        {
            if (LogLevel < 1) return;
            try
            {
                string logDir = Path.Combine(SwAgentAddin.GetAddinDirectory(), "logs");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, $"addin-{DateTime.Now:yyyyMMdd}.log");
                string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
                File.AppendAllText(logPath, line + System.Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    internal static class EnumerableExtensions
    {
        public static int MaxOrDefault<T>(this IEnumerable<T> source, Func<T, int> selector, int defaultValue)
        {
            if (source == null || !source.Any()) return defaultValue;
            return source.Max(selector);
        }
    }

    #region Cockpit Data Contracts (Poco — no COM objects)

    public class CockpitContext
    {
        public string SchemaVersion { get; set; } = "mechpilot.cockpit.context.v1";
        public string TimestampUtc { get; set; }
        public CockpitClientInfo Client { get; set; }
        public CockpitDocumentInfo ActiveDocument { get; set; }
        public List<CockpitSelectionInfo> Selection { get; set; } = new List<CockpitSelectionInfo>();
        public List<CockpitTreeNode> AssemblyTree { get; set; } = new List<CockpitTreeNode>();
        public CockpitPropertyTable PropertyTable { get; set; }
        public CockpitSummaryInfo Summary { get; set; }
        public List<string> Capabilities { get; set; } = new List<string>();
        public List<CockpitWarning> Warnings { get; set; } = new List<CockpitWarning>();
    }

    public class CockpitClientInfo
    {
        public string EngineerId { get; set; }
        public string SwVersion { get; set; }
        public string AddinVersion { get; set; }
        public string ExecutionMode { get; set; }
        public string MachineName { get; set; }
    }

    public class CockpitDocumentInfo
    {
        public string Title { get; set; }
        public string FilePath { get; set; }
        public string DocType { get; set; }
        public string ConfigurationName { get; set; }
        public bool IsSaved { get; set; }
        public bool IsModified { get; set; }
        public int CustomPropertyCount { get; set; }
        public string FileSize { get; set; }
        public string LastModified { get; set; }
    }

    public class CockpitSelectionInfo
    {
        public int Index { get; set; }
        public string DisplayName { get; set; }
        public string ComponentName { get; set; }
        public string ComponentPath { get; set; }
        public string DocType { get; set; }
        public string Configuration { get; set; }
        public bool IsSuppressed { get; set; }
        public bool IsLightweight { get; set; }
        public string PivotKey { get; set; }
    }

    public class CockpitTreeNode
    {
        public string NodeId { get; set; }
        public string ParentId { get; set; }
        public string DisplayName { get; set; }
        public string Name { get; set; }
        public string ComponentName { get; set; }
        public string DocumentName { get; set; }
        public string InstancePath { get; set; }
        public string DocType { get; set; }
        public string FilePath { get; set; }
        public string Configuration { get; set; }
        public string FileSize { get; set; }
        public int Quantity { get; set; }
        public int Depth { get; set; }
        public int ChildrenCount { get; set; }
        public string PivotKey { get; set; }
        public bool IsExpanded { get; set; }
        public bool IsAssembly { get; set; }
        public bool IsPart { get; set; }
        public bool IsSuppressed { get; set; }
        public bool IsLightweight { get; set; }
        public List<CockpitTreeNode> Children { get; set; } = new List<CockpitTreeNode>();
    }

    public class CockpitPropertyTable
    {
        public string TargetLabel { get; set; }
        public int TotalRows { get; set; }
        public int TotalInstances { get; set; }
        public List<string> IntrinsicColumns { get; set; } = new List<string>();
        public List<string> DynamicColumns { get; set; } = new List<string>();
        public List<CockpitPropertyRow> Rows { get; set; } = new List<CockpitPropertyRow>();
    }

    public class CockpitPropertyRow
    {
        // Stable IDs for frontend linking
        public string PivotKey { get; set; }
        public string DocumentKey { get; set; }
        public string InstanceKey { get; set; }
        public string RowKey { get; set; }
        // Intrinsic identity fields
        public string DisplayName { get; set; }
        public string FilePath { get; set; }
        public string Configuration { get; set; }
        public string DocType { get; set; }
        public int Quantity { get; set; }
        public string FileSize { get; set; }
        // Dynamic custom properties
        public Dictionary<string, CockpitPropertyValue> Properties { get; set; }
            = new Dictionary<string, CockpitPropertyValue>(StringComparer.OrdinalIgnoreCase);
    }

    public class CockpitPropertyValue
    {
        public string RawValue { get; set; }
        public string ResolvedValue { get; set; }
        public int Type { get; set; }
    }

    public class CockpitSummaryInfo
    {
        public int TargetCount { get; set; }
        public int TotalComponents { get; set; }
        public int UniqueDocCount { get; set; }
        public int PartCount { get; set; }
        public int SubAssemblyCount { get; set; }
        public int DrawingCount { get; set; }
        public int SuppressedCount { get; set; }
        public int LightweightCount { get; set; }
        public int ReadFailedCount { get; set; }
        public int CustomPropertyColumnCount { get; set; }
    }

    public class CockpitWarning
    {
        public string Level { get; set; }
        public string Target { get; set; }
        public string Message { get; set; }
    }

    public class CockpitCommandEnvelope
    {
        public string Command { get; set; }
        public string RequestId { get; set; }
        public string TimestampUtc { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }

    public class CockpitResultEnvelope
    {
        public string RequestId { get; set; }
        public bool Success { get; set; }
        public string TimestampUtc { get; set; }
        public CockpitError Error { get; set; }
        public Dictionary<string, object> Data { get; set; }
    }

    public class CockpitError
    {
        public string Code { get; set; }
        public string Message { get; set; }
    }

public class LocalPropertyRules
    {
        public string Version { get; set; } = "demo-2026-06-21";
        public Dictionary<string, Dictionary<string, string>> PropertySets { get; set; }
            = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Get property set by task type and doc type.
        /// Falls back: doc_type match -> task_type match -> first available.
        /// </summary>
        public Dictionary<string, string> GetPropertySet(string taskType, string docType)
        {
            if (PropertySets.ContainsKey(docType))
                return PropertySets[docType];

            if (PropertySets.ContainsKey(taskType))
                return PropertySets[taskType];

            if (PropertySets.Count > 0)
                return PropertySets.Values.First();

            return new Dictionary<string, string>();
        }
    }

    public static class RuleProvider
    {
        public static LocalPropertyRules LoadLocalRules(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var serializer = new JavaScriptSerializer();
                    var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                    var rules = new LocalPropertyRules();

                    if (dict.ContainsKey("version"))
                        rules.Version = Convert.ToString(dict["version"]);

                    if (dict.ContainsKey("property_sets"))
                    {
                        var sets = dict["property_sets"] as Dictionary<string, object>;
                        if (sets != null)
                        {
                            foreach (var kv in sets)
                            {
                                var propDict = kv.Value as Dictionary<string, object>;
                                if (propDict != null)
                                {
                                    var props = new Dictionary<string, string>();
                                    foreach (var pkv in propDict)
                                        props[pkv.Key] = Convert.ToString(pkv.Value);
                                    rules.PropertySets[kv.Key] = props;
                                }
                            }
                        }
                    }

                    return rules;
                }
                catch (Exception ex)
                {
                    SwAgentAddin.WriteTrace("RuleProvider.LoadLocalRules parse error: " + ex.Message);
                }
            }

            // File not found or parse error — create demo rules and save
            var demoRules = CreateDemoRules();
            try
            {
                SaveRules(demoRules, path);
                SwAgentAddin.WriteTrace("RuleProvider: created demo rules at " + path);
            }
            catch { }
            return demoRules;
        }

        public static LocalPropertyRules CreateDemoRules()
        {
            var rules = new LocalPropertyRules();
            rules.PropertySets["part"] = new Dictionary<string, string>
            {
                ["物料名称"] = "{file_name_no_ext}",
                ["图号"] = "{file_name_no_ext}",
                ["文档类型"] = "{doc_type}",
                ["处理状态"] = "MechPilot Local Demo",
                ["处理人"] = "{engineer_id}",
                ["处理日期"] = "{date}"
            };
            rules.PropertySets["assembly"] = new Dictionary<string, string>
            {
                ["装配体名称"] = "{file_name_no_ext}",
                ["图号"] = "{file_name_no_ext}",
                ["文档类型"] = "{doc_type}",
                ["处理状态"] = "MechPilot Local Demo",
                ["处理人"] = "{engineer_id}",
                ["处理日期"] = "{date}"
            };
            rules.PropertySets["drawing"] = new Dictionary<string, string>
            {
                ["图纸名称"] = "{file_name_no_ext}",
                ["图号"] = "{file_name_no_ext}",
                ["文档类型"] = "{doc_type}",
                ["处理状态"] = "MechPilot Local Demo",
                ["处理人"] = "{engineer_id}",
                ["处理日期"] = "{date}"
            };
            return rules;
        }

        private static void SaveRules(LocalPropertyRules rules, string path)
        {
            var dict = new Dictionary<string, object>
            {
                ["version"] = rules.Version,
                ["property_sets"] = rules.PropertySets
            };
            string json = new JavaScriptSerializer().Serialize(dict);
            File.WriteAllText(path, json, Encoding.UTF8);
        }
    }

    #endregion

    #region Taskpane HTML

    internal static class TaskpaneHtml
    {
        public const string DefaultHtml = @"<!doctype html>
<html>
<head>
<meta charset=""utf-8"">
<style>
body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f8fafc;color:#0f172a;font-size:13px}
.head{background:#1f4f8f;color:white;padding:12px 14px;font-weight:700}
.section{padding:12px 14px;border-bottom:1px solid #e2e8f0}
.title{font-size:11px;text-transform:uppercase;color:#64748b;font-weight:700;margin-bottom:8px}
.btn{display:block;width:100%;padding:9px 10px;margin:8px 0;border:1px solid #cbd5e1;background:white;border-radius:6px;text-align:left}
.ok{color:#047857;font-weight:700}
.hint{color:#64748b;font-size:12px;line-height:1.5}
code{background:#e2e8f0;padding:2px 4px;border-radius:4px}
</style>
</head>
<body>
<div class=""head"">MechPilot</div>
<div class=""section"">
  <div class=""title"">Status</div>
  <div class=""ok"">插件已加载</div>
  <div class=""hint"">可通过上方 MechPilot 工具栏执行属性填写、属性检查和图纸审核。</div>
</div>
<div class=""section"">
  <div class=""title"">操作</div>
  <div class=""btn"">属性填写</div>
  <div class=""btn"">属性检查</div>
  <div class=""btn"">图纸审核</div>
</div>
<div class=""section"">
  <div class=""title"">配置</div>
  <div class=""hint"">本地模式会直接写入当前 SolidWorks 文件属性；远程模式会提交任务到 Agent Server。</div>
</div>
</body>
</html>";
    }

    #endregion

    #endregion  // Data Classes
}
