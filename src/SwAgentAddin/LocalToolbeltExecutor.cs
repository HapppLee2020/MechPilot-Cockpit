using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SwAgentAddin
{
    /// <summary>
    /// LocalToolbelt Executor — 本地 P0 工程工具执行器
    /// </summary>
    public class LocalToolbeltExecutor
    {
        private readonly ISldWorks _swApp;
        private readonly AddinConfig _config;
        private readonly string _outputBase;

        public LocalToolbeltExecutor(ISldWorks swApp, AddinConfig config)
        {
            _swApp = swApp;
            _config = config;
            _outputBase = Path.Combine(GetDeployDirectory(), "outputs");
            Directory.CreateDirectory(_outputBase);
            Directory.CreateDirectory(Path.Combine(_outputBase, "bom"));
            Directory.CreateDirectory(Path.Combine(_outputBase, "convert"));
            Directory.CreateDirectory(Path.Combine(_outputBase, "drawing"));
            Directory.CreateDirectory(Path.Combine(_outputBase, "backup"));
        }

        private static string GetDeployDirectory()
        {
            try { return Path.GetDirectoryName(typeof(LocalToolbeltExecutor).Assembly.Location) ?? "D:\\SWAgentAddin"; }
            catch { return "D:\\SWAgentAddin"; }
        }

        // ===== 2.1 文件名拆分写属性 =====
        public MechPilotResult FilenameParseToProperties(MechPilotCommand cmd)
        {
            var result = new MechPilotResult { CommandId = cmd.CommandId };
            try
            {
                var rules = LoadFilenameParseRules();
                if (rules.Count == 0)
                    return Fail(result, "未配置文件名解析规则。请在 config.json 中添加 filename_parse_rules。");

                IModelDoc2 activeDoc = _swApp?.ActiveDoc as IModelDoc2;
                if (activeDoc == null) return Fail(result, "请先打开一个 SolidWorks 文档。");
                string filePath = activeDoc.GetPathName();
                if (string.IsNullOrWhiteSpace(filePath)) return Fail(result, "请先保存当前文档。");
                string fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
                Log($"[FilenameParse] 解析: {fileNameNoExt}");

                var parsedProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string matchedRule = null;
                foreach (var rule in rules)
                {
                    try
                    {
                        var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase);
                        var match = regex.Match(fileNameNoExt);
                        if (!match.Success) continue;
                        matchedRule = rule.Name;
                        foreach (var m in rule.Map)
                            if (match.Groups[m.Key].Success)
                                parsedProps[m.Value] = match.Groups[m.Key].Value;
                        break;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add(new MechPilotWarning { Level = "warning", Target = rule.Name, Message = "正则执行失败: " + ex.Message });
                    }
                }

                if (parsedProps.Count == 0)
                    return Fail(result, "文件名不匹配任何解析规则: " + fileNameNoExt);

                // Preview + confirm
                var sb = new StringBuilder();
                sb.AppendLine("文件: " + fileNameNoExt);
                sb.AppendLine("匹配规则: " + matchedRule);
                sb.AppendLine();
                sb.AppendLine("将要写入以下属性:");
                foreach (var kv in parsedProps) sb.AppendLine($"  {kv.Key,-16} = {kv.Value}");

                if (MessageBox.Show(sb.ToString() + "\n\n是否确认写入？", "MechPilot - 文件名拆分", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return Fail(result, "用户取消了写入。");

                CustomPropertyManager mgr = activeDoc.Extension.get_CustomPropertyManager("");
                if (mgr == null) return Fail(result, "无法获取 CustomPropertyManager。");

                int wrote = 0;
                foreach (var kv in parsedProps)
                {
                    try { mgr.Add3(kv.Key, 30, kv.Value, 1); wrote++; }
                    catch (Exception ex) { result.Warnings.Add(new MechPilotWarning { Level = "error", Target = kv.Key, Message = ex.Message }); }
                }
                try { activeDoc.ForceRebuild3(false); } catch { }
                try { activeDoc.SetSaveFlag(); } catch { }

                result.Ok = true;
                result.Message = $"完成: 写入 {wrote}/{parsedProps.Count} 个属性";
                result.Data["wrote"] = wrote;
                result.Data["total"] = parsedProps.Count;
                result.Data["properties"] = parsedProps;
                Log($"[FilenameParse] 完成: {wrote}/{parsedProps.Count}");
            }
            catch (Exception ex) { Log("[FilenameParse] 异常: " + ex); return Fail(result, ex.Message); }
            return result;
        }

        // ===== 2.2 BOM 导出 =====
        public MechPilotResult BomExport(MechPilotCommand cmd)
        {
            var result = new MechPilotResult { CommandId = cmd.CommandId };
            try
            {
                IModelDoc2 activeDoc = _swApp?.ActiveDoc as IModelDoc2;
                if (activeDoc == null) return Fail(result, "请先打开一个 SolidWorks 文档。");
                int docType = activeDoc.GetType();
                if ((swDocumentTypes_e)docType != swDocumentTypes_e.swDocASSEMBLY)
                    return Fail(result, "BOM 导出仅支持装配体。当前类型: " + GetDocTypeName(docType));

                string asmPath = activeDoc.GetPathName();
                if (string.IsNullOrWhiteSpace(asmPath)) return Fail(result, "请先保存当前装配体。");

                IAssemblyDoc asm = activeDoc as IAssemblyDoc;
                if (asm == null) return Fail(result, "无法获取 IAssemblyDoc 接口。");
                object compObjs = asm.GetComponents(false);
                if (compObjs == null) return Fail(result, "组件列表为空。");
                object[] comps = compObjs as object[];
                if (comps == null) return Fail(result, "组件列表为空。");

                var groupMap = new Dictionary<string, BomRow>(StringComparer.OrdinalIgnoreCase);
                int suppressed = 0;

                foreach (object c in comps)
                {
                    IComponent2 comp = c as IComponent2;
                    if (comp == null) continue;
                    try { if (comp.GetSuppression() == (int)swComponentSuppressionState_e.swComponentSuppressed) { suppressed++; continue; } } catch { }
                    IModelDoc2 cm = null;
                    try { cm = (IModelDoc2)comp.GetModelDoc2(); } catch { }
                    if (cm == null) continue;
                    string cp = cm.GetPathName();
                    if (string.IsNullOrWhiteSpace(cp)) continue;

                    if (!groupMap.ContainsKey(cp))
                    {
                        string nm = Path.GetFileNameWithoutExtension(cp);
                        string cdt = GetDocTypeName(cm.GetType());
                        string cn = comp.Name2 ?? nm;
                        var props = ReadKeyProperties(cm);
                        groupMap[cp] = new BomRow { FilePath = cp, ComponentName = nm, InstanceName = cn, DocType = cdt, Properties = props };
                    }
                    groupMap[cp].Quantity++;
                }

                string asmName = Path.GetFileNameWithoutExtension(asmPath);
                string csvDir = Path.Combine(_outputBase, "bom");
                string csvPath = Path.Combine(csvDir, $"{asmName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                var csv = new StringBuilder();
                csv.AppendLine("序号,数量,零件名称,文档类型,图号,材料,物料名称,表面处理,重量,文件路径");
                int idx = 0;
                foreach (var kv in groupMap)
                {
                    idx++;
                    var r = kv.Value;
                    csv.AppendLine($"{idx},{r.Quantity},{EscapeCsv(r.ComponentName)},{r.DocType}," +
                        $"{EscapeCsv(r.Properties.ContainsKey("图号") ? r.Properties["图号"] : "")}," +
                        $"{EscapeCsv(r.Properties.ContainsKey("材料") ? r.Properties["材料"] : "")}," +
                        $"{EscapeCsv(r.Properties.ContainsKey("物料名称") ? r.Properties["物料名称"] : "")}," +
                        $"{EscapeCsv(r.Properties.ContainsKey("表面处理") ? r.Properties["表面处理"] : "")}," +
                        $"{EscapeCsv(r.Properties.ContainsKey("重量") ? r.Properties["重量"] : "")}," +
                        $"{EscapeCsv(r.FilePath)}");
                }
                File.WriteAllText(csvPath, csv.ToString(), Encoding.UTF8);

                result.Ok = true;
                result.Message = $"BOM 导出完成: {groupMap.Count} 个唯一零件（含 {suppressed} 个抑制）";
                result.Data["unique_parts"] = groupMap.Count;
                result.Data["total_instances"] = comps.Length;
                result.Data["suppressed"] = suppressed;
                result.Artifacts.Add(new MechPilotArtifact { Type = "csv", Path = csvPath, Size = new FileInfo(csvPath).Length, Description = $"{asmName} BOM" });
                Log($"[BomExport] 完成: {groupMap.Count} 行 -> {csvPath}");
            }
            catch (Exception ex) { Log("[BomExport] 异常: " + ex); return Fail(result, ex.Message); }
            return result;
        }

        // ===== 2.3 批量转换 =====
        public MechPilotResult BatchConvert(MechPilotCommand cmd)
        {
            var result = new MechPilotResult { CommandId = cmd.CommandId };
            try
            {
                IModelDoc2 activeDoc = _swApp?.ActiveDoc as IModelDoc2;
                if (activeDoc == null) return Fail(result, "请先打开一个 SolidWorks 文档。");
                string srcPath = activeDoc.GetPathName();
                if (string.IsNullOrWhiteSpace(srcPath)) return Fail(result, "请先保存当前文档。");

                string format = "pdf";
                if (cmd.Payload != null && cmd.Payload.ContainsKey("format"))
                    format = Convert.ToString(cmd.Payload["format"]).ToLowerInvariant();

                int docType = activeDoc.GetType();
                string convertDir = Path.Combine(_outputBase, "convert");
                string baseName = Path.GetFileNameWithoutExtension(srcPath);

                switch (format)
                {
                    case "pdf":
                        return ConvertToPdf(activeDoc, docType, srcPath, baseName, convertDir, result);
                    case "step":
                    case "stp":
                        return ConvertToStep(activeDoc, docType, srcPath, baseName, convertDir, result);
                    case "dxf":
                    case "dwg":
                        return ConvertToDxfDwg(activeDoc, docType, srcPath, baseName, format, convertDir, result);
                    default:
                        return Fail(result, $"不支持的转换格式: {format}。支持: pdf, step, stp, dxf, dwg");
                }
            }
            catch (Exception ex) { Log("[BatchConvert] 异常: " + ex); return Fail(result, ex.Message); }
        }

        // ===== 2.4 图纸导出 =====
        public MechPilotResult DrawingExport(MechPilotCommand cmd)
        {
            var result = new MechPilotResult { CommandId = cmd.CommandId };
            try
            {
                IModelDoc2 activeDoc = _swApp?.ActiveDoc as IModelDoc2;
                if (activeDoc == null) return Fail(result, "请先打开一个 SolidWorks 文档。");
                string srcPath = activeDoc.GetPathName();
                if (string.IsNullOrWhiteSpace(srcPath)) return Fail(result, "请先保存当前文档。");

                int docType = activeDoc.GetType();
                string drawDir = Path.Combine(_outputBase, "drawing");

                if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocDRAWING)
                {
                    // Current doc IS a drawing — export directly
                    return ConvertToPdf(activeDoc, docType, srcPath, Path.GetFileNameWithoutExtension(srcPath), drawDir, result);
                }

                // Part/Assembly — search for same-name drawing
                string baseName = Path.GetFileNameWithoutExtension(srcPath);
                string srcDir = Path.GetDirectoryName(srcPath);
                string[] drawingExts = { ".SLDDRW", ".slddrw" };
                string foundDrawing = null;
                foreach (string ext in drawingExts)
                {
                    string candidate = Path.Combine(srcDir, baseName + ext);
                    if (File.Exists(candidate)) { foundDrawing = candidate; break; }
                }

                if (foundDrawing == null)
                    return Fail(result, $"未找到同名工程图。在 {srcDir} 中搜索 {baseName}.SLDDRW 失败。");

                // Open and export
                int errors = 0, warnings = 0;
                IModelDoc2 drawDoc = _swApp.OpenDoc6(foundDrawing, (int)swDocumentTypes_e.swDocDRAWING,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
                if (drawDoc == null)
                    return Fail(result, $"无法打开工程图: {foundDrawing}");

                var subResult = ConvertToPdf(drawDoc, (int)swDocumentTypes_e.swDocDRAWING, foundDrawing, baseName, drawDir, result);
                // Close the opened drawing if it wasn't originally open
                try { _swApp.CloseDoc(foundDrawing); } catch { }
                return subResult;
            }
            catch (Exception ex) { Log("[DrawingExport] 异常: " + ex); return Fail(result, ex.Message); }
        }

        // ===== 2.5 打包备份 =====
        public MechPilotResult PackageBackup(MechPilotCommand cmd)
        {
            var result = new MechPilotResult { CommandId = cmd.CommandId };
            try
            {
                IModelDoc2 activeDoc = _swApp?.ActiveDoc as IModelDoc2;
                if (activeDoc == null) return Fail(result, "请先打开一个 SolidWorks 文档。");
                string srcPath = activeDoc.GetPathName();
                if (string.IsNullOrWhiteSpace(srcPath)) return Fail(result, "请先保存当前文档。");

                string baseName = Path.GetFileNameWithoutExtension(srcPath);
                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupDir = Path.Combine(_outputBase, "backup", $"{baseName}_{timeStamp}");

                string confirmMsg = $"备份目标: {backupDir}\n\n将打包:\n- 当前文件\n- 引用零部件（同目录）\n- 同名工程图（如存在）\n- 配置文件\n\n是否继续？";
                if (MessageBox.Show(confirmMsg, "MechPilot - 打包备份", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return Fail(result, "用户取消了备份。");

                Directory.CreateDirectory(backupDir);

                int copied = 0;
                var warnings = new List<string>();

                // 1. Current file
                try { CopyFileSafe(srcPath, backupDir); copied++; }
                catch (Exception ex) { warnings.Add("主文件: " + ex.Message); }

                // 2. Assembly referenced components
                int docType = activeDoc.GetType();
                if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocASSEMBLY)
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
                                var copiedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (object c in comps)
                                {
                                    IComponent2 comp = c as IComponent2;
                                    if (comp == null) continue;
                                    try { if (comp.GetSuppression() == (int)swComponentSuppressionState_e.swComponentSuppressed) continue; } catch { }
                                    IModelDoc2 cm = null;
                                    try { cm = (IModelDoc2)comp.GetModelDoc2(); } catch { }
                                    if (cm == null) continue;
                                    string cp = cm.GetPathName();
                                    if (string.IsNullOrWhiteSpace(cp) || copiedPaths.Contains(cp)) continue;
                                    try { CopyFileSafe(cp, backupDir); copied++; copiedPaths.Add(cp); }
                                    catch (Exception ex) { warnings.Add(Path.GetFileName(cp) + ": " + ex.Message); }
                                }
                            }
                        }
                    }
                }

                // 3. Same-name drawing
                string srcDir = Path.GetDirectoryName(srcPath);
                foreach (string ext in new[] { ".SLDDRW", ".slddrw" })
                {
                    string drawPath = Path.Combine(srcDir, baseName + ext);
                    if (File.Exists(drawPath))
                    {
                        try { CopyFileSafe(drawPath, backupDir); copied++; } catch { }
                        break;
                    }
                }

                // 4. Config files
                string deployDir = GetDeployDirectory();
                foreach (string cfgFile in new[] { "config.json", "rules.local.json" })
                {
                    string cfgPath = Path.Combine(deployDir, "config", cfgFile);
                    if (File.Exists(cfgPath))
                    {
                        try { CopyFileSafe(cfgPath, backupDir); } catch { }
                    }
                }

                result.Ok = true;
                result.Message = $"备份完成: {copied} 个文件 -> {backupDir}";
                result.Data["copied"] = copied;
                result.Data["backup_dir"] = backupDir;
                if (warnings.Count > 0) result.Data["warnings"] = warnings;
                result.Artifacts.Add(new MechPilotArtifact { Type = "dir", Path = backupDir, Size = 0, Description = $"{baseName} 打包备份" });
                Log($"[PackageBackup] 完成: {copied} 个文件 -> {backupDir}");
            }
            catch (Exception ex) { Log("[PackageBackup] 异常: " + ex); return Fail(result, ex.Message); }
            return result;
        }

        #region Conversion Helpers

        private MechPilotResult ConvertToPdf(IModelDoc2 doc, int docType, string srcPath, string baseName, string outDir, MechPilotResult result)
        {
            string outPath = Path.Combine(outDir, baseName + ".pdf");
            // Don't silently overwrite
            int fileIndex = 1;
            while (File.Exists(outPath))
            {
                outPath = Path.Combine(outDir, $"{baseName}_{fileIndex}.pdf");
                fileIndex++;
            }

            bool success = false;
            string errorMsg = "";

            if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocDRAWING)
            {
                try
                {
                    IDrawingDoc drawDoc = doc as IDrawingDoc;
                    if (drawDoc != null)
                    {
                        int errors = 0, warnings = 0;
                        bool exportResult = doc.Extension.SaveAs(outPath,
                            (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                            null, ref errors, ref warnings);

                        success = exportResult;
                    }
                }
                catch (Exception ex)
                {
                    errorMsg = ex.Message;
                    // Try alternate method
                    try
                    {
                        int errs = 0, warns = 0;
                        doc.Extension.SaveAs(outPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                            (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errs, ref warns);
                        success = File.Exists(outPath);
                    }
                    catch { }
                }
            }
            else
            {
                // Part/Assembly -> PDF (3D PDF or drawing-like)
                errorMsg = "零件/装配体导出PDF需要工程图方式。请使用 drawing.export 命令或先制作工程图。";
                success = false;
            }

            if (!success)
            {
                result.Ok = false;
                result.Message = "PDF 导出失败: " + errorMsg;
                return result;
            }

            result.Ok = true;
            result.Message = "PDF 导出完成: " + outPath;
            result.Artifacts.Add(new MechPilotArtifact { Type = "pdf", Path = outPath, Size = new FileInfo(outPath).Length, Description = baseName + ".pdf" });
            Log($"[Convert] PDF: {outPath}");
            return result;
        }

        private MechPilotResult ConvertToStep(IModelDoc2 doc, int docType, string srcPath, string baseName, string outDir, MechPilotResult result)
        {
            if ((swDocumentTypes_e)docType != swDocumentTypes_e.swDocPART &&
                (swDocumentTypes_e)docType != swDocumentTypes_e.swDocASSEMBLY)
                return Fail(result, "STEP 导出仅支持零件和装配体。");

            string outPath = Path.Combine(outDir, baseName + ".step");
            int fileIndex = 1;
            while (File.Exists(outPath))
            {
                outPath = Path.Combine(outDir, $"{baseName}_{fileIndex}.step");
                fileIndex++;
            }

            try
            {
                int errors = 0, warnings = 0;
                bool saved = doc.Extension.SaveAs(outPath,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null, ref errors, ref warnings);

                if (!saved || !File.Exists(outPath))
                    return Fail(result, "STEP 保存失败。请确认 SW API SaveAs 支持 STEP 格式。");

                result.Ok = true;
                result.Message = "STEP 导出完成: " + outPath;
                result.Artifacts.Add(new MechPilotArtifact { Type = "step", Path = outPath, Size = new FileInfo(outPath).Length, Description = baseName + ".step" });
                Log($"[Convert] STEP: {outPath}");
            }
            catch (Exception ex)
            {
                return Fail(result, "STEP 导出异常: " + ex.Message);
            }
            return result;
        }

        private MechPilotResult ConvertToDxfDwg(IModelDoc2 doc, int docType, string srcPath, string baseName, string format, string outDir, MechPilotResult result)
        {
            if ((swDocumentTypes_e)docType != swDocumentTypes_e.swDocDRAWING)
                return Fail(result, $"{format.ToUpper()} 导出仅支持工程图。");

            string outPath = Path.Combine(outDir, baseName + "." + format);
            int fileIndex = 1;
            while (File.Exists(outPath))
            {
                outPath = Path.Combine(outDir, $"{baseName}_{fileIndex}.{format}");
                fileIndex++;
            }

            try
            {
                int errors = 0, warnings = 0;
                bool saved = doc.Extension.SaveAs(outPath,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null, ref errors, ref warnings);

                if (!saved || !File.Exists(outPath))
                    return Fail(result, $"{format.ToUpper()} 保存失败。");

                result.Ok = true;
                result.Message = $"{format.ToUpper()} 导出完成: " + outPath;
                result.Artifacts.Add(new MechPilotArtifact { Type = format, Path = outPath, Size = new FileInfo(outPath).Length, Description = baseName + "." + format });
                Log($"[Convert] {format.ToUpper()}: {outPath}");
            }
            catch (Exception ex)
            {
                return Fail(result, $"{format.ToUpper()} 导出异常: " + ex.Message);
            }
            return result;
        }

        #endregion

        #region Helpers

        private static MechPilotResult Fail(MechPilotResult r, string msg)
        {
            r.Ok = false;
            r.Message = msg;
            return r;
        }

        private static string GetDocTypeName(int dt)
        {
            if ((swDocumentTypes_e)dt == swDocumentTypes_e.swDocPART) return "part";
            if ((swDocumentTypes_e)dt == swDocumentTypes_e.swDocASSEMBLY) return "assembly";
            if ((swDocumentTypes_e)dt == swDocumentTypes_e.swDocDRAWING) return "drawing";
            return "unknown";
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(",") || val.Contains("\"") || val.Contains("\n"))
                return "\"" + val.Replace("\"", "\"\"") + "\"";
            return val;
        }

        private Dictionary<string, string> ReadKeyProperties(IModelDoc2 model)
        {
            var props = new Dictionary<string, string>();
            try
            {
                CustomPropertyManager mgr = model.Extension.get_CustomPropertyManager("");
                if (mgr != null)
                {
                    foreach (string pn in new[] { "物料名称", "图号", "材料", "表面处理", "重量", "处理状态", "处理人" })
                    {
                        string rv = "", ev = "";
                        try { mgr.Get2(pn, out rv, out ev); } catch { }
                        props[pn] = ev ?? rv ?? "";
                    }
                }
            }
            catch { }
            return props;
        }

        private static void CopyFileSafe(string src, string destDir)
        {
            string name = Path.GetFileName(src);
            string dest = Path.Combine(destDir, name);
            File.Copy(src, dest, true);
        }

        private static List<FilenameParseRule> LoadFilenameParseRules()
        {
            var rules = new List<FilenameParseRule>();
            try
            {
                string configPath = Path.Combine(GetDeployDirectory(), "config", "config.json");
                if (!File.Exists(configPath)) return GetDefaultFilenameRules();

                string json = File.ReadAllText(configPath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                var dict = serializer.Deserialize<Dictionary<string, object>>(json);

                if (dict != null && dict.ContainsKey("filename_parse_rules"))
                {
                    var arr = dict["filename_parse_rules"] as System.Collections.ArrayList;
                    if (arr != null)
                    {
                        foreach (var item in arr)
                        {
                            var ruleDict = item as Dictionary<string, object>;
                            if (ruleDict == null) continue;
                            var rule = new FilenameParseRule();
                            if (ruleDict.ContainsKey("name")) rule.Name = Convert.ToString(ruleDict["name"]);
                            if (ruleDict.ContainsKey("pattern")) rule.Pattern = Convert.ToString(ruleDict["pattern"]);
                            if (ruleDict.ContainsKey("map") && ruleDict["map"] is Dictionary<string, object> mapDict)
                            {
                                foreach (var kv in mapDict)
                                    rule.Map[kv.Key] = Convert.ToString(kv.Value);
                            }
                            if (!string.IsNullOrEmpty(rule.Pattern))
                                rules.Add(rule);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[LocalToolbelt] LoadFilenameParseRules error: " + ex);
            }

            if (rules.Count == 0)
                return GetDefaultFilenameRules();
            return rules;
        }

        private static List<FilenameParseRule> GetDefaultFilenameRules()
        {
            return new List<FilenameParseRule>
            {
                new FilenameParseRule
                {
                    Name = "默认规则",
                    Pattern = @"^(?<project>[^-]+)-(?<drawing_no>[^-]+)-(?<part_name>[^-]+)-(?<version>[^.]+)",
                    Map = new Dictionary<string, string>
                    {
                        ["project"] = "项目号",
                        ["drawing_no"] = "图号",
                        ["part_name"] = "物料名称",
                        ["version"] = "版本"
                    }
                },
                new FilenameParseRule
                {
                    Name = "简单命名",
                    Pattern = @"^(?<part_name>.+)$",
                    Map = new Dictionary<string, string>
                    {
                        ["part_name"] = "物料名称"
                    }
                }
            };
        }

        private static void Log(string msg)
        {
            try
            {
                string logPath = Path.Combine(GetDeployDirectory(), "addin-load.log");
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [LocalToolbelt] {msg}";
                File.AppendAllText(logPath, line + System.Environment.NewLine, Encoding.UTF8);
                Debug.WriteLine(line);
            }
            catch { }
        }

        #endregion

        #region Internal Classes

        private class BomRow
        {
            public string FilePath;
            public string ComponentName;
            public string InstanceName;
            public string DocType;
            public int Quantity;
            public Dictionary<string, string> Properties;
        }

        private class FilenameParseRule
        {
            public string Name;
            public string Pattern;
            public Dictionary<string, string> Map = new Dictionary<string, string>();
        }

        #endregion
    }
}
