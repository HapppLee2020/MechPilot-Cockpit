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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        internal const string DefaultOnlineSelectionUrl = "https://xtalpi.aiforce.cloud/app/app_4k6beaevn9gub";
        internal const string OnlineSelectionTaskPaneProgId = "SwAgentAddin.OnlineSelectionTaskPane";
        private const int CommandGroupId = 1001;
        private const int FirstMenuId = 0;

        // Command IDs — must match ImageListIndex in 16-icon ribbon strip
        // AI 工具区 (0-5)
        private const int CmdCockpit = 0;
        private const int CmdAIAssistant = 1;
        private const int CmdAIDrawingReview = 2;
        private const int CmdAISelection = 3;
        private const int CmdOnlineSelection = 4;
        private const int CmdMaterialSearch = 5;
        private const int CmdDesignCalc = 6;
        // 本地工程工具区 (6-12)
        private const int CmdPropertyFill = 7;
        private const int CmdReadProperties = 8;
        private const int CmdPropertyCheck = 9;
        private const int CmdBomExport = 10;
        private const int CmdBatchConvert = 11;
        private const int CmdDrawingExport = 12;
        private const int CmdPackageBackup = 13;
        // 系统区 (13-15)
        private const int CmdSettings = 14;
        private const int CmdRulesConfig = 15;
        private const int CmdAbout = 16;

        #endregion

        #region Member Variables

        private ISldWorks _swApp;
        private ICommandManager _cmdMgr;
        private ITaskpaneView _taskpaneView;
        private OnlineSelectionTaskPane _onlineSelectionPane;
        private readonly List<ITaskpaneView> _taskpaneViews = new List<ITaskpaneView>();
        private readonly List<OnlineSelectionTaskPane> _taskpanePanes = new List<OnlineSelectionTaskPane>();
        private AddinConfig _config;
        private LocalPropertyRules _rules;
        private int _addinCookie;
        private readonly HttpClient _httpClient = new HttpClient();

        // ActionRouter / LocalToolbeltExecutor
        private ActionRouter _actionRouter;
        private LocalToolbeltExecutor _localExecutor;

        // Document event handlers
        private Hashtable _openDocuments;
        private bool _autoRefreshEnabled = false;
        /// <summary>仅 local.read_properties 置 true：装配体内 Resolve 轻化件读属性，禁止 OpenDoc6 逐个开文件。</summary>
        private bool _resolveLightweightForPropertyRead;
        private List<IComponent2> _lightweightComponentsToRestore;

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
                InitActionRouter();
                AddCommandMgr();
                AddCustomTaskPanes();

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
                // CKP-004-12: 关闭 Cockpit 前先取消 TopMost 并释放
                SafeCloseCockpit("addin_disconnect");
                RemoveCommandMgr();
                RemoveCustomTaskPanes();
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

        /// <summary>
        /// CKP-004-12: 安全关闭 Cockpit 窗口。SW 退出 / Add-in unload / 异常时调用。
        /// </summary>
        private void SafeCloseCockpit(string reason)
        {
            WriteTrace("SafeCloseCockpit: reason=" + reason + " cockpitExists=" + (_cockpitForm != null));
            if (_cockpitForm == null || _cockpitForm.IsDisposed) return;

            try
            {
                // 确保在 UI 线程操作
                if (_cockpitForm.InvokeRequired)
                {
                    _cockpitForm.BeginInvoke(new Action(() => SafeCloseCockpit(reason)));
                    return;
                }

                // 清理 TopMost 防止残留置顶窗口
                try { _cockpitForm.TopMost = false; } catch { }

                // 关闭窗口（会触发 OnFormClosing → Dispose WebView2）
                try { _cockpitForm.Close(); } catch (Exception ex)
                {
                    WriteTrace("SafeCloseCockpit: Close() failed: " + ex.Message);
                    try { _cockpitForm.Hide(); } catch { }
                    try { _cockpitForm.Dispose(); } catch { }
                }
                WriteTrace("SafeCloseCockpit: closed (reason=" + reason + ")");
            }
            catch (Exception ex)
            {
                WriteTrace("SafeCloseCockpit outer exception: " + ex.Message);
                try { _cockpitForm?.Dispose(); } catch { }
            }
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

            int addItem = (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem;

            // ── AI 工具区 (index 0-5) ──
            cmdGroup.AddCommandItem2("驾驶舱", -1, "打开 AICockpit 总览", "驾驶舱", CmdCockpit, "SwCmd_Cockpit", "", CmdCockpit, addItem);
            cmdGroup.AddCommandItem2("AI助手", -1, "打开 AICockpit 并展开 AI 面板", "AI助手", CmdAIAssistant, "SwCmd_AIAssistant", "", CmdAIAssistant, addItem);
            cmdGroup.AddCommandItem2("图纸审核", -1, "打开 AICockpit 图纸审核页面", "图纸审核", CmdAIDrawingReview, "SwCmd_AIDrawingReview", "", CmdAIDrawingReview, addItem);
            cmdGroup.AddCommandItem2("快速选型", -1, "打开 AICockpit 快速选型页面", "快速选型", CmdAISelection, "SwCmd_AISelection", "", CmdAISelection, addItem);
            cmdGroup.AddCommandItem2("在线选型", -1, "打开 MechPilot 在线选型任务窗格", "在线选型", CmdOnlineSelection, "SwCmd_OnlineSelection", "", CmdOnlineSelection, addItem);
            cmdGroup.AddCommandItem2("物料检索", -1, "打开 AICockpit 物料检索页面", "物料检索", CmdMaterialSearch, "SwCmd_MaterialSearch", "", CmdMaterialSearch, addItem);
            cmdGroup.AddCommandItem2("设计计算", -1, "打开 AICockpit 设计计算页面", "设计计算", CmdDesignCalc, "SwCmd_DesignCalc", "", CmdDesignCalc, addItem);

            // ── 本地工程工具区 (index 6-12) ──
            cmdGroup.AddCommandItem2("属性填写", -1, "按当前模式填写自定义属性", "属性填写", CmdPropertyFill, "SwCmd_PropertyFill", "", CmdPropertyFill, addItem);
            cmdGroup.AddCommandItem2("读取属性", -1, "读取当前文档自定义属性", "读取属性", CmdReadProperties, "SwCmd_ReadProperties", "", CmdReadProperties, addItem);
            cmdGroup.AddCommandItem2("属性检查", -1, "检查当前模型自定义属性", "属性检查", CmdPropertyCheck, "SwCmd_PropertyCheck", "", CmdPropertyCheck, addItem);
            cmdGroup.AddCommandItem2("BOM导出", -1, "导出物料清单", "BOM导出", CmdBomExport, "SwCmd_BomExport", "", CmdBomExport, addItem);
            cmdGroup.AddCommandItem2("批量转换", -1, "批量转换文档格式", "批量转换", CmdBatchConvert, "SwCmd_BatchConvert", "", CmdBatchConvert, addItem);
            cmdGroup.AddCommandItem2("图纸导出", -1, "导出工程图为 PDF/DWG", "图纸导出", CmdDrawingExport, "SwCmd_DrawingExport", "", CmdDrawingExport, addItem);
            cmdGroup.AddCommandItem2("打包备份", -1, "打包备份当前工程", "打包备份", CmdPackageBackup, "SwCmd_PackageBackup", "", CmdPackageBackup, addItem);

            // ── 系统区 (index 13-15) ──
            cmdGroup.AddCommandItem2("插件设置", -1, "打开插件配置文件", "插件设置", CmdSettings, "SwCmd_Settings", "", CmdSettings, addItem);
            cmdGroup.AddCommandItem2("规则配置", -1, "打开规则配置文件", "规则配置", CmdRulesConfig, "SwCmd_RulesConfig", "", CmdRulesConfig, addItem);
            cmdGroup.AddCommandItem2("关于", -1, "查看版本与部署信息", "关于", CmdAbout, "SwCmd_About", "", CmdAbout, addItem);

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
                // Ribbon 风格优先，蓝图备选
                string sm20 = File.Exists(Path.Combine(dir, "assets/icons/mechpilot-ribbon-main-20.bmp"))
                    ? "assets/icons/mechpilot-ribbon-main-20.bmp" : "assets/icons/mechpilot-blueprint-main-20.bmp";
                string lg32 = File.Exists(Path.Combine(dir, "assets/icons/mechpilot-ribbon-main-32.bmp"))
                    ? "assets/icons/mechpilot-ribbon-main-32.bmp" : "assets/icons/mechpilot-blueprint-main-32.bmp";
                string si20 = File.Exists(Path.Combine(dir, "assets/icons/mechpilot-ribbon-icons-16-20.bmp"))
                    ? "assets/icons/mechpilot-ribbon-icons-16-20.bmp" : "assets/icons/mechpilot-blueprint-icons-16-20.bmp";
                string li32 = File.Exists(Path.Combine(dir, "assets/icons/mechpilot-ribbon-icons-16-32.bmp"))
                    ? "assets/icons/mechpilot-ribbon-icons-16-32.bmp" : "assets/icons/mechpilot-blueprint-icons-16-32.bmp";
                cmdGroup.SmallMainIcon = Path.Combine(dir, sm20);
                cmdGroup.LargeMainIcon = Path.Combine(dir, lg32);
                cmdGroup.SmallIconList = Path.Combine(dir, si20);
                cmdGroup.LargeIconList = Path.Combine(dir, li32);
                WriteTrace("ApplyCommandGroupIcons: ribbon icons assigned (fallback to blueprint if missing).");
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

            int textBelow = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

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

                    // ── Box 1: AI 工具区 (6 buttons) ──
                    CommandTabBox boxAI = tab.AddCommandTabBox();
                    if (boxAI != null)
                    {
                        int[] aiIds = {
                            cmdGroup.get_CommandID(CmdCockpit),
                            cmdGroup.get_CommandID(CmdAIAssistant),
                            cmdGroup.get_CommandID(CmdAIDrawingReview),
                            cmdGroup.get_CommandID(CmdAISelection),
                            cmdGroup.get_CommandID(CmdOnlineSelection),
                            cmdGroup.get_CommandID(CmdMaterialSearch),
                            cmdGroup.get_CommandID(CmdDesignCalc)
                        };
                        int[] aiStyles = { textBelow, textBelow, textBelow, textBelow, textBelow, textBelow, textBelow };
                        boxAI.AddCommands(aiIds, aiStyles);
                    }

                    // ── Box 2: 本地工程工具区 (7 buttons) ──
                    CommandTabBox boxLocal = tab.AddCommandTabBox();
                    if (boxLocal != null)
                    {
                        int[] localIds = {
                            cmdGroup.get_CommandID(CmdPropertyFill),
                            cmdGroup.get_CommandID(CmdReadProperties),
                            cmdGroup.get_CommandID(CmdPropertyCheck),
                            cmdGroup.get_CommandID(CmdBomExport),
                            cmdGroup.get_CommandID(CmdBatchConvert),
                            cmdGroup.get_CommandID(CmdDrawingExport),
                            cmdGroup.get_CommandID(CmdPackageBackup)
                        };
                        int[] localStyles = { textBelow, textBelow, textBelow, textBelow, textBelow, textBelow, textBelow };
                        boxLocal.AddCommands(localIds, localStyles);
                    }

                    // ── Box 3: 系统区 (3 buttons) ──
                    CommandTabBox boxSys = tab.AddCommandTabBox();
                    if (boxSys != null)
                    {
                        int[] sysIds = {
                            cmdGroup.get_CommandID(CmdSettings),
                            cmdGroup.get_CommandID(CmdRulesConfig),
                            cmdGroup.get_CommandID(CmdAbout)
                        };
                        int[] sysStyles = { textBelow, textBelow, textBelow };
                        boxSys.AddCommands(sysIds, sysStyles);
                    }

                    tab.Visible = true;
                    tab.Active = true;
                    WriteTrace("AddCommandTabs: 3-zone built. docType=" + docType);
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
            // Ribbon 首选 + 蓝图备选，任一存在即可
            string[][] pairs =
            {
                new[] { "assets/icons/mechpilot-ribbon-main-20.bmp", "assets/icons/mechpilot-blueprint-main-20.bmp" },
                new[] { "assets/icons/mechpilot-ribbon-main-32.bmp", "assets/icons/mechpilot-blueprint-main-32.bmp" },
                new[] { "assets/icons/mechpilot-ribbon-icons-16-20.bmp", "assets/icons/mechpilot-blueprint-icons-16-20.bmp" },
                new[] { "assets/icons/mechpilot-ribbon-icons-16-32.bmp", "assets/icons/mechpilot-blueprint-icons-16-32.bmp" }
            };
            foreach (var pair in pairs)
            {
                if (!File.Exists(Path.Combine(dir, pair[0])) && !File.Exists(Path.Combine(dir, pair[1])))
                    WriteTrace("WARNING: icon file missing: " + pair[0] + " / " + pair[1] + " — toolbar icons may not display.");
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

        private void AddCustomTaskPanes()
        {
            try
            {
                RemoveCustomTaskPanes();

                string htmlPath = Path.Combine(GetAddinDirectory(), "frontend/taskpane.html");
                if (!File.Exists(htmlPath))
                    File.WriteAllText(htmlPath, TaskpaneHtml.DefaultHtml, Encoding.UTF8);

                foreach (var entry in GetCustomTaskPaneEntries().Reverse())
                {
                    string iconPath = GetCustomTaskPaneIconPath(entry.IconKey);
                    WriteTrace("AddCustomTaskPanes: creating index=" + entry.Index + " title=" + entry.Title + " icon=" + iconPath);

                    ITaskpaneView view = _swApp.CreateTaskpaneView2(iconPath ?? "", entry.Title);
                    if (view == null)
                    {
                        WriteTrace("AddCustomTaskPanes: CreateTaskpaneView2 returned null for " + entry.Title + "; trying CreateTaskpaneView3.");
                        view = _swApp.CreateTaskpaneView3(null, entry.Title);
                    }

                    if (view == null)
                    {
                        WriteTrace("AddCustomTaskPanes: failed to create pane for " + entry.Title);
                        continue;
                    }

                    _taskpaneViews.Add(view);
                    if (entry.Index == 1)
                        _taskpaneView = view;

                    object control = null;
                    try
                    {
                        control = view.AddControl(OnlineSelectionTaskPaneProgId, "");
                        var pane = control as OnlineSelectionTaskPane;
                        if (pane != null)
                        {
                            pane.Initialize(entry.Title, entry.Url);
                            _taskpanePanes.Add(pane);
                            if (entry.Index == 1)
                                _onlineSelectionPane = pane;
                        }
                    }
                    catch (Exception controlEx)
                    {
                        WriteTrace("AddCustomTaskPanes: WebView2 pane control failed for " + entry.Title + ": " + controlEx);
                    }

                    if (control == null)
                    {
                        try
                        {
                            control = view.AddControl("Shell.Explorer.2", "");
                            if (control != null && !string.IsNullOrWhiteSpace(entry.Url) && !IsShellOpenTarget(entry.Url))
                            {
                                dynamic browser = control;
                                browser.Navigate(entry.Url);
                            }
                        }
                        catch (Exception browserEx)
                        {
                            WriteTrace("AddCustomTaskPanes: browser fallback failed for " + entry.Title + ": " + browserEx);
                        }
                    }
                }

                WriteTrace("AddCustomTaskPanes: created count=" + _taskpaneViews.Count);
            }
            catch (Exception ex)
            {
                WriteTrace("AddCustomTaskPanes exception: " + ex);
            }
        }

        private void RemoveCustomTaskPanes()
        {
            try
            {
                foreach (var view in _taskpaneViews.ToArray())
                {
                    try { view.DeleteView(); }
                    catch (Exception innerEx) { WriteTrace("RemoveCustomTaskPanes: DeleteView failed: " + innerEx.Message); }
                }

                _taskpaneViews.Clear();
                _taskpanePanes.Clear();
                _taskpaneView = null;
                _onlineSelectionPane = null;
                WriteTrace("RemoveCustomTaskPanes: deleted.");
            }
            catch (Exception ex)
            {
                WriteTrace("RemoveCustomTaskPanes exception: " + ex);
            }
        }

        private void AddTaskPane()
        {
            try
            {
                string iconPath = GetTaskPaneIconPath();
                WriteTrace("AddTaskPane: creating with CreateTaskpaneView2... icon=" + iconPath);
                _taskpaneView = _swApp.CreateTaskpaneView2(iconPath ?? "", "MechPilot 在线选型");
                if (_taskpaneView == null)
                {
                    WriteTrace("AddTaskPane: CreateTaskpaneView2 returned null; trying CreateTaskpaneView3 without image list.");
                    _taskpaneView = _swApp.CreateTaskpaneView3(null, "MechPilot 在线选型");
                }
                if (_taskpaneView != null)
                {
                    string htmlPath = Path.Combine(GetAddinDirectory(), "frontend/taskpane.html");
                    if (!File.Exists(htmlPath))
                        File.WriteAllText(htmlPath, TaskpaneHtml.DefaultHtml, Encoding.UTF8);

                    object control = null;
                    try
                    {
                        control = _taskpaneView.AddControl(OnlineSelectionTaskPaneProgId, "");
                        _onlineSelectionPane = control as OnlineSelectionTaskPane;
                        _onlineSelectionPane?.Initialize(GetOnlineSelectionUrl());
                    }
                    catch (Exception controlEx)
                    {
                        WriteTrace("AddTaskPane: WebView2 pane control failed, falling back to browser control: " + controlEx);
                    }

                    if (control == null)
                        control = _taskpaneView.AddControl("Shell.Explorer.2", "");

                    if (control != null)
                    {
                        if (_onlineSelectionPane == null)
                        {
                            dynamic browser = control;
                            browser.Navigate(GetOnlineSelectionUrl());
                        }
                    }
                    WriteTrace("AddTaskPane: created with panel. control=" + (control == null ? "null" : control.GetType().FullName));
                }
                else
                {
                    WriteTrace("AddTaskPane: CreateTaskpaneView2/CreateTaskpaneView3 returned null.");
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
                    _onlineSelectionPane = null;
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
                    AddCustomTaskPanes();
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
            try
            {
                if (_swApp != null)
                {
                    ((DSldWorksEvents_Event)_swApp).ActiveDocChangeNotify += OnActiveDocChange;
                    WriteTrace("AttachSWEvents: ActiveDocChangeNotify subscribed.");
                }
            }
            catch (Exception ex)
            {
                WriteTrace("AttachSWEvents exception: " + ex);
            }
        }

        private void DetachSWEvents()
        {
            try
            {
                if (_swApp != null)
                {
                    ((DSldWorksEvents_Event)_swApp).ActiveDocChangeNotify -= OnActiveDocChange;
                    WriteTrace("DetachSWEvents: ActiveDocChangeNotify unsubscribed.");
                }
            }
            catch (Exception ex)
            {
                WriteTrace("DetachSWEvents exception: " + ex);
            }
        }

        private int OnActiveDocChange()
        {
            try
            {
                WriteTrace("OnActiveDocChange: autoRefresh=" + _autoRefreshEnabled);
                if (_autoRefreshEnabled && _cockpitForm != null)
                {
                    // Build fresh context and push to WebView2
                    string contextJson = BuildCockpitContext();
                    if (!string.IsNullOrEmpty(contextJson))
                    {
                        _cockpitForm.PushContextToWebView(contextJson);
                    }
                }
                return 0; // S_OK
            }
            catch (Exception ex)
            {
                WriteTrace("OnActiveDocChange exception: " + ex);
                return 0;
            }
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

                // If already open, bring to front and ensure maximized
                if (_cockpitForm != null && !_cockpitForm.IsDisposed)
                {
                    _cockpitForm.BringToFront();
                    _cockpitForm.EnsureCustomMaximized();  // 使用自定义最大化，不遮挡任务栏
                    return;
                }

                _cockpitForm = new CockpitForm(_config, BuildCockpitContext, HandleCockpitCommandAsync);
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

        public void SwCmd_Settings()
        {
            OpenConfigFile();
        }

        public void SwCmd_ReadProperties()
        {
            ExecuteReadProperties();
        }

        // ── AI 工具区回调 ──

        public void SwCmd_AIAssistant()
        {
            OpenCockpitPage("assistant");
        }

        public void SwCmd_AIDrawingReview()
        {
            OpenCockpitPage("drawing");
        }

        public void SwCmd_AISelection()
        {
            OpenCockpitPage("selection");
        }

        public void SwCmd_OnlineSelection()
        {
            ShowOnlineSelectionTaskPane();
        }

        public void SwCmd_MaterialSearch()
        {
            OpenCockpitPage("material");
        }

        public void SwCmd_DesignCalc()
        {
            OpenCockpitPage("design");
        }

        // ── 本地工程工具区回调 ──

        public void SwCmd_BomExport()
        {
            ExecuteLocalTool("bom", "export", "BOM导出");
        }

        public void SwCmd_BatchConvert()
        {
            ExecuteLocalTool("file", "convert", "批量转换");
        }

        public void SwCmd_DrawingExport()
        {
            ExecuteLocalTool("drawing", "export", "图纸导出");
        }

        public void SwCmd_PackageBackup()
        {
            ExecuteLocalTool("package", "backup", "打包备份");
        }

        // ── 系统区回调 ──

        public void SwCmd_RulesConfig()
        {
            string rulesPath = Path.Combine(GetAddinDirectory(), "config/rules.local.json");
            try
            {
                if (!File.Exists(rulesPath))
                {
                    SafeMessage("规则文件不存在：\n" + rulesPath, MessageBoxIcon.Warning);
                    return;
                }
                Process.Start(rulesPath);
            }
            catch
            {
                SafeMessage("规则配置文件：\n" + rulesPath, MessageBoxIcon.Information);
            }
        }

        public void SwCmd_About()
        {
            string addinDir = GetAddinDirectory();
            string logPath = Path.Combine(addinDir, "addin-load.log");
            string ver = typeof(SwAgentAddin).Assembly.GetName().Version.ToString();
            SafeMessage(
                "MechPilot v" + ver + "\n" +
                "SolidWorks 2022 Add-in\n" +
                "双核心架构: AIPilot / LocalToolbelt\n\n" +
                "部署目录: " + addinDir + "\n" +
                "日志文件: " + logPath + "\n" +
                "执行模式: " + (_config?.ExecutionMode ?? "local") + "\n" +
                "工程师ID: " + (_config?.EngineerId ?? ""),
                MessageBoxIcon.Information);
        }

        /// <summary>
        /// 打开 AICockpit 并导航到指定页面
        /// </summary>
        private void OpenCockpitPage(string page)
        {
            try
            {
                if (!_config.CockpitEnabled)
                {
                    SafeMessage("AICockpit 已在配置中禁用。", MessageBoxIcon.Information);
                    return;
                }

                if (_cockpitForm != null && !_cockpitForm.IsDisposed)
                {
                    _cockpitForm.BringToFront();
                    _cockpitForm.WindowState = FormWindowState.Maximized;
                    _cockpitForm.NavigatePage(page);
                    return;
                }

                _cockpitForm = new CockpitForm(_config, BuildCockpitContext, HandleCockpitCommandAsync);
                _cockpitForm.FormClosed += (s, e) => { _cockpitForm = null; };
                _cockpitForm.Show();
                _cockpitForm.NavigatePage(page);
                WriteTrace("OpenCockpitPage: " + page);
            }
            catch (Exception ex)
            {
                WriteTrace("OpenCockpitPage exception (" + page + "): " + ex);
                if (ex.Message.Contains("WebView2") || (ex.InnerException != null && ex.InnerException.Message.Contains("WebView2")))
                {
                    SafeMessage(
                        "WebView2 运行时未安装。\n\n" +
                        "AICockpit 需要 Microsoft WebView2 Runtime。\n" +
                        "请从 https://developer.microsoft.com/en-us/microsoft-edge/webview2/ 下载安装后重试。",
                        MessageBoxIcon.Warning);
                }
                else
                {
                    SafeMessage("AICockpit 打开失败：" + ex.Message, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// 本地工具统一入口 — 通过 ActionRouter 或直接调用 LocalToolbeltExecutor
        /// </summary>
        private void ExecuteLocalTool(string feature, string action, string displayName)
        {
            _ = ExecuteLocalToolAsync(feature, action, displayName);
        }

        private async Task ExecuteLocalToolAsync(string feature, string action, string displayName)
        {
            try
            {
                if (_actionRouter != null)
                {
                    var cmd = new MechPilotCommand
                    {
                        CommandId = "cmd-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        Source = "toolbar",
                        Feature = feature,
                        Action = action,
                        Executor = "local"
                    };
                    string resultJson = await _actionRouter.HandleCommandAsync(cmd.ToJson()).ConfigureAwait(true);
                    WriteTrace("ExecuteLocalTool " + feature + "." + action + ": " + resultJson);
                    SafeMessage(displayName + " 已执行。详情请查看日志。", MessageBoxIcon.Information);
                }
                else if (_localExecutor != null)
                {
                    var cmd = new MechPilotCommand
                    {
                        CommandId = "cmd-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        Source = "toolbar",
                        Feature = feature,
                        Action = action,
                        Executor = "local"
                    };
                    MechPilotResult result = null;
                    switch (feature)
                    {
                        case "bom": result = _localExecutor.BomExport(cmd); break;
                        case "file": result = _localExecutor.BatchConvert(cmd); break;
                        case "drawing": result = _localExecutor.DrawingExport(cmd); break;
                        case "package": result = _localExecutor.PackageBackup(cmd); break;
                        default: result = new MechPilotResult { Ok = false, Message = "未知功能: " + feature }; break;
                    }
                    SafeMessage(result?.Message ?? displayName + " 执行完成", result?.Ok == true ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
                else
                {
                    SafeMessage(displayName + "：执行器未就绪，请重新加载插件。", MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                WriteTrace("ExecuteLocalTool error (" + feature + "." + action + "): " + ex);
                SafeMessage(displayName + " 执行失败：" + ex.Message, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// WebView2 cockpit 命令桥 — 前端 JS 通过 WebMessageReceived 发送 JSON 命令（异步，避免阻塞 STA）
        /// </summary>
        private async Task<string> HandleCockpitCommandAsync(string commandJson)
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

                    case "local.set_auto_refresh":
                        var arPayload = envelope.ContainsKey("payload") ? envelope["payload"] as Dictionary<string, object> : null;
                        bool enabled = arPayload != null && arPayload.ContainsKey("enabled") && Convert.ToBoolean(arPayload["enabled"]);
                        _autoRefreshEnabled = enabled;
                        WriteTrace("Auto-refresh " + (enabled ? "enabled" : "disabled"));
                        return MakeCockpitResult(requestId, true, "Auto-refresh " + (enabled ? "已开启" : "已关闭"), null, null);

                    case "local.read_properties":
                        _resolveLightweightForPropertyRead = true;
                        string ctxJsonRead = BuildCockpitContext();
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object>
                        {
                            ["command"] = command,
                            ["context_json"] = ctxJsonRead
                        });

                    case "refresh_context":
                        string ctxJson = BuildCockpitContext();
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object>
                        {
                            ["command"] = command,
                            ["context_json"] = ctxJson
                        });

                    case "window_close":
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object> { ["action"] = "close" });

                    case "window_minimize":
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object> { ["action"] = "minimize" });

                    case "window_maximize":
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object> { ["action"] = "maximize" });

                    case "agent.health.check":
                        return HandleHermesHealthCheck(requestId);

                    case "local.material_review.write_properties":
                    case "local.write_properties":
                        return await Task.Run(() => HandleLocalMaterialReviewWrite(requestId, envelope)).ConfigureAwait(false);

                    case "local.pdm.status":
                        return await Task.Run(() => HandlePdmFileStatus(requestId, envelope)).ConfigureAwait(false);

                    case "local.pdm.checkout":
                        return await Task.Run(() => HandlePdmCheckout(requestId, envelope)).ConfigureAwait(false);

                    case "local.pdm.checkin":
                        return await Task.Run(() => HandlePdmCheckin(requestId, envelope)).ConfigureAwait(false);

                    case "attribute.rules.load":
                        return HandleAttributeRulesLoad(requestId);

                    default:
                        if (_actionRouter != null)
                        {
                            try
                            {
                                string routed = await _actionRouter.HandleCommandAsync(commandJson).ConfigureAwait(false);
                                WriteTrace("HandleCockpitCommandAsync routed via ActionRouter: " + command);
                                return routed;
                            }
                            catch (Exception routeEx)
                            {
                                WriteTrace("ActionRouter error for " + command + ": " + routeEx);
                                return MakeCockpitResult(requestId, false, "ROUTER_ERROR", "命令路由失败: " + routeEx.Message);
                            }
                        }
                        return MakeCockpitResult(requestId, false, "UNKNOWN_COMMAND",
                            "未实现的命令: " + command + "。ActionRouter 未初始化。");
                }
            }
            catch (Exception ex)
            {
                WriteTrace("HandleCockpitCommandAsync error: " + ex);
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

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return serializer.Serialize(result);
        }

        private string HandleHermesHealthCheck(string requestId)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var agentCfg = _config?.AgentServer;
                if (agentCfg == null || string.IsNullOrWhiteSpace(agentCfg.BaseUrl))
                {
                    return MakeCockpitResult(requestId, false, "NO_HERMES_CONFIG",
                        "Hermes 未配置：config.json 中 agent_server.base_url 为空");
                }

                string baseUrl = agentCfg.BaseUrl.TrimEnd('/');
                string endpoint = agentCfg.JobSubmitEndpoint ?? "/v1/runs";
                if (!endpoint.StartsWith("/")) endpoint = "/" + endpoint;
                string fullUrl = baseUrl + endpoint;
                string apiKey = agentCfg.ApiKey ?? "";

                using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    if (!string.IsNullOrEmpty(apiKey))
                        client.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                    var payload = new Dictionary<string, object>
                    {
                        ["input"] = "MechPilot health check, reply OK only",
                        ["metadata"] = new Dictionary<string, object>
                        {
                            ["source"] = "cockpit_health_check"
                        }
                    };
                    string json = new JavaScriptSerializer().Serialize(payload);
                    var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                    var response = client.PostAsync(fullUrl, content).GetAwaiter().GetResult();
                    int httpStatus = (int)response.StatusCode;

                    sw.Stop();

                    if (httpStatus == 202 || httpStatus == 200)
                    {
                        return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object>
                        {
                            ["status"] = "online",
                            ["base_url"] = baseUrl,
                            ["endpoint"] = endpoint,
                            ["http_status"] = httpStatus,
                            ["message"] = "OK (" + httpStatus + ")",
                            ["checked_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            ["duration_ms"] = sw.ElapsedMilliseconds
                        });
                    }
                    else if (httpStatus == 401 || httpStatus == 403)
                    {
                        return MakeCockpitResult(requestId, false, "AUTH_FAILED",
                            "Hermes 返回 " + httpStatus + "：认证失败",
                            new Dictionary<string, object>
                            {
                                ["status"] = "auth_required",
                                ["base_url"] = baseUrl,
                                ["endpoint"] = endpoint,
                                ["http_status"] = httpStatus,
                                ["message"] = "HTTP " + httpStatus + " — 认证失败",
                                ["checked_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                ["duration_ms"] = sw.ElapsedMilliseconds
                            });
                    }
                    else if (httpStatus == 405)
                    {
                        return MakeCockpitResult(requestId, false, "WRONG_METHOD",
                            "Hermes 返回 405：端点方法不匹配",
                            new Dictionary<string, object>
                            {
                                ["status"] = "reachable_wrong_method",
                                ["base_url"] = baseUrl,
                                ["endpoint"] = endpoint,
                                ["http_status"] = httpStatus,
                                ["message"] = "HTTP 405 — 服务可达，端点方法不匹配",
                                ["checked_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                ["duration_ms"] = sw.ElapsedMilliseconds
                            });
                    }
                    else
                    {
                        return MakeCockpitResult(requestId, false, "HERMES_ERROR",
                            "Hermes 返回 " + httpStatus,
                            new Dictionary<string, object>
                            {
                                ["status"] = "error",
                                ["base_url"] = baseUrl,
                                ["endpoint"] = endpoint,
                                ["http_status"] = httpStatus,
                                ["message"] = "HTTP " + httpStatus,
                                ["checked_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                ["duration_ms"] = sw.ElapsedMilliseconds
                            });
                    }
                }
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                sw.Stop();
                string msg = ex.Message ?? "";
                WriteTrace("HandleHermesHealthCheck: request failed: " + msg);
                return MakeCockpitResult(requestId, false, "HERMES_UNREACHABLE",
                    "Hermes 不可达：" + (msg.Length > 120 ? msg.Substring(0, 120) + "..." : msg),
                    new Dictionary<string, object>
                    {
                        ["status"] = "offline",
                        ["base_url"] = _config?.AgentServer?.BaseUrl ?? "",
                        ["endpoint"] = _config?.AgentServer?.JobSubmitEndpoint ?? "/v1/runs",
                        ["http_status"] = null,
                        ["message"] = msg.Length > 120 ? msg.Substring(0, 120) + "..." : msg,
                        ["checked_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ["duration_ms"] = sw.ElapsedMilliseconds
                    });
            }
            catch (Exception ex)
            {
                sw.Stop();
                WriteTrace("HandleHermesHealthCheck: exception: " + ex.Message);
                return MakeCockpitResult(requestId, false, "HEALTH_CHECK_ERROR",
                    "健康检查异常",
                    new Dictionary<string, object>
                    {
                        ["status"] = "error",
                        ["base_url"] = _config?.AgentServer?.BaseUrl ?? "",
                        ["endpoint"] = _config?.AgentServer?.JobSubmitEndpoint ?? "/v1/runs",
                        ["http_status"] = null,
                        ["message"] = ex.Message ?? "",
                        ["checked_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ["duration_ms"] = sw.ElapsedMilliseconds
                    });
            }
        }

        #endregion

        #region CKP-005-01: Local Material Review + PDM Lifecycle

        private PdmLifecycleHelper _pdmHelper;
        private PdmComHelper _pdmComHelper;
        private PdmLifecycleHelper PdmHelper
        {
            get { return _pdmHelper ?? (_pdmHelper = new PdmLifecycleHelper(useMock: false)); }
        }
        private PdmComHelper PdmCom
        {
            get { return _pdmComHelper ?? (_pdmComHelper = new PdmComHelper()); }
        }

        private string HandleLocalMaterialReviewWrite(string requestId, Dictionary<string, object> envelope)
        {
            var payload = envelope != null && envelope.ContainsKey("payload")
                ? envelope["payload"] as Dictionary<string, object> : new Dictionary<string, object>();

            var results = new List<object>();
            int successCount = 0, skipCount = 0, failCount = 0;

            var items = payload != null && payload.ContainsKey("items")
                ? payload["items"] as System.Collections.ArrayList : null;
            if (items == null || items.Count == 0)
                return MakeCockpitResult(requestId, false, "NO_ITEMS", "无要写入的项目");

            IModelDoc2 activeDoc = _swApp?.ActiveDoc as IModelDoc2;

            foreach (var itemObj in items)
            {
                var item = itemObj as Dictionary<string, object>;
                if (item == null) { failCount++; continue; }
                string filePath = Convert.ToString(item.ContainsKey("file_path") ? item["file_path"] : "");
                string displayName = Convert.ToString(item.ContainsKey("display_name") ? item["display_name"] : "");
                string decision = Convert.ToString(item.ContainsKey("decision") ? item["decision"] : "skip");
                var expectedProps = item.ContainsKey("expected_properties")
                    ? item["expected_properties"] as Dictionary<string, object> : null;

                if (decision == "skip") { skipCount++; continue; }
                if (decision != "fix" || expectedProps == null) { skipCount++; continue; }

                bool openedForWrite = false;
                IModelDoc2 targetModel = null;
                try
                {
                    targetModel = ResolveModelForLocalWrite(activeDoc, filePath, out openedForWrite);

                    if (targetModel == null)
                    {
                        failCount++;
                        results.Add(new Dictionary<string, object>
                        {
                            ["file_path"] = filePath, ["display_name"] = displayName,
                            ["success"] = false, ["error"] = "无法打开模型: " + filePath
                        });
                        continue;
                    }

                    CustomPropertyManager mgr = targetModel.Extension.get_CustomPropertyManager("");
                    if (mgr == null) { failCount++; continue; }

                    foreach (var kv in expectedProps)
                    {
                        string key = kv.Key;
                        string value = Convert.ToString(kv.Value ?? "");
                        try { mgr.Add3(key, 30, value, 1); } catch { }
                    }

                    try { targetModel.ForceRebuild3(false); } catch { }
                    try { targetModel.Save2(true); } catch { }

                    successCount++;
                    results.Add(new Dictionary<string, object>
                    {
                        ["file_path"] = filePath, ["display_name"] = displayName,
                        ["success"] = true,
                        ["opened_for_write"] = openedForWrite,
                        ["properties_written"] = expectedProps.Keys.ToList()
                    });
                }
                catch (Exception ex)
                {
                    failCount++;
                    results.Add(new Dictionary<string, object>
                    {
                        ["file_path"] = filePath, ["display_name"] = displayName,
                        ["success"] = false, ["error"] = ex.Message
                    });
                }
                finally
                {
                    if (openedForWrite && targetModel != null)
                    {
                        try { _swApp.CloseDoc(targetModel.GetTitle()); } catch { }
                    }
                }
            }

            return MakeCockpitResult(requestId, true, null, null, new Dictionary<string, object>
            {
                ["execution_mode"] = "local",
                ["success_count"] = successCount,
                ["skip_count"] = skipCount,
                ["fail_count"] = failCount,
                ["results"] = results
            });
        }

        /// <summary>
        /// P0-3: 匹配已打开文档，必要时 Silent OpenDoc6 写入后关闭。
        /// </summary>
        private IModelDoc2 ResolveModelForLocalWrite(IModelDoc2 activeDoc, string filePath, out bool openedForWrite)
        {
            openedForWrite = false;
            if (string.IsNullOrWhiteSpace(filePath) || _swApp == null) return null;

            if (activeDoc != null
                && string.Equals(activeDoc.GetPathName() ?? "", filePath, StringComparison.OrdinalIgnoreCase))
                return activeDoc;

            IModelDoc2 existing = FindOpenModelByPath(filePath);
            if (existing != null) return existing;

            if (!File.Exists(filePath)) return null;

            int docType = (int)swDocumentTypes_e.swDocPART;
            string ext = Path.GetExtension(filePath);
            if (string.Equals(ext, ".SLDASM", StringComparison.OrdinalIgnoreCase))
                docType = (int)swDocumentTypes_e.swDocASSEMBLY;
            else if (string.Equals(ext, ".SLDDRW", StringComparison.OrdinalIgnoreCase))
                docType = (int)swDocumentTypes_e.swDocDRAWING;

            int errors = 0, warnings = 0;
            var opened = _swApp.OpenDoc6(filePath, docType, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings) as IModelDoc2;
            if (opened == null) return null;
            openedForWrite = true;
            return opened;
        }

        private IModelDoc2 FindOpenModelByPath(string filePath)
        {
            if (_swApp == null || string.IsNullOrWhiteSpace(filePath)) return null;

            try
            {
                var byName = _swApp.GetOpenDocumentByName(filePath) as IModelDoc2;
                if (byName != null && PathsEqual(byName.GetPathName(), filePath)) return byName;

                string fileName = Path.GetFileName(filePath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    byName = _swApp.GetOpenDocumentByName(fileName) as IModelDoc2;
                    if (byName != null && PathsEqual(byName.GetPathName(), filePath)) return byName;
                }

                object docsObj = _swApp.GetDocuments();
                if (docsObj is object[] docs)
                {
                    foreach (object docObj in docs)
                    {
                        var doc = docObj as IModelDoc2;
                        if (doc != null && PathsEqual(doc.GetPathName(), filePath))
                            return doc;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteTrace("FindOpenModelByPath failed: " + ex.Message);
            }

            return null;
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private string HandlePdmFileStatus(string requestId, Dictionary<string, object> envelope)
        {
            var payload = envelope != null && envelope.ContainsKey("payload")
                ? envelope["payload"] as Dictionary<string, object> : new Dictionary<string, object>();
            string filePath = Convert.ToString(payload != null && payload.ContainsKey("file_path") ? payload["file_path"] : "");
            var result = PdmHelper.GetFileStatus(filePath);
            result["file_path"] = filePath;
            return MakeCockpitResult(requestId, true, null, null, result);
        }

        private string HandlePdmCheckout(string requestId, Dictionary<string, object> envelope)
        {
            var payload = envelope != null && envelope.ContainsKey("payload")
                ? envelope["payload"] as Dictionary<string, object> : new Dictionary<string, object>();
            string filePath = Convert.ToString(payload != null && payload.ContainsKey("file_path") ? payload["file_path"] : "");
            string comment = Convert.ToString(payload != null && payload.ContainsKey("comment") ? payload["comment"] : "");
            var result = PdmHelper.CheckoutFile(filePath, comment);
            result["file_path"] = filePath;
            return MakeCockpitResult(requestId, true, null, null, result);
        }

        private string HandlePdmCheckin(string requestId, Dictionary<string, object> envelope)
        {
            var payload = envelope != null && envelope.ContainsKey("payload")
                ? envelope["payload"] as Dictionary<string, object> : new Dictionary<string, object>();
            string filePath = Convert.ToString(payload != null && payload.ContainsKey("file_path") ? payload["file_path"] : "");
            string comment = Convert.ToString(payload != null && payload.ContainsKey("comment") ? payload["comment"] : "");
            string newVersion = Convert.ToString(payload != null && payload.ContainsKey("new_version") ? payload["new_version"] : "");
            var result = PdmHelper.CheckinFile(filePath, comment, newVersion);
            result["file_path"] = filePath;
            return MakeCockpitResult(requestId, true, null, null, result);
        }

        // CKP-004-16: Load machine-executable attribute rules for local pre-check.
        // Reads rules/attribute-review/attribute_rules.generated.json from the addin dir.
        // Degrades gracefully: missing/corrupt file -> success=false with error code,
        // so the front-end can fall back to "no local pre-check" without blocking.
        private string HandleAttributeRulesLoad(string requestId)
        {
            try
            {
                string rulesPath = Path.Combine(GetAddinDirectory(),
                    "rules", "attribute-review", "attribute_rules.generated.json");
                if (!File.Exists(rulesPath))
                {
                    WriteTrace("Attribute rules file not found: " + rulesPath);
                    return MakeCockpitResult(requestId, false, "ERR_RULES_NOT_FOUND",
                        "attribute_rules.generated.json not found at rules/attribute-review/", null);
                }
                string json = File.ReadAllText(rulesPath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                var parsed = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (parsed == null)
                {
                    return MakeCockpitResult(requestId, false, "ERR_RULES_PARSE_FAILED",
                        "attribute_rules.generated.json is not a JSON object", null);
                }
                return MakeCockpitResult(requestId, true, null, null, parsed);
            }
            catch (Exception ex)
            {
                WriteTrace("Attribute rules load failed: " + ex.Message);
                return MakeCockpitResult(requestId, false, "ERR_RULES_LOAD_FAILED",
                    ex.Message, null);
            }
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
                        ReadAssemblyAllComponents(activeDoc, rows, ref totalCount, true);
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

        private void ReadModelProperties(IModelDoc2 model, string targetName, string localCompName, string filePath, string docType, int quantity, List<PropertyReadRow> rows, string configurationName = null)
        {
            string config = string.IsNullOrWhiteSpace(configurationName) ? "(默认)" : configurationName;
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (string.Equals(row.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.ConfigurationName ?? "(默认)", config, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.LocalComponentName ?? "", localCompName ?? "", StringComparison.OrdinalIgnoreCase))
                    existing.Add(row.PropertyName ?? "");
            }

            ReadModelPropertiesFromManager(model, targetName, localCompName, filePath, docType, quantity, rows, config, "", existing);
            if (!string.Equals(config, "(默认)", StringComparison.OrdinalIgnoreCase))
                ReadModelPropertiesFromManager(model, targetName, localCompName, filePath, docType, quantity, rows, config, config, existing);
            ReadConfiguredPropertyNamesFromManager(model, targetName, localCompName, filePath, docType, quantity, rows, config, "", existing);
            if (!string.Equals(config, "(默认)", StringComparison.OrdinalIgnoreCase))
                ReadConfiguredPropertyNamesFromManager(model, targetName, localCompName, filePath, docType, quantity, rows, config, config, existing);
        }

        private void ReadConfiguredPropertyNamesFromManager(IModelDoc2 model, string targetName, string localCompName, string filePath, string docType, int quantity, List<PropertyReadRow> rows, string rowConfigName, string managerConfig, HashSet<string> existing)
        {
            try
            {
                CustomPropertyManager mgr = model.Extension.get_CustomPropertyManager(managerConfig ?? "");
                if (mgr == null) return;
                var configured = _config?.ReadPropertyNames ?? new List<string>();
                foreach (var canonical in configured)
                {
                    if (string.IsNullOrWhiteSpace(canonical)) continue;
                    var aliases = new List<string> { canonical };
                    string[] mapped;
                    if (PropertyNameAliases.TryGetValue(canonical, out mapped))
                        aliases.AddRange(mapped);
                    foreach (var alias in aliases)
                    {
                        if (string.IsNullOrWhiteSpace(alias) || existing.Contains(alias)) continue;
                        string rawVal = "", resolvedVal = "";
                        bool wasResolved = false, linkToProperty = false;
                        try { mgr.Get6(alias, false, out rawVal, out resolvedVal, out wasResolved, out linkToProperty); } catch { }
                        string display = !string.IsNullOrWhiteSpace(resolvedVal) ? resolvedVal : (rawVal ?? "");
                        if (string.IsNullOrWhiteSpace(display)) continue;
                        existing.Add(alias);
                        rows.Add(new PropertyReadRow
                        {
                            TargetName = targetName, LocalComponentName = localCompName,
                            FilePath = filePath, ConfigurationName = rowConfigName ?? "(默认)",
                            PropertyName = alias, RawValue = rawVal ?? "",
                            ResolvedValue = resolvedVal ?? rawVal ?? "", Source = docType, Quantity = quantity
                        });
                    }
                }
            }
            catch (Exception ex) { WriteTrace("ReadConfiguredPropertyNamesFromManager failed: " + ex.Message); }
        }

        private void ReadModelPropertiesFromManager(IModelDoc2 model, string targetName, string localCompName, string filePath, string docType, int quantity, List<PropertyReadRow> rows, string rowConfigName, string managerConfig, HashSet<string> existing)
        {
            try
            {
                CustomPropertyManager mgr = model.Extension.get_CustomPropertyManager(managerConfig ?? "");
                if (mgr == null) return;
                object propNames = mgr.GetNames();
                if (propNames == null) return;
                string[] names = propNames as string[];
                if (names == null) return;
                foreach (string name in names)
                {
                    if (string.IsNullOrWhiteSpace(name) || existing.Contains(name)) continue;
                    string rawVal = "", resolvedVal = "";
                    bool wasResolved = false, linkToProperty = false;
                    try { mgr.Get6(name, false, out rawVal, out resolvedVal, out wasResolved, out linkToProperty); } catch { }
                    existing.Add(name);
                    rows.Add(new PropertyReadRow
                    {
                        TargetName = targetName, LocalComponentName = localCompName,
                        FilePath = filePath, ConfigurationName = rowConfigName ?? "(默认)",
                        PropertyName = name, RawValue = rawVal ?? "",
                        ResolvedValue = !string.IsNullOrWhiteSpace(resolvedVal) ? resolvedVal : (rawVal ?? ""),
                        Source = docType, Quantity = quantity
                    });
                }
            }
            catch (Exception ex) { WriteTrace("ReadModelPropertiesFromManager failed: " + ex.Message); }
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

        private void ReadAssemblyAllComponents(IModelDoc2 asmDoc, List<PropertyReadRow> rows, ref int totalCount, bool resolveLightweightForRead)
        {
            bool resolvedLightweight = false;
            try
            {
                if (resolveLightweightForRead)
                    resolvedLightweight = TryResolveAssemblyLightweightForRead(asmDoc);

                IAssemblyDoc asm = asmDoc as IAssemblyDoc;
                if (asm == null) return;
                object compObjs = asm.GetComponents(false);
                if (compObjs == null) return;
                object[] comps = compObjs as object[];
                if (comps == null) return;
                var groups = new Dictionary<string, List<IComponent2>>(StringComparer.OrdinalIgnoreCase);
                int suppressedCount = 0;
                int skippedNoModel = 0;
                foreach (object c in comps)
                {
                    IComponent2 comp = c as IComponent2;
                    if (comp == null) continue;
                    if (SafeIsSuppressed(comp)) suppressedCount++;

                    string compPath = SafeGetComponentFilePath(comp);
                    if (string.IsNullOrWhiteSpace(compPath) || compPath == "不可用")
                        continue;
                    if (!groups.ContainsKey(compPath)) groups[compPath] = new List<IComponent2>();
                    groups[compPath].Add(comp);
                }
                totalCount = comps.Length - suppressedCount;
                foreach (var kv in groups)
                {
                    IComponent2 firstComp = kv.Value[0];
                    IModelDoc2 model = TryGetComponentModelDoc(firstComp, kv.Key);
                    if (model == null)
                    {
                        skippedNoModel++;
                        continue;
                    }
                    string compName = firstComp.Name2 ?? model.GetTitle();
                    string docType = GetDocTypeName(model.GetType());
                    if (string.IsNullOrWhiteSpace(docType) || docType == "unknown")
                        docType = GuessDocTypeNameFromPath(kv.Key);
                    string config = "(默认)";
                    try
                    {
                        string refCfg = firstComp.ReferencedConfiguration;
                        if (!string.IsNullOrWhiteSpace(refCfg)) config = refCfg;
                    }
                    catch { }
                    ReadModelProperties(model, compName, compName, kv.Key, docType, kv.Value.Count, rows, config);
                }
                if (suppressedCount > 0) WriteTrace("ReadAssemblyAllComponents: 含 " + suppressedCount + " 个压缩组件。");
                if (skippedNoModel > 0 && !resolveLightweightForRead)
                    WriteTrace("ReadAssemblyAllComponents: " + skippedNoModel + " 个组件属性未加载（轻化/未解析），路径仍可用；点「读取属性」可解析轻化件。");
                else if (skippedNoModel > 0)
                    WriteTrace("ReadAssemblyAllComponents: " + skippedNoModel + " 个组件仍无法读取属性。");
            }
            catch (Exception ex) { WriteTrace("ReadAssemblyAllComponents failed: " + ex); }
            finally
            {
                if (resolvedLightweight)
                    TryRestoreAssemblyLightweight(asmDoc);
            }
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
            string path = SafeGetComponentFilePath(comp);
            if (!string.IsNullOrWhiteSpace(path) && path != "不可用") return path;
            return comp?.Name2 ?? "";
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

        private void InitActionRouter()
        {
            try
            {
                _localExecutor = new LocalToolbeltExecutor(_swApp, _config);
                _actionRouter = new ActionRouter(_swApp, _config, _rules, BuildCockpitContext, (taskType, displayName) => ExecuteLocalTask(taskType, displayName));
                WriteTrace("InitActionRouter initialized.");
            }
            catch (Exception ex)
            {
                WriteTrace("InitActionRouter error: " + ex);
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

    /// <summary>
    /// 组件文件路径：轻化/压缩时 GetModelDoc2 可能为 null，但 IComponent2.GetPathName 仍返回 as-saved 路径。
    /// </summary>
    private static string SafeGetComponentFilePath(IComponent2 comp)
    {
        if (comp == null) return "不可用";
        try
        {
            var model = comp.GetModelDoc2() as IModelDoc2;
            if (model != null)
            {
                string p = model.GetPathName();
                if (!string.IsNullOrWhiteSpace(p)) return p;
            }
        }
        catch { }

        try
        {
            string p = comp.GetPathName();
            if (!string.IsNullOrWhiteSpace(p)) return p;
        }
        catch { }

        return "不可用";
    }

    private static string GuessDocTypeNameFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "part";
        string ext = Path.GetExtension(path);
        if (string.Equals(ext, ".SLDASM", StringComparison.OrdinalIgnoreCase)) return "assembly";
        if (string.Equals(ext, ".SLDDRW", StringComparison.OrdinalIgnoreCase)) return "drawing";
        return "part";
    }

    private static int GuessSwDocTypeFromPath(string path)
    {
        string ext = Path.GetExtension(path ?? "");
        if (string.Equals(ext, ".SLDASM", StringComparison.OrdinalIgnoreCase)) return (int)swDocumentTypes_e.swDocASSEMBLY;
        if (string.Equals(ext, ".SLDDRW", StringComparison.OrdinalIgnoreCase)) return (int)swDocumentTypes_e.swDocDRAWING;
        return (int)swDocumentTypes_e.swDocPART;
    }

    /// <summary>
    /// 仅使用已加载/已打开的模型，禁止 OpenDoc6（避免打开 Cockpit 时逐个弹出零件窗口）。
    /// </summary>
    private IModelDoc2 TryGetComponentModelDoc(IComponent2 comp, string compPath)
    {
        if (comp == null) return null;
        try
        {
            var loaded = comp.GetModelDoc2() as IModelDoc2;
            if (loaded != null) return loaded;
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(compPath) && compPath != "不可用")
            return FindOpenModelByPath(compPath);
        return null;
    }

    /// <summary>
    /// 仅在用户点击「读取属性」时：装配体内静默 Resolve 轻化件（不 OpenDoc6 逐个开文件）。
    /// </summary>
    private bool TryResolveAssemblyLightweightForRead(IModelDoc2 asmDoc)
    {
        _lightweightComponentsToRestore = null;
        try
        {
            IAssemblyDoc asm = asmDoc as IAssemblyDoc;
            if (asm == null) return false;

            var lightweightComps = new List<IComponent2>();
            object compObjs = asm.GetComponents(false);
            object[] comps = compObjs as object[];
            if (comps != null)
            {
                foreach (object c in comps)
                {
                    IComponent2 comp = c as IComponent2;
                    if (comp != null && SafeIsLightweight(comp))
                        lightweightComps.Add(comp);
                }
            }
            if (lightweightComps.Count == 0) return false;

            _lightweightComponentsToRestore = lightweightComps;
            try { _swApp?.ActivateDoc3(asmDoc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0); } catch { }

            int status = asm.ResolveAllLightWeightComponents(false);
            WriteTrace("ResolveAllLightWeightComponents(false) status=" + status + ", count=" + lightweightComps.Count);
            return true;
        }
        catch (Exception ex)
        {
            WriteTrace("TryResolveAssemblyLightweightForRead failed: " + ex.Message);
            _lightweightComponentsToRestore = null;
            return false;
        }
    }

    private void TryRestoreAssemblyLightweight(IModelDoc2 asmDoc)
    {
        if (_lightweightComponentsToRestore == null || _lightweightComponentsToRestore.Count == 0)
        {
            _lightweightComponentsToRestore = null;
            return;
        }
        try
        {
            foreach (IComponent2 comp in _lightweightComponentsToRestore)
            {
                try
                {
                    comp.SetSuppression2((int)swComponentSuppressionState_e.swComponentLightweight);
                }
                catch { }
            }
            WriteTrace("Restored " + _lightweightComponentsToRestore.Count + " components to lightweight.");
        }
        catch (Exception ex)
        {
            WriteTrace("TryRestoreAssemblyLightweight failed: " + ex.Message);
        }
        finally
        {
            _lightweightComponentsToRestore = null;
        }
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

    // CKP-004-20: 新增筛选辅助方法（COM 属性/方法兼容处理）
    private static bool SafeIsHidden(IComponent2 comp)
    {
        try
        {
            if (comp == null) return false;
            var h = comp.IsHidden(false);
            return h != null && (bool)h;
        }
        catch { return false; }
    }

    private static bool SafeIsEnvelope(IComponent2 comp)
    {
        try
        {
            if (comp == null) return false;
            dynamic dc = comp;
            try { return dc.IsEnvelope; } catch { }
            try { return dc.IsEnvelope(); } catch { }
            return false;
        }
        catch { return false; }
    }

    private static bool SafeIsVirtual(IComponent2 comp)
    {
        try
        {
            if (comp == null) return false;
            dynamic dc = comp;
            try { return dc.IsVirtual; } catch { }
            try { return dc.IsVirtual(); } catch { }
            return false;
        }
        catch { return false; }
    }

    private static bool SafeIsReadOnly(IComponent2 comp, string filePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                var fi = new System.IO.FileInfo(filePath);
                // PDM 管理下文件默认只读（退出 PDM 或本地文件视为非只读）
                return fi.Exists;
            }
            return false;
        }
        catch { return false; }
    }

    private static string SafeCompFileKey(IComponent2 comp)
    {
        string path = SafeGetComponentFilePath(comp);
        if (!string.IsNullOrEmpty(path) && path != "不可用") return path;
        return comp?.Name2 ?? Guid.NewGuid().ToString();
    }

    private static string SafeCompInstanceKey(IComponent2 comp)
    {
        try
        {
            string name = comp?.Name2;
            if (!string.IsNullOrEmpty(name)) return name;
        }

        catch { }
        return SafeCompFileKey(comp);
    }

    private void ShowOnlineSelectionTaskPane()
    {
        try
        {
            string url = GetOnlineSelectionUrl();
            if (_taskpaneView == null)
                AddCustomTaskPanes();

            if (_taskpaneView == null)
            {
                SafeMessage("SolidWorks 未能创建右侧在线选型任务窗格。\n\n请确认右侧任务窗格没有被隐藏，然后重新加载 MechPilot。", MessageBoxIcon.Warning);
                return;
            }

            _taskpaneView?.ShowView();
            if (_onlineSelectionPane != null)
                _onlineSelectionPane.Initialize(_config);
            WriteTrace("ShowOnlineSelectionTaskPane: " + url);
        }
        catch (Exception ex)
        {
            WriteTrace("ShowOnlineSelectionTaskPane exception: " + ex);
            SafeMessage("在线选型任务窗格打开失败。\n\n" + ex.Message, MessageBoxIcon.Warning);
        }
    }

    private string GetOnlineSelectionUrl()
    {
        return NormalizeOnlineSelectionUrl(_config?.OnlineSelectionUrl);
    }

    internal static string NormalizeOnlineSelectionUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return DefaultOnlineSelectionUrl;

        string trimmed = url.Trim();
        if (IsShellOpenTarget(trimmed))
            return NormalizeShellOpenTarget(trimmed);

        if (!trimmed.Contains("://"))
            trimmed = "https://" + trimmed;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return uri.AbsoluteUri;
        }

        return DefaultOnlineSelectionUrl;
    }

    internal static bool IsShellOpenTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;

        string trimmed = target.Trim();
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            return true;
        if (Regex.IsMatch(trimmed, @"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant))
            return true;
        if (trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!trimmed.Contains("://") && trimmed.Contains("\\"))
            return true;

        return false;
    }

    internal static string NormalizeShellOpenTarget(string target)
    {
        string trimmed = (target ?? "").Trim();
        if (trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            return trimmed;
        if (Regex.IsMatch(trimmed, @"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant))
            return trimmed;
        if (!trimmed.Contains("://") && trimmed.Contains("\\"))
            return @"\\" + trimmed.TrimStart('\\');
        return trimmed;
    }

    private static string GetTaskPaneIconPath()
    {
        string dir = GetAddinDirectory();
        string[] candidates =
        {
            Path.Combine(dir, "assets", "icons", "mechpilot-online-selection-32.bmp"),
            Path.Combine(dir, "assets", "icons", "mechpilot-online-selection-20.bmp"),
            Path.Combine(dir, "assets", "icons", "mechpilot-ribbon-main-32.bmp"),
            Path.Combine(dir, "assets", "icons", "mechpilot-ribbon-main-20.bmp"),
            Path.Combine(dir, "assets", "icons", "mechpilot-blueprint-main-20.bmp"),
            Path.Combine(dir, "assets", "icons", "mechpilot-main-20.bmp")
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        WriteTrace("GetTaskPaneIconPath: no icon found, CreateTaskpaneView3 will use null icon.");
        return null;
    }

    private IEnumerable<CustomTaskPaneEntry> GetCustomTaskPaneEntries()
    {
        yield return new CustomTaskPaneEntry(1, _config?.CustomButton1Title, _config?.CustomButton1Url, "PDM");
        yield return new CustomTaskPaneEntry(2, _config?.CustomButton2Title, _config?.CustomButton2Url, "MDM");
        yield return new CustomTaskPaneEntry(3, _config?.CustomButton3Title, _config?.CustomButton3Url, "ERP");
        yield return new CustomTaskPaneEntry(4, _config?.CustomButton4Title, _config?.CustomButton4Url, "BBS");
        yield return new CustomTaskPaneEntry(5, _config?.CustomButton5Title, _config?.CustomButton5Url, "DAT");
    }

    private static string GetCustomTaskPaneIconPath(string iconKey)
    {
        string dir = GetAddinDirectory();
        string[] candidates =
        {
            Path.Combine(dir, "assets", "icons", "mechpilot-custom-" + iconKey + "-32.bmp"),
            Path.Combine(dir, "assets", "icons", "mechpilot-custom-" + iconKey + "-20.bmp"),
            Path.Combine(dir, "assets", "icons", "mechpilot-online-selection-32.bmp"),
            Path.Combine(dir, "assets", "icons", "mechpilot-ribbon-main-32.bmp"),
            Path.Combine(dir, "assets", "icons", "mechpilot-main-32.bmp")
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        WriteTrace("GetCustomTaskPaneIconPath: no icon found for " + iconKey);
        return null;
    }

    private class CustomTaskPaneEntry
    {
        public CustomTaskPaneEntry(int index, string title, string url, string iconKey)
        {
            Index = index;
            Title = string.IsNullOrWhiteSpace(title) ? iconKey : title;
            Url = string.IsNullOrWhiteSpace(url) ? "" : NormalizeOnlineSelectionUrl(url);
            IconKey = iconKey;
        }

        public int Index { get; private set; }
        public string Title { get; private set; }
        public string Url { get; private set; }
        public string IconKey { get; private set; }
    }

    internal static void OpenOnlineSelectionInBrowser(string url)
    {
        url = IsShellOpenTarget(url) ? NormalizeShellOpenTarget(url) : NormalizeOnlineSelectionUrl(url);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "无法打开在线选型页面。\n\n" + url + "\n\n" + ex.Message,
                "MechPilot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
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
                Capabilities = new List<string> { "read", "write", "check", "review", "cockpit" },
                RuntimeConfig = BuildCockpitRuntimeConfig()
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
                string title = activeDoc.GetTitle() ?? "";
                string config = "(默认)";
                try
                {
                    IConfigurationManager cfgMgr = activeDoc.ConfigurationManager;
                    if (cfgMgr != null)
                    {
                        IConfiguration activeCfg = cfgMgr.ActiveConfiguration;
                        if (activeCfg != null && !string.IsNullOrWhiteSpace(activeCfg.Name))
                            config = activeCfg.Name;
                    }
                }
                catch { }
                ReadModelProperties(activeDoc, title, title, fp, "part", 1, rows, config);
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
                                    if (SafeIsSuppressed(comp)) { suppressedCount++; }
                                    if (SafeIsLightweight(comp)) { lightweightCount++; }
                                }
                            }
                        }
                    }
                    bool resolveLw = _resolveLightweightForPropertyRead;
                    ReadAssemblyAllComponents(activeDoc, rows, ref totalCount, resolveLw);
                    _resolveLightweightForPropertyRead = false;
                }
                catch (Exception ex)
                {
                    warnings.Add(new CockpitWarning { Level = "error", Target = "assembly", Message = "装配体属性读取失败: " + ex.Message });
                }
            }

            // ── 属性表 ──
            context.PropertyTable = BuildCockpitPropertyTable(rows, context.ActiveDocument.Title, warnings);
            EnrichPdmVaultFlags(context.PropertyTable);
            EnrichPdmWorkflowStateProperties(context.PropertyTable, warnings);
            int propsRowCount = 0;
            if (context.PropertyTable?.Rows != null)
            {
                foreach (var pr in context.PropertyTable.Rows)
                    if (pr.ResolvedProperties != null && pr.ResolvedProperties.Count > 0) propsRowCount++;
            }
            WriteTrace(string.Format(
                "BuildCockpitContext properties: rawRows={0} pivotRows={1} rowsWithProps={2}",
                rows.Count, context.PropertyTable?.Rows?.Count ?? 0, propsRowCount));

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
                CountCockpitTreeNodes(context.AssemblyTree), warnings.Count, sw.Elapsed.TotalMilliseconds));

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

                    string compPath = SafeGetComponentFilePath(comp);
                    string compName = comp.Name2 ?? "";
                    string displayName = CleanComponentDisplayName(compName);
                    string docType = "";
                    try
                    {
                        IModelDoc2 compModel = comp.GetModelDoc2() as IModelDoc2;
                        if (compModel != null) docType = GetDocTypeName(compModel.GetType());
                    }
                    catch { }
                    if (string.IsNullOrWhiteSpace(docType) && compPath != "不可用")
                        docType = GuessDocTypeNameFromPath(compPath);
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

                if (SafeIsSuppressed(comp)) { skippedSuppressed++; }
                if (SafeIsLightweight(comp)) { skippedLightweight++; }

                IComponent2 parent = null;
                try { parent = (IComponent2)comp.GetParent(); } catch { }
                string parentKey = parent != null ? SafeCompInstanceKey(parent) : "__ROOT__";
                if (!childrenMap.ContainsKey(parentKey))
                    childrenMap[parentKey] = new List<IComponent2>();
                childrenMap[parentKey].Add(comp);
            }

            if (skippedSuppressed > 0)
                warnings.Add(new CockpitWarning { Level = "info", Target = "assembly_tree", Message = string.Format("含 {0} 个压缩组件", skippedSuppressed) });
            if (skippedLightweight > 0)
                warnings.Add(new CockpitWarning { Level = "info", Target = "assembly_tree", Message = string.Format("含 {0} 个轻化组件（已解析路径）", skippedLightweight) });

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

                string compPath = SafeGetComponentFilePath(comp);
                string docType = "";
                bool isAssemblyComp = false;
                try
                {
                    IModelDoc2 cm = comp.GetModelDoc2() as IModelDoc2;
                    if (cm != null)
                    {
                        int dt = cm.GetType();
                        docType = GetDocTypeName(dt);
                        isAssemblyComp = (swDocumentTypes_e)dt == swDocumentTypes_e.swDocASSEMBLY;
                    }
                    else if (compPath != "不可用")
                    {
                        docType = GuessDocTypeNameFromPath(compPath);
                        isAssemblyComp = docType == "assembly";
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
                // CKP-004-20: 新增筛选字段
                bool isHidden = SafeIsHidden(comp);
                bool isEnvelope = SafeIsEnvelope(comp);
                bool isVirtual = SafeIsVirtual(comp);
                bool isReadOnly = SafeIsReadOnly(comp, compPath);
                bool isInPdmVault = PdmCom.IsFileInVault(compPath);

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
                    IsHidden = isHidden,
                    IsEnvelope = isEnvelope,
                    IsVirtual = isVirtual,
                    IsReadOnly = isReadOnly,
                    IsInPdmVault = isInPdmVault,
                    Children = new List<CockpitTreeNode>()
                };

                // Recurse into children
                string myKey = SafeCompInstanceKey(comp);
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

    private static int CountCockpitTreeNodes(List<CockpitTreeNode> nodes)
    {
        if (nodes == null) return 0;
        int count = 0;
        foreach (var node in nodes)
        {
            count++;
            count += CountCockpitTreeNodes(node.Children);
        }
        return count;
    }

    /// <summary>
    /// 构建属性表。支持 config.json 中的 read_property_names 过滤。
    /// </summary>
    private CockpitPropertyTable BuildCockpitPropertyTable(List<PropertyReadRow> rows, string targetLabel, List<CockpitWarning> warnings)
    {
        try
        {
            // Determine which property names to include (config-driven + defaults)
            var configuredProps = new List<string>(_config?.ReadPropertyNames ?? new List<string>());
            foreach (var name in DefaultReadPropertyNames)
            {
                if (!configuredProps.Contains(name, StringComparer.OrdinalIgnoreCase))
                    configuredProps.Add(name);
            }
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

                // Property filtering + alias normalization (W物料名称 → 物料名称)
                string propName = row.PropertyName ?? "";
                if (string.Equals(propName, PdmWorkflowStatePropertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                string canonicalName = ResolveConfiguredPropertyName(propName, configuredProps);
                bool include = showAll;
                if (!include)
                {
                    if (configuredProps.Count == 0)
                        include = true;
                    else
                        include = canonicalName != null || intrinsicSet.Contains(propName);
                }
                if (!include) continue;

                string storeKey = canonicalName ?? propName;
                string resolvedDisplay = !string.IsNullOrWhiteSpace(row.ResolvedValue) ? row.ResolvedValue : (row.RawValue ?? "");
                if (!crow.Properties.ContainsKey(storeKey))
                {
                    crow.Properties[storeKey] = new CockpitPropertyValue
                    {
                        RawValue = row.RawValue ?? "",
                        ResolvedValue = resolvedDisplay,
                        Type = 30  // swCustomInfoText
                    };
                }
                else if (!string.IsNullOrWhiteSpace(resolvedDisplay))
                {
                    var existing = crow.Properties[storeKey];
                    if (string.IsNullOrWhiteSpace(existing.ResolvedValue) && string.IsNullOrWhiteSpace(existing.RawValue))
                    {
                        existing.RawValue = row.RawValue ?? "";
                        existing.ResolvedValue = resolvedDisplay;
                    }
                }
                if (!string.IsNullOrWhiteSpace(resolvedDisplay))
                {
                    if (crow.ResolvedProperties == null)
                        crow.ResolvedProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    crow.ResolvedProperties[storeKey] = resolvedDisplay;
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

    private const string PdmWorkflowStatePropertyName = "物料状态";

    private static readonly string[] DefaultReadPropertyNames =
    {
        "物料编码", "物料名称", "规格型号", "材质", "表面处理", "设计人", "物料状态"
    };

    private static readonly Dictionary<string, string[]> PropertyNameAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["物料编码"] = new[] { "物料编码", "W物料编码", "FileBM", "物料代码", "编码", "PartNumber", "零件号", "MaterialCode" },
            ["物料名称"] = new[] { "物料名称", "W物料名称", "名称", "Description", "PartName", "零件名称" },
            ["规格型号"] = new[] { "规格型号", "G规格型号", "规格", "型号", "Specification", "Model" },
            ["材质"] = new[] { "材质", "C材质", "材料", "Material", "C材料", "C_Material" },
            ["表面处理"] = new[] { "表面处理", "SurfaceTreatment", "表面處理", "Finish", "Coating" },
            ["设计人"] = new[] { "设计人", "设计", "Designer", "设计人员", "设计者", "DesignedBy", "Author", "创建者" },
            ["物料状态"] = new[] { "物料状态" }
        };

    private static string ResolveConfiguredPropertyName(string swPropName, List<string> configuredProps)
    {
        if (string.IsNullOrWhiteSpace(swPropName) || configuredProps == null || configuredProps.Count == 0)
            return null;
        foreach (var canonical in configuredProps)
        {
            if (string.Equals(canonical, swPropName, StringComparison.OrdinalIgnoreCase))
                return canonical;
        }
        foreach (var canonical in configuredProps)
        {
            string[] aliases;
            if (!PropertyNameAliases.TryGetValue(canonical, out aliases)) continue;
            foreach (var alias in aliases)
            {
                if (string.Equals(alias, swPropName, StringComparison.OrdinalIgnoreCase))
                    return canonical;
            }
        }
        return null;
    }

    /// <summary>
    /// 标记属性行是否由 PDM Vault 管理（供设计树/扁平视图图标着色）。
    /// </summary>
    private void EnrichPdmVaultFlags(CockpitPropertyTable table)
    {
        if (table?.Rows == null || table.Rows.Count == 0) return;

        foreach (var row in table.Rows)
        {
            string fp = row.FilePath ?? "";
            row.IsInPdmVault = !string.IsNullOrWhiteSpace(fp) && fp != "不可用" && PdmCom.IsFileInVault(fp);
        }
    }

    /// <summary>
    /// 当 read_property_names 包含「物料状态」时，从 PDM 工作流注入当前状态（非 SW 自定义属性）。
    /// </summary>
    private void EnrichPdmWorkflowStateProperties(CockpitPropertyTable table, List<CockpitWarning> warnings)
    {
        try
        {
            var configuredProps = new List<string>(_config?.ReadPropertyNames ?? new List<string>());
            foreach (var name in DefaultReadPropertyNames)
            {
                if (!configuredProps.Contains(name, StringComparer.OrdinalIgnoreCase))
                    configuredProps.Add(name);
            }
            if (!configuredProps.Any(p => string.Equals(p, PdmWorkflowStatePropertyName, StringComparison.OrdinalIgnoreCase)))
                return;
            if (table?.Rows == null || table.Rows.Count == 0) return;

            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in table.Rows)
            {
                string fp = row.FilePath ?? "";
                if (string.IsNullOrWhiteSpace(fp) || fp == "不可用") continue;

                // 与设计树 PDM 图标一致：仅 Vault 内文件查物料状态，本地文件跳过
                string stateName = "";
                if (row.IsInPdmVault)
                {
                    if (!cache.TryGetValue(fp, out stateName))
                    {
                        stateName = PdmCom.GetWorkflowStateName(fp) ?? "";
                        cache[fp] = stateName;
                    }
                }

                row.Properties[PdmWorkflowStatePropertyName] = new CockpitPropertyValue
                {
                    RawValue = stateName ?? "",
                    ResolvedValue = stateName ?? "",
                    Type = 30
                };
                if (row.ResolvedProperties == null)
                    row.ResolvedProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                row.ResolvedProperties[PdmWorkflowStatePropertyName] = stateName ?? "";
            }

            if (table.DynamicColumns == null)
                table.DynamicColumns = new List<string>();
            if (!table.DynamicColumns.Any(c => string.Equals(c, PdmWorkflowStatePropertyName, StringComparison.OrdinalIgnoreCase)))
                table.DynamicColumns.Add(PdmWorkflowStatePropertyName);
            table.DynamicColumns = table.DynamicColumns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            warnings.Add(new CockpitWarning
            {
                Level = "warn",
                Target = "pdm_workflow_state",
                Message = "PDM 物料状态注入失败: " + ex.Message
            });
            WriteTrace("EnrichPdmWorkflowStateProperties failed: " + ex.ToString());
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

    private Dictionary<string, object> BuildCockpitRuntimeConfig()
    {
        var hindsight = _config?.Hindsight ?? new HindsightConfig();
        var agent = _config?.AgentServer ?? new AgentServerConfig();
        return new Dictionary<string, object>
        {
            ["execution_mode"] = _config?.ExecutionMode ?? "local",
            ["cockpit_url_mode"] = _config?.CockpitUrlMode ?? "local",
            ["agent_server"] = new Dictionary<string, object>
            {
                ["provider"] = agent.Provider ?? "hermes",
                ["base_url"] = agent.BaseUrl ?? "",
                ["auth_mode"] = string.IsNullOrEmpty(agent.AuthMode) ? "none" : agent.AuthMode,
                ["context_mode_default"] = agent.ContextModeDefault ?? "summary",
                ["timeout_seconds"] = agent.TimeoutSeconds,
                ["poll_interval_seconds"] = agent.PollIntervalSeconds,
                ["job_submit_endpoint"] = agent.JobSubmitEndpoint ?? "/api/jobs",
                ["job_status_endpoint_template"] = agent.JobStatusEndpointTemplate ?? "/api/jobs/{job_id}",
                ["job_poll_interval_seconds"] = agent.JobPollIntervalSeconds
            },
            ["hindsight"] = new Dictionary<string, object>
            {
                ["enabled"] = hindsight.Enabled,
                ["base_url"] = hindsight.BaseUrl ?? "",
                ["bank"] = hindsight.Bank ?? "",
                ["source_db_path"] = hindsight.SourceDbPath ?? "",
                ["top_k"] = hindsight.TopK,
                ["score_threshold"] = hindsight.ScoreThreshold,
                ["timeout_seconds"] = hindsight.TimeoutSeconds,
                ["explain_with_hermes"] = hindsight.ExplainWithHermes
            },
            ["read_property_names"] = _config?.ReadPropertyNames ?? new List<string>()
        };
    }

    #endregion



    
    }

    [ComVisible(true)]
    [Guid("4A9EB945-6F2A-4C3E-9B39-3B6B8D3E2C4F")]
    [ProgId(SwAgentAddin.OnlineSelectionTaskPaneProgId)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class OnlineSelectionTaskPane : UserControl
    {
        private readonly Panel _header;
        private readonly Label _status;
        private readonly Button _reloadButton;
        private readonly Button _externalButton;
        private readonly ToolTip _toolTip;
        private WebView2 _webView;
        private string _url = SwAgentAddin.DefaultOnlineSelectionUrl;
        private bool _initializing;
        private bool _ready;

        public OnlineSelectionTaskPane()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.White;

            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(10, 7, 8, 7),
                BackColor = Color.FromArgb(247, 248, 250)
            };

            var title = new Label
            {
                Dock = DockStyle.Fill,
                Text = "MechPilot 在线选型",
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };

            _externalButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 44,
                Image = CreateHeaderIcon("external"),
                ImageAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Cursor = Cursors.Hand
            };
            StyleHeaderButton(_externalButton);
            _externalButton.Click += (s, e) => SwAgentAddin.OpenOnlineSelectionInBrowser(_url);

            _reloadButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 44,
                Image = CreateHeaderIcon("reload"),
                ImageAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Cursor = Cursors.Hand
            };
            StyleHeaderButton(_reloadButton);
            _reloadButton.Click += (s, e) => NavigateOnlineSelection();

            _toolTip = new ToolTip
            {
                AutomaticDelay = 300,
                ReshowDelay = 100,
                ShowAlways = true
            };
            _toolTip.SetToolTip(_externalButton, "浏览器打开");
            _toolTip.SetToolTip(_reloadButton, "刷新");

            _header.Controls.Add(title);
            _header.Controls.Add(_externalButton);
            _header.Controls.Add(_reloadButton);

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                Padding = new Padding(10, 0, 10, 0),
                Text = "正在准备在线选型页面...",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(78, 89, 105),
                BackColor = Color.FromArgb(247, 248, 250)
            };

            Controls.Add(_status);
            Controls.Add(_header);
            Load += (s, e) => NavigateOnlineSelection();
        }

        private static void StyleHeaderButton(Button button)
        {
            button.Margin = new Padding(4, 0, 0, 0);
            button.TabStop = false;
            button.FlatAppearance.BorderColor = Color.FromArgb(196, 204, 214);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 244, 255);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(209, 233, 255);
        }

        private static Bitmap CreateHeaderIcon(string kind)
        {
            var bmp = new Bitmap(18, 18);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var pen = new Pen(Color.FromArgb(31, 41, 55), 1.8F))
            using (var accent = new Pen(Color.FromArgb(13, 148, 136), 2.2F))
            using (var accentBrush = new SolidBrush(Color.FromArgb(13, 148, 136)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                if (kind == "reload")
                {
                    g.DrawArc(accent, 3, 3, 12, 12, 35, 280);
                    g.FillPolygon(accentBrush, new[]
                    {
                        new PointF(13.4F, 2.2F),
                        new PointF(16.2F, 5.7F),
                        new PointF(11.8F, 6.2F)
                    });
                }
                else
                {
                    g.DrawRectangle(pen, 3.2F, 6.2F, 8.6F, 8.6F);
                    g.DrawLine(accent, 8.2F, 9.8F, 14.6F, 3.4F);
                    g.DrawLine(accent, 10.6F, 3.3F, 14.7F, 3.3F);
                    g.DrawLine(accent, 14.7F, 3.3F, 14.7F, 7.4F);
                }
            }
            return bmp;
        }

        public void Initialize(string url)
        {
            _url = SwAgentAddin.IsShellOpenTarget(url)
                ? SwAgentAddin.NormalizeShellOpenTarget(url)
                : SwAgentAddin.NormalizeOnlineSelectionUrl(url);
            NavigateOnlineSelection();
        }

        public void Initialize(string title, string url)
        {
            Initialize(url);
        }

        public void Initialize(AddinConfig config)
        {
            Initialize(config?.OnlineSelectionUrl);
        }

        public async void NavigateOnlineSelection()
        {
            try
            {
                if (_initializing) return;

                if (SwAgentAddin.IsShellOpenTarget(_url))
                {
                    _status.Text = "Click the top-right button to open this shared folder.";
                    return;
                }

                if (_webView == null)
                {
                    _initializing = true;
                    _status.Text = "正在初始化 WebView2...";
                    _webView = new WebView2
                    {
                        Dock = DockStyle.Fill,
                        CreationProperties = new CoreWebView2CreationProperties
                        {
                            UserDataFolder = Path.Combine(
                                SwAgentAddin.GetAddinDirectory(),
                                "online-selection-cache")
                        }
                    };
                    Controls.Add(_webView);
                    Controls.SetChildIndex(_webView, 1);
                    await _webView.EnsureCoreWebView2Async();
                    _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                    _ready = true;
                    _initializing = false;
                }

                if (_ready && _webView.CoreWebView2 != null)
                {
                    _status.Text = "正在打开在线选型...";
                    _webView.CoreWebView2.Navigate(_url);
                }
            }
            catch (Exception ex)
            {
                _initializing = false;
                _status.Text = "WebView2 打开失败，可使用浏览器打开。";
                SwAgentAddin.WriteTrace("OnlineSelectionTaskPane NavigateOnlineSelection exception: " + ex);
                MessageBox.Show(
                    "在线选型页面暂时无法在任务窗格中打开。\n\n可以点击“浏览器打开”继续使用。\n\n" + ex.Message,
                    "MechPilot 在线选型",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _status.Text = e.IsSuccess
                ? "在线选型页面已加载。"
                : "页面加载失败，可刷新或使用浏览器打开。";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (_webView?.CoreWebView2 != null)
                        _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                    _toolTip?.Dispose();
                    _webView?.Dispose();
                }
                catch { }
            }
            base.Dispose(disposing);
        }
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
        private readonly Func<string, Task<string>> _handleCommandAsync;
        private WebView2 _webView;
        private Rectangle _normalBounds = Rectangle.Empty;
        private Rectangle _customMaxBounds = Rectangle.Empty;
        private bool _isCustomMaximized = false;
        private string _pendingPage;
        private bool _pageLoaded;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public CockpitForm(AddinConfig config, Func<string> buildContext, Func<string, Task<string>> handleCommandAsync)
        {
            _config = config;
            _buildContext = buildContext;
            _handleCommandAsync = handleCommandAsync;

            Text = "MechPilot Agent驾驶舱";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 800);
            MinimumSize = new Size(800, 500);
            Padding = new Padding(6);
            BackColor = Color.FromArgb(217, 221, 228);
            _normalBounds = Bounds;
            // 初次展示使用自定义最大化，贴合 WorkingArea 不遮挡任务栏
            ApplyCustomMaximize();

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

        public void NavigatePage(string pageId)
        {
            if (string.IsNullOrWhiteSpace(pageId)) return;
            _pendingPage = pageId.Trim();
            TryNavigatePendingPage();
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
                // 自定义最大化时禁用边框 resize 热区（窗口已贴合屏幕边缘）
                if (!_isCustomMaximized)
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
            _pageLoaded = false;
            _webView.CoreWebView2.Navigate(url);

            // Wait for page load, then inject context
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        }

        private void OnNavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) return;
            _pageLoaded = true;

            try
            {
                // C# → JS: inject initial context (Base64 avoids ExecuteScript JSON/Unicode escaping issues)
                string contextJson = _buildContext?.Invoke() ?? "{}";
                InjectContextToWebView(contextJson);
                SwAgentAddin.WriteTrace("CockpitForm: context injected (" + contextJson.Length + " chars)");
                TryNavigatePendingPage();
            }
            catch (Exception ex)
            {
                SwAgentAddin.WriteTrace("CockpitForm: context injection failed: " + ex.Message);
            }
        }

        private void TryNavigatePendingPage()
        {
            if (string.IsNullOrWhiteSpace(_pendingPage)) return;
            if (_webView?.CoreWebView2 == null) return;
            if (!_pageLoaded) return;

            string page = _pendingPage;
            _pendingPage = null;
            string escaped = EscapeForJsInjection(page);
            string script =
                "if (window.MechPilot && window.MechPilot.navigate_page) { " +
                "window.MechPilot.navigate_page('" + escaped + "'); }";
            _webView.CoreWebView2.ExecuteScriptAsync(script).ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    _pendingPage = page;
                    SwAgentAddin.WriteTrace("CockpitForm: page navigation failed: " +
                        task.Exception.GetBaseException().Message);
                }
                else
                {
                    SwAgentAddin.WriteTrace("CockpitForm: navigated page " + page);
                }
            });
        }

        /// <summary>
        /// Push refreshed context to the WebView2 frontend (called by ActiveDocChangeNotify)
        /// </summary>
        public void PushContextToWebView(string contextJson)
        {
            if (_webView?.CoreWebView2 == null || !_pageLoaded) return;
            if (string.IsNullOrEmpty(contextJson)) return;

            try
            {
                InjectContextToWebView(contextJson);
                SwAgentAddin.WriteTrace("CockpitForm: pushed refreshed context (" + contextJson.Length + " chars)");
            }
            catch (Exception ex)
            {
                SwAgentAddin.WriteTrace("CockpitForm: push context failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Inject CockpitContext into WebView2 via Base64-wrapped JSON (same strategy as receiveResult).
        /// </summary>
        private void InjectContextToWebView(string contextJson)
        {
            if (_webView?.CoreWebView2 == null || string.IsNullOrEmpty(contextJson)) return;
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(contextJson));
            string script =
                "if (window.MechPilot && window.MechPilot.decodeBase64Utf8Json && window.MechPilot.receiveContext) { " +
                "try { window.MechPilot.receiveContext(window.MechPilot.decodeBase64Utf8Json('" + b64 + "')); } " +
                "catch(e) { console.error('[MechPilot] receiveContext failed', e); } }";
            _webView.CoreWebView2.ExecuteScriptAsync(script).ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                    SwAgentAddin.WriteTrace("CockpitForm: context injection script failed: " + task.Exception.GetBaseException().Message);
            });
        }

        private async void OnWebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
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
                if (message.Contains("window_pin_toggle"))
                {
                    // CKP-004-08: 窗口钉住/置顶 toggle
                    TopMost = !TopMost;
                    string pinResult = "{\"request_id\":\"" + ExtractRequestId(message) + "\",\"success\":true,\"data\":{\"pinned\":" + (TopMost ? "true" : "false") + ",\"topmost\":" + (TopMost ? "true" : "false") + "}}";
                    string pinScript = "if (window.MechPilot && window.MechPilot.receiveResult) { window.MechPilot.receiveResult(" + EscapeForJsInjection(pinResult) + "); }";
                    await _webView.CoreWebView2.ExecuteScriptAsync(pinScript).ConfigureAwait(true);
                    SwAgentAddin.WriteTrace("CockpitForm: window_pin_toggle -> TopMost=" + TopMost);
                    return;
                }

                // Dispatch other commands without blocking the WebView2 message pump
                if (_handleCommandAsync == null) return;
                string result = await _handleCommandAsync(message).ConfigureAwait(true);
                if (!string.IsNullOrEmpty(result))
                {
                    // Base64 avoids ExecuteScript JSON escaping issues on large Hermes payloads
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(result));
                    string script =
                        "if (window.MechPilot && window.MechPilot.decodeBase64Utf8Json && window.MechPilot.receiveResult) { " +
                        "try { window.MechPilot.receiveResult(window.MechPilot.decodeBase64Utf8Json('" + b64 + "')); } " +
                        "catch(e) { console.error('[MechPilot] receiveResult failed', e); } }";
                    await _webView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
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
            if (_isCustomMaximized)
            {
                // CKP-004-09: 还原为当前尺寸 2 倍，居中
                Rectangle workingArea = Screen.FromControl(this).WorkingArea;
                int newW = _normalBounds.Width > 0 ? _normalBounds.Width * 2 : 1280;
                int newH = _normalBounds.Height > 0 ? _normalBounds.Height * 2 : 800;
                // Clamp to 90% of working area
                int maxW = (int)(workingArea.Width * 0.9);
                int maxH = (int)(workingArea.Height * 0.9);
                if (newW > maxW) newW = maxW;
                if (newH > maxH) newH = maxH;
                // Center on working area
                int newX = workingArea.X + (workingArea.Width - newW) / 2;
                int newY = workingArea.Y + (workingArea.Height - newH) / 2;
                if (newX < workingArea.X) newX = workingArea.X;
                if (newY < workingArea.Y) newY = workingArea.Y;

                _normalBounds = new Rectangle(newX, newY, newW, newH);
                Bounds = _normalBounds;
                _isCustomMaximized = false;
                SwAgentAddin.WriteTrace("CockpitForm: restored to 2x centered " + newW + "x" + newH + " at " + newX + "," + newY);
                return;
            }

            // 保存当前窗口大小作为还原目标
            _normalBounds = Bounds;
            ApplyCustomMaximize();
        }

        /// <summary>
        /// 应用自定义最大化：将窗口贴合当前屏幕 WorkingArea（不遮挡任务栏）
        /// </summary>
        private void ApplyCustomMaximize()
        {
            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            _customMaxBounds = workingArea;
            Bounds = workingArea;
            WindowState = FormWindowState.Normal;  // 保持 Normal，靠 Bounds 撑满
            _isCustomMaximized = true;
            Debug.WriteLine("[CockpitForm] ApplyCustomMaximize workingArea=" + workingArea);
        }

        /// <summary>
        /// 公开方法：确保窗口处于最大化状态（供 SwCmd_Cockpit 复用时调用）
        /// </summary>
        public void EnsureCustomMaximized()
        {
            if (!_isCustomMaximized)
            {
                ApplyCustomMaximize();
            }
            else if (Bounds != _customMaxBounds)
            {
                // 窗口可能被移动，重新应用 WorkingArea
                ApplyCustomMaximize();
            }
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

        /// <summary>
        /// CKP-004-08: Extract request_id from JSON message for window-level command responses.
        /// </summary>
        private static string ExtractRequestId(string rawMessage)
        {
            try
            {
                // Simple regex extraction to avoid full parse for window commands
                var match = System.Text.RegularExpressions.Regex.Match(rawMessage ?? "", "\"request_id\"\\s*:\\s*\"([^\"]+)\"");
                return match.Success ? match.Groups[1].Value : "";
            }
            catch { return ""; }
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
            // CKP-004-12: 强制允许关闭，不做无条件 Cancel。SW 退出时必须能关。
            SwAgentAddin.WriteTrace("CockpitForm: OnFormClosing reason=" + e.CloseReason + " cancel=" + e.Cancel);
            try
            {
                // 清理 TopMost（关闭前复原，避免阻挡其他窗口）
                TopMost = false;

                if (_webView?.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                }
                _webView?.Dispose();
                _webView = null;
            }
            catch (Exception ex)
            {
                SwAgentAddin.WriteTrace("CockpitForm: OnFormClosing cleanup exception: " + ex.Message);
            }

            // 永远允许关闭，不 Cancel
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SwAgentAddin.WriteTrace("CockpitForm: OnFormClosed reason=" + e.CloseReason);
            base.OnFormClosed(e);
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
            "物料编码", "物料名称", "规格型号", "材质", "表面处理", "设计人", "物料状态"
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
        public string OnlineSelectionUrl { get; set; } = SwAgentAddin.DefaultOnlineSelectionUrl;
        public string CustomButton1Title { get; set; } = "PDM";
        public string CustomButton1Url { get; set; } = "";
        public string CustomButton2Title { get; set; } = "MDM";
        public string CustomButton2Url { get; set; } = "";
        public string CustomButton3Title { get; set; } = "ERP";
        public string CustomButton3Url { get; set; } = "";
        public string CustomButton4Title { get; set; } = "BBS";
        public string CustomButton4Url { get; set; } = "";
        public string CustomButton5Title { get; set; } = "DAT";
        public string CustomButton5Url { get; set; } = "";

        // Agent Server / Hermes (Agent S)
        public AgentServerConfig AgentServer { get; set; } = new AgentServerConfig();

        // Hindsight RAG
        public HindsightConfig Hindsight { get; set; } = new HindsightConfig();

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
            if (dict.ContainsKey("online_selection_url"))
                config.OnlineSelectionUrl = Convert.ToString(dict["online_selection_url"]);
            if (dict.ContainsKey("custom_button_1_title"))
                config.CustomButton1Title = Convert.ToString(dict["custom_button_1_title"]);
            if (dict.ContainsKey("custom_button_1_url"))
                config.CustomButton1Url = Convert.ToString(dict["custom_button_1_url"]);
            if (dict.ContainsKey("custom_button_2_title"))
                config.CustomButton2Title = Convert.ToString(dict["custom_button_2_title"]);
            if (dict.ContainsKey("custom_button_2_url"))
                config.CustomButton2Url = Convert.ToString(dict["custom_button_2_url"]);
            if (dict.ContainsKey("custom_button_3_title"))
                config.CustomButton3Title = Convert.ToString(dict["custom_button_3_title"]);
            if (dict.ContainsKey("custom_button_3_url"))
                config.CustomButton3Url = Convert.ToString(dict["custom_button_3_url"]);
            if (dict.ContainsKey("custom_button_4_title"))
                config.CustomButton4Title = Convert.ToString(dict["custom_button_4_title"]);
            if (dict.ContainsKey("custom_button_4_url"))
                config.CustomButton4Url = Convert.ToString(dict["custom_button_4_url"]);
            if (dict.ContainsKey("custom_button_5_title"))
                config.CustomButton5Title = Convert.ToString(dict["custom_button_5_title"]);
            if (dict.ContainsKey("custom_button_5_url"))
                config.CustomButton5Url = Convert.ToString(dict["custom_button_5_url"]);

            // Agent Server (Hermes)
            if (dict.ContainsKey("agent_server"))
            {
                var agentServerDict = dict["agent_server"] as Dictionary<string, object>;
                if (agentServerDict != null)
                    config.AgentServer = AgentServerConfig.FromJson(agentServerDict);
            }

            // Hindsight RAG
            if (dict.ContainsKey("hindsight"))
            {
                var hindsightDict = dict["hindsight"] as Dictionary<string, object>;
                if (hindsightDict != null)
                    config.Hindsight = HindsightConfig.FromJson(hindsightDict);
            }

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
            dict["online_selection_url"] = OnlineSelectionUrl;
            dict["custom_button_1_title"] = CustomButton1Title;
            dict["custom_button_1_url"] = CustomButton1Url;
            dict["custom_button_2_title"] = CustomButton2Title;
            dict["custom_button_2_url"] = CustomButton2Url;
            dict["custom_button_3_title"] = CustomButton3Title;
            dict["custom_button_3_url"] = CustomButton3Url;
            dict["custom_button_4_title"] = CustomButton4Title;
            dict["custom_button_4_url"] = CustomButton4Url;
            dict["custom_button_5_title"] = CustomButton5Title;
            dict["custom_button_5_url"] = CustomButton5Url;

            // Agent Server (Hermes)
            if (AgentServer != null) dict["agent_server"] = AgentServer.ToDict();

            // Hindsight RAG
            if (Hindsight != null) dict["hindsight"] = Hindsight.ToDict();

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
        public Dictionary<string, object> RuntimeConfig { get; set; } = new Dictionary<string, object>();
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
        // CKP-004-20: 6 筛选器字段
        public bool IsHidden { get; set; }
        public bool IsEnvelope { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsInPdmVault { get; set; }
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
        public bool IsInPdmVault { get; set; }
        // Dynamic custom properties
        public Dictionary<string, CockpitPropertyValue> Properties { get; set; }
            = new Dictionary<string, CockpitPropertyValue>(StringComparer.OrdinalIgnoreCase);
        // Flat string map for reliable JSON binding in WebView
        public Dictionary<string, string> ResolvedProperties { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
