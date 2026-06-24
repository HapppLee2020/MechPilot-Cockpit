using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SwAgentAddin
{
    /// <summary>
    /// Action Router — 统一命令路由层。
    /// 接收 MechPilotCommand 并路由到 LocalToolbeltExecutor / HermesClient / HindsightRagClient。
    /// 保留旧命令兼容：local.read_properties / refresh_context / property_fill / property_check。
    /// </summary>
    public class ActionRouter
    {
        private readonly ISldWorks _swApp;
        private readonly AddinConfig _config;
        private readonly LocalPropertyRules _rules;
        private readonly Func<string> _buildCockpitContext;
        private readonly Action<string, string> _executeLocalTask;
        private readonly LocalToolbeltExecutor _localExecutor;
        private readonly HermesClient _hermesClient;
        private readonly HindsightRagClient _hindsightClient;

        public ActionRouter(
            ISldWorks swApp,
            AddinConfig config,
            LocalPropertyRules rules,
            Func<string> buildCockpitContext,
            Action<string, string> executeLocalTask)
        {
            _swApp = swApp;
            _config = config;
            _rules = rules;
            _buildCockpitContext = buildCockpitContext;
            _executeLocalTask = executeLocalTask;
            _localExecutor = new LocalToolbeltExecutor(swApp, config);

            // Init Hermes client if configured
            if (config.AgentServer != null &&
                !string.Equals(config.AgentServer.Provider, "none", StringComparison.OrdinalIgnoreCase))
            {
                _hermesClient = new HermesClient(config.AgentServer, WriteTrace);
            }

            // Init Hindsight RAG client if enabled
            if (config.Hindsight != null && config.Hindsight.Enabled)
            {
                _hindsightClient = new HindsightRagClient(config.Hindsight, WriteTrace);
            }
        }

        /// <summary>
        /// 主入口 — 处理 JSON 命令字符串，返回 JSON 结果字符串
        /// </summary>
        public string HandleCommand(string commandJson)
        {
            try
            {
                MechPilotCommand cmd = MechPilotCommand.FromJson(commandJson);
                if (cmd == null)
                    return MechPilotResult.FailResult("", "无法解析命令 JSON").ToJson();

                // Ensure CommandId
                if (string.IsNullOrEmpty(cmd.CommandId))
                    cmd.CommandId = "cmd-" + Guid.NewGuid().ToString("N").Substring(0, 8);

                WriteTrace("route command_id=" + cmd.CommandId
                    + " feature=" + (cmd.Feature ?? "")
                    + " action=" + (cmd.Action ?? "")
                    + " executor=" + (cmd.Executor ?? ""));

                MechPilotResult result = Route(cmd);
                WriteTrace("route result command_id=" + cmd.CommandId
                    + " ok=" + result.Ok
                    + " message=" + Shorten(result.Message, 160));
                return result.ToJson();
            }
            catch (Exception ex)
            {
                WriteTrace("ActionRouter.HandleCommand error: " + ex);
                return MechPilotResult.FailResult("", "路由异常: " + ex.Message).ToJson();
            }
        }

        /// <summary>
        /// 从 feature/action/executor 重建前端期望的 legacy command 名
        /// </summary>
        private static string RebuildLegacyCommand(string feature, string action, string executor)
        {
            if (string.Equals(executor, "hindsight", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(feature, "material", StringComparison.OrdinalIgnoreCase))
                    return "ai.material.search";
            }
            if (string.Equals(executor, "hermes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(executor, "remote", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(feature, "assistant", StringComparison.OrdinalIgnoreCase)) return "ai.assistant.chat";
                if (string.Equals(feature, "drawing", StringComparison.OrdinalIgnoreCase)) return "ai.drawing.review";
                if (string.Equals(feature, "selection", StringComparison.OrdinalIgnoreCase)) return "ai.selection.recommend";
                if (string.Equals(feature, "design", StringComparison.OrdinalIgnoreCase)) return "ai.design.calculate";
                if (string.Equals(feature, "node", StringComparison.OrdinalIgnoreCase)) return "ai.node.analyze";
                if (string.Equals(feature, "agent", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(action, "submit", StringComparison.OrdinalIgnoreCase)) return "agent.task.submit";
                    if (string.Equals(action, "poll", StringComparison.OrdinalIgnoreCase)) return "agent.task.poll";
                    if (string.Equals(action, "result", StringComparison.OrdinalIgnoreCase)) return "agent.task.result";
                }
            }
            if (string.Equals(executor, "local", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(feature, "selection", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(action, "node_selected", StringComparison.OrdinalIgnoreCase))
                    return "node_selected";
                if (string.Equals(feature, "properties", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(action, "check", StringComparison.OrdinalIgnoreCase))
                    return "ai.props.check";
                if (string.Equals(feature, "bom", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(action, "locate", StringComparison.OrdinalIgnoreCase))
                    return "bom.locate";
            }
            return feature + "." + action;
        }

        private MechPilotResult Route(MechPilotCommand cmd)
        {
            string feature = cmd.Feature ?? "";
            string action = cmd.Action ?? "";
            string legacyCmd = RebuildLegacyCommand(feature, action, cmd.Executor ?? "");

            // ─── Local Toolbelt commands ───
            if (string.Equals(cmd.Executor, "local", StringComparison.OrdinalIgnoreCase))
            {
                switch ($"{feature}.{action}")
                {
                    // Legacy compatibility
                    case "properties.read":
                        return ExecuteReadProperties(cmd);
                    case "properties.write":
                    case "properties.fill":
                        return ExecutePropertyFill(cmd);
                    case "properties.check":
                        return ExecutePropertyCheck(cmd);
                    case "selection.node_selected":
                        return RecordNodeSelection(cmd);
                    case "bom.locate":
                        return LocateBomTarget(cmd);

                    // LocalToolbelt P0 features
                    case "filename.parse_to_properties":
                        return _localExecutor.FilenameParseToProperties(cmd);
                    case "bom.export":
                        return _localExecutor.BomExport(cmd);
                    case "file.convert":
                        return _localExecutor.BatchConvert(cmd);
                    case "drawing.export":
                        return _localExecutor.DrawingExport(cmd);
                    case "package.backup":
                        return _localExecutor.PackageBackup(cmd);

                    // System
                    case "system.ping":
                        return Ping(cmd);

                    default:
                        return TryLocalExecutor(feature, action, cmd);
                }
            }

            // ─── Hindsight RAG commands (material.search) ───
            if (string.Equals(cmd.Executor, "hindsight", StringComparison.OrdinalIgnoreCase))
            {
                return RouteHindsight(feature, action, cmd, legacyCmd);
            }

            // ─── Hermes / Remote AI commands ───
            if (string.Equals(cmd.Executor, "hermes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmd.Executor, "remote", StringComparison.OrdinalIgnoreCase))
            {
                return RouteHermes(feature, action, cmd, legacyCmd);
            }

            // Unknown
            return MechPilotResult.FailResult(cmd.CommandId,
                "未知命令: feature=" + feature + " action=" + action + " executor=" + cmd.Executor,
                "UNKNOWN_COMMAND", legacyCmd);
        }

        /// <summary>
        /// Hindsight RAG 路由 — 物料检索优先走本地知识库
        /// </summary>
        private MechPilotResult RouteHindsight(string feature, string action, MechPilotCommand cmd, string legacyCmd)
        {
            if (_hindsightClient == null)
            {
                WriteTrace("Hindsight route skipped: client not initialized.");
                return MechPilotResult.FailResult(cmd.CommandId,
                    "Hindsight RAG 未启用或未配置。请检查 config.json 中 hindsight.enabled = true",
                    "HINDSIGHT_OFFLINE", legacyCmd);
            }

            try
            {
                string query = "";
                if (cmd.Payload.ContainsKey("query"))
                    query = Convert.ToString(cmd.Payload["query"]);
                else if (cmd.Payload.ContainsKey("keyword"))
                    query = Convert.ToString(cmd.Payload["keyword"]);
                else if (cmd.Payload.ContainsKey("text"))
                    query = Convert.ToString(cmd.Payload["text"]);

                if (string.IsNullOrWhiteSpace(query))
                    return MechPilotResult.FailResult(cmd.CommandId, "缺少查询参数 (query/keyword/text)",
                        "MISSING_QUERY", legacyCmd);

                int? topK = cmd.Payload.ContainsKey("top_k") ? Convert.ToInt32(cmd.Payload["top_k"]) : (int?)null;
                string bankOverride = cmd.Payload.ContainsKey("collection") ? Convert.ToString(cmd.Payload["collection"]) : null;
                double? scoreThreshold = cmd.Payload.ContainsKey("score_threshold")
                    ? Convert.ToDouble(cmd.Payload["score_threshold"])
                    : (double?)null;
                WriteTrace("Hindsight route query=" + Shorten(query, 80)
                    + " top_k=" + (topK.HasValue ? topK.Value.ToString() : "default"));
                var ragResult = _hindsightClient.Query(query, topK, bankOverride, scoreThreshold, cmd.Payload);

                if (ragResult.ContainsKey("success") && Convert.ToBoolean(ragResult["success"]))
                {
                    return MechPilotResult.OkResult(cmd.CommandId, "Hindsight 检索完成", ragResult, legacyCmd);
                }

                string error = ragResult.ContainsKey("error") ? Convert.ToString(ragResult["error"]) : "未知错误";
                return MechPilotResult.FailResult(cmd.CommandId, "Hindsight 检索失败: " + error,
                    "HINDSIGHT_OFFLINE", legacyCmd);
            }
            catch (Exception ex)
            {
                WriteTrace("RouteHindsight error: " + ex);
                return MechPilotResult.FailResult(cmd.CommandId, "Hindsight 异常: " + ex.Message,
                    "HINDSIGHT_OFFLINE", legacyCmd);
            }
        }

        /// <summary>
        /// Hermes Agent 路由 — AI 助手、图纸审核、选型推荐、设计计算、Agent 任务
        /// </summary>
        private MechPilotResult RouteHermes(string feature, string action, MechPilotCommand cmd, string legacyCmd)
        {
            if (_hermesClient == null)
            {
                return MechPilotResult.FailResult(cmd.CommandId,
                    "Hermes Agent 未配置。请检查 config.json 中 agent_server.base_url",
                    "HERMES_NOT_CONFIGURED", legacyCmd);
            }

            try
            {
                // Agent task lifecycle
                if (string.Equals(feature, "agent", StringComparison.OrdinalIgnoreCase))
                {
                    return RouteAgentTask(action, cmd, legacyCmd);
                }

                // Build context for Hermes
                string contextJson = _buildCockpitContext != null ? _buildCockpitContext() : "{}";
                string trimmedContext = ContextTrimmer.Trim(contextJson,
                    _config.AgentServer?.ContextModeDefault ?? "summary");

                var payload = new Dictionary<string, object>();
                foreach (var kv in cmd.Payload) payload[kv.Key] = kv.Value;
                payload["context"] = trimmedContext;
                payload["feature"] = feature;
                payload["action"] = action;

                string hermesResult = _hermesClient.InvokeAgent($"{feature}.{action}", payload);

                // Parse Hermes response
                var serializer = new JavaScriptSerializer();
                var parsed = serializer.Deserialize<Dictionary<string, object>>(hermesResult);

                if (parsed != null && parsed.ContainsKey("success") && Convert.ToBoolean(parsed["success"]))
                {
                    var data = parsed.ContainsKey("data") ? parsed["data"] : null;
                    return MechPilotResult.OkResult(cmd.CommandId,
                        parsed.ContainsKey("message") ? Convert.ToString(parsed["message"]) : "Hermes 调用成功",
                        data as Dictionary<string, object> ?? new Dictionary<string, object> { ["raw"] = hermesResult },
                        legacyCmd);
                }

                string error = parsed != null && parsed.ContainsKey("error")
                    ? Convert.ToString(parsed["error"])
                    : hermesResult;
                return MechPilotResult.FailResult(cmd.CommandId, "Hermes 调用失败: " + Shorten(error, 200),
                    "HERMES_CALL_FAILED", legacyCmd);
            }
            catch (Exception ex)
            {
                WriteTrace("RouteHermes error: " + ex);
                return MechPilotResult.FailResult(cmd.CommandId, "Hermes 异常: " + ex.Message,
                    "HERMES_EXCEPTION", legacyCmd);
            }
        }

        /// <summary>
        /// Agent 任务生命周期：submit → poll → result
        /// </summary>
        private MechPilotResult RouteAgentTask(string action, MechPilotCommand cmd, string legacyCmd)
        {
            switch (action)
            {
                case "submit":
                    var submitResult = _hermesClient.SubmitTask(
                        cmd.Payload.ContainsKey("task_type") ? Convert.ToString(cmd.Payload["task_type"]) : "general",
                        cmd.Payload);
                    if (submitResult.ContainsKey("success") && Convert.ToBoolean(submitResult["success"]))
                        return MechPilotResult.OkResult(cmd.CommandId, "任务已提交", submitResult, legacyCmd);
                    return MechPilotResult.FailResult(cmd.CommandId,
                        submitResult.ContainsKey("error") ? Convert.ToString(submitResult["error"]) : "提交失败",
                        "AGENT_SUBMIT_FAILED", legacyCmd);

                case "poll":
                    string pollTaskId = cmd.Payload.ContainsKey("task_id") ? Convert.ToString(cmd.Payload["task_id"]) : "";
                    if (string.IsNullOrEmpty(pollTaskId))
                        return MechPilotResult.FailResult(cmd.CommandId, "缺少 task_id", "MISSING_TASK_ID", legacyCmd);
                    var pollResult = _hermesClient.PollTaskStatus(pollTaskId);
                    if (pollResult.ContainsKey("success") && Convert.ToBoolean(pollResult["success"]))
                        return MechPilotResult.OkResult(cmd.CommandId, "任务状态已获取", pollResult, legacyCmd);
                    return MechPilotResult.FailResult(cmd.CommandId,
                        pollResult.ContainsKey("error") ? Convert.ToString(pollResult["error"]) : "轮询失败",
                        "AGENT_POLL_FAILED", legacyCmd);

                case "result":
                    string resultTaskId = cmd.Payload.ContainsKey("task_id") ? Convert.ToString(cmd.Payload["task_id"]) : "";
                    if (string.IsNullOrEmpty(resultTaskId))
                        return MechPilotResult.FailResult(cmd.CommandId, "缺少 task_id", "MISSING_TASK_ID", legacyCmd);
                    var taskResult = _hermesClient.GetTaskResult(resultTaskId);
                    if (taskResult.ContainsKey("success") && Convert.ToBoolean(taskResult["success"]))
                        return MechPilotResult.OkResult(cmd.CommandId, "任务结果已获取", taskResult, legacyCmd);
                    return MechPilotResult.FailResult(cmd.CommandId,
                        taskResult.ContainsKey("error") ? Convert.ToString(taskResult["error"]) : "获取结果失败",
                        "AGENT_RESULT_FAILED", legacyCmd);

                default:
                    return MechPilotResult.FailResult(cmd.CommandId, "未知 agent 操作: " + action,
                        "UNKNOWN_AGENT_ACTION", legacyCmd);
            }
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
            return text.Substring(0, max) + "...";
        }

        private MechPilotResult TryLocalExecutor(string feature, string action, MechPilotCommand cmd)
        {
            // Map feature to P0 method
            switch (feature)
            {
                case "filename": return _localExecutor.FilenameParseToProperties(cmd);
                case "bom": return _localExecutor.BomExport(cmd);
                case "file": return _localExecutor.BatchConvert(cmd);
                case "drawing": return _localExecutor.DrawingExport(cmd);
                case "package": return _localExecutor.PackageBackup(cmd);
                default:
                    return MechPilotResult.FailResult(cmd.CommandId,
                        "未实现的本地功能: feature=" + feature + " action=" + action);
            }
        }

        #region Legacy Handlers

        private MechPilotResult RecordNodeSelection(MechPilotCommand cmd)
        {
            string nodeId = "";
            string name = "";
            if (cmd.Payload != null)
            {
                if (cmd.Payload.ContainsKey("nodeId")) nodeId = Convert.ToString(cmd.Payload["nodeId"]);
                if (cmd.Payload.ContainsKey("name")) name = Convert.ToString(cmd.Payload["name"]);
            }

            return MechPilotResult.OkResult(cmd.CommandId, "已同步当前选择: " + (string.IsNullOrEmpty(name) ? nodeId : name),
                new Dictionary<string, object>
                {
                    ["node_id"] = nodeId ?? "",
                    ["name"] = name ?? "",
                    ["status"] = "selected"
                },
                "node_selected");
        }

        private MechPilotResult LocateBomTarget(MechPilotCommand cmd)
        {
            string nodeId = "";
            string name = "";
            if (cmd.Payload != null)
            {
                if (cmd.Payload.ContainsKey("nodeId")) nodeId = Convert.ToString(cmd.Payload["nodeId"]);
                if (cmd.Payload.ContainsKey("name")) name = Convert.ToString(cmd.Payload["name"]);
            }

            return MechPilotResult.OkResult(cmd.CommandId, "BOM定位已接收，后续可联动BOM视图高亮目标。",
                new Dictionary<string, object>
                {
                    ["node_id"] = nodeId ?? "",
                    ["name"] = name ?? "",
                    ["status"] = "pending_bom_highlight"
                },
                "bom.locate");
        }

        private MechPilotResult Ping(MechPilotCommand cmd)
        {
            return MechPilotResult.OkResult(cmd.CommandId, "pong", new Dictionary<string, object>
            {
                ["pong"] = true,
                ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["engineer_id"] = _config?.EngineerId ?? ""
            });
        }

        private MechPilotResult ExecuteReadProperties(MechPilotCommand cmd)
        {
            try
            {
                bool refreshContext = cmd.Payload.ContainsKey("refresh_context") &&
                    Convert.ToBoolean(cmd.Payload["refresh_context"]);
                string ctxJson = _buildCockpitContext != null ? _buildCockpitContext() : "{}";

                var serializer = new JavaScriptSerializer();
                object ctx = serializer.DeserializeObject(ctxJson);

                return MechPilotResult.OkResult(cmd.CommandId, "上下文已刷新", new Dictionary<string, object>
                {
                    ["context"] = ctx
                });
            }
            catch (Exception ex)
            {
                return MechPilotResult.FailResult(cmd.CommandId, "读取属性上下文失败: " + ex.Message);
            }
        }

        private MechPilotResult ExecutePropertyFill(MechPilotCommand cmd)
        {
            try
            {
                _executeLocalTask?.Invoke("property_fill", "Property Fill");
                return MechPilotResult.OkResult(cmd.CommandId, "属性填写已触发");
            }
            catch (Exception ex)
            {
                return MechPilotResult.FailResult(cmd.CommandId, "属性填写失败: " + ex.Message);
            }
        }

        private MechPilotResult ExecutePropertyCheck(MechPilotCommand cmd)
        {
            try
            {
                _executeLocalTask?.Invoke("property_check", "Property Check");
                return MechPilotResult.OkResult(cmd.CommandId, "属性检查已触发");
            }
            catch (Exception ex)
            {
                return MechPilotResult.FailResult(cmd.CommandId, "属性检查失败: " + ex.Message);
            }
        }

        #endregion

        private static void WriteTrace(string message)
        {
            try { SwAgentAddin.WriteTrace("[ActionRouter] " + message); } catch { }
            try { Debug.WriteLine("[ActionRouter] " + message); } catch { }
        }
    }
}
