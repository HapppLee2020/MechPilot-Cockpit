using System;
using System.Collections.Generic;

namespace SwAgentAddin
{
    /// <summary>
    /// MechPilot 统一命令协议 v1 — 适用于 LocalToolbelt / AICockpit / 工具栏三种入口
    /// schema_version: mechpilot.command.v1
    /// </summary>
    public class MechPilotCommand
    {
        public string SchemaVersion { get; set; } = "mechpilot.command.v1";
        public string CommandId { get; set; }
        public string Source { get; set; }
        public string Feature { get; set; }
        public string Action { get; set; }
        public string Executor { get; set; }
        public MechPilotCommandTarget Target { get; set; }
        public Dictionary<string, object> Payload { get; set; } = new Dictionary<string, object>();

        public static MechPilotCommand FromJson(string json)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var dict = serializer.Deserialize<Dictionary<string, object>>(json);
            if (dict == null) return null;

            var cmd = new MechPilotCommand();
            if (dict.ContainsKey("schema_version")) cmd.SchemaVersion = Convert.ToString(dict["schema_version"]);
            if (dict.ContainsKey("command_id")) cmd.CommandId = Convert.ToString(dict["command_id"]);
            if (dict.ContainsKey("source")) cmd.Source = Convert.ToString(dict["source"]);
            if (dict.ContainsKey("feature")) cmd.Feature = Convert.ToString(dict["feature"]);
            if (dict.ContainsKey("action")) cmd.Action = Convert.ToString(dict["action"]);
            if (dict.ContainsKey("executor")) cmd.Executor = Convert.ToString(dict["executor"]);

            if (dict.ContainsKey("target") && dict["target"] is Dictionary<string, object> tgtDict)
            {
                cmd.Target = new MechPilotCommandTarget();
                if (tgtDict.ContainsKey("scope")) cmd.Target.Scope = Convert.ToString(tgtDict["scope"]);
                if (tgtDict.ContainsKey("file_path")) cmd.Target.FilePath = Convert.ToString(tgtDict["file_path"]);
                if (tgtDict.ContainsKey("component_paths") && tgtDict["component_paths"] is System.Collections.ArrayList comps)
                {
                    cmd.Target.ComponentPaths = new List<string>();
                    foreach (var c in comps) cmd.Target.ComponentPaths.Add(Convert.ToString(c));
                }
            }

            if (dict.ContainsKey("payload") && dict["payload"] is Dictionary<string, object> pld)
                cmd.Payload = pld;

            // Fallback: derive from legacy "command" field
            if (dict.ContainsKey("command"))
            {
                string legacyCmd = Convert.ToString(dict["command"]);
                MapLegacyCommand(cmd, legacyCmd, dict);
            }

            return cmd;
        }

        public string ToJson()
        {
            var dict = new Dictionary<string, object>
            {
                ["schema_version"] = SchemaVersion,
                ["command_id"] = CommandId ?? "",
                ["source"] = Source ?? "",
                ["feature"] = Feature ?? "",
                ["action"] = Action ?? "",
                ["executor"] = Executor ?? ""
            };
            if (Target != null)
            {
                var tgtDict = new Dictionary<string, object>();
                if (Target.Scope != null) tgtDict["scope"] = Target.Scope;
                if (Target.FilePath != null) tgtDict["file_path"] = Target.FilePath;
                if (Target.ComponentPaths != null && Target.ComponentPaths.Count > 0)
                    tgtDict["component_paths"] = Target.ComponentPaths;
                dict["target"] = tgtDict;
            }
            if (Payload != null && Payload.Count > 0)
                dict["payload"] = Payload;
            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(dict);
        }

        private static void MapLegacyCommand(MechPilotCommand cmd, string legacyCmd, Dictionary<string, object> dict)
        {
            switch (legacyCmd)
            {
                case "local.read_properties":
                    cmd.Feature = "properties"; cmd.Action = "read"; cmd.Executor = "local";
                    cmd.Source = "cockpit";
                    break;
                case "refresh_context":
                    cmd.Feature = "properties"; cmd.Action = "read"; cmd.Executor = "local";
                    cmd.Source = "cockpit"; cmd.Payload["refresh_context"] = true;
                    break;
                case "property_fill":
                    cmd.Feature = "properties"; cmd.Action = "write"; cmd.Executor = "local";
                    cmd.Source = "cockpit";
                    break;
                case "property_check":
                    cmd.Feature = "properties"; cmd.Action = "check"; cmd.Executor = "local";
                    cmd.Source = "cockpit";
                    break;
                case "cockpit.ping":
                    cmd.Feature = "system"; cmd.Action = "ping"; cmd.Executor = "local";
                    cmd.Source = "cockpit";
                    break;
                case "window_close":
                case "window_minimize":
                case "window_maximize":
                    cmd.Feature = "window"; cmd.Action = legacyCmd.Replace("window_", "");
                    cmd.Executor = "local"; cmd.Source = "cockpit";
                    break;
                // AI commands — match frontend actual command names
                case "ai.material.search":
                    cmd.Feature = "material"; cmd.Action = "search"; cmd.Executor = "hindsight";
                    cmd.Source = "cockpit";
                    break;
                case "ai.drawing.review":
                    cmd.Feature = "drawing"; cmd.Action = "review"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
                case "ai.selection.recommend":
                    cmd.Feature = "selection"; cmd.Action = "recommend"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
                case "ai.design.calculate":
                    cmd.Feature = "design"; cmd.Action = "calculate"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
                case "ai.assistant.chat":
                    cmd.Feature = "assistant"; cmd.Action = "chat"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
                // Agent task lifecycle
                case "agent.task.submit":
                    cmd.Feature = "agent"; cmd.Action = "submit"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
                case "agent.task.poll":
                    cmd.Feature = "agent"; cmd.Action = "poll"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
                case "agent.task.result":
                    cmd.Feature = "agent"; cmd.Action = "result"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
                // Legacy aliases (fallback)
                case "ai.drawing_review":
                    cmd.Feature = "drawing"; cmd.Action = "review"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
                case "ai.selection":
                    cmd.Feature = "selection"; cmd.Action = "recommend"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
                case "ai.design_calc":
                    cmd.Feature = "design"; cmd.Action = "calculate"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
                case "agent.task.status":
                    cmd.Feature = "agent"; cmd.Action = "poll"; cmd.Executor = "hermes";
                    cmd.Source = "cockpit";
                    break;
            }
            if (dict.ContainsKey("request_id"))
                cmd.CommandId = Convert.ToString(dict["request_id"]);
            if (dict.ContainsKey("parameters") && dict["parameters"] is Dictionary<string, object> parms)
                cmd.Payload = parms;
        }
    }

    public class MechPilotCommandTarget
    {
        public string Scope { get; set; }
        public string FilePath { get; set; }
        public List<string> ComponentPaths { get; set; }
    }

    public class MechPilotResult
    {
        public string SchemaVersion { get; set; } = "mechpilot.result.v1";
        public string CommandId { get; set; }
        public string Command { get; set; }
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string ErrorCode { get; set; }
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
        public List<MechPilotWarning> Warnings { get; set; } = new List<MechPilotWarning>();
        public List<MechPilotArtifact> Artifacts { get; set; } = new List<MechPilotArtifact>();

        public string ToJson()
        {
            var dict = new Dictionary<string, object>
            {
                ["schema_version"] = SchemaVersion,
                ["command_id"] = CommandId ?? "",
                ["ok"] = Ok,
                ["message"] = Message ?? ""
            };
            if (!string.IsNullOrEmpty(Command)) dict["command"] = Command;
            if (Data != null && Data.Count > 0) dict["data"] = Data;
            if (Warnings != null && Warnings.Count > 0) dict["warnings"] = Warnings;
            if (Artifacts != null && Artifacts.Count > 0)
            {
                var artList = new List<Dictionary<string, object>>();
                foreach (var a in Artifacts) artList.Add(a.ToDict());
                dict["artifacts"] = artList;
            }
            if (!Ok)
            {
                dict["error"] = new Dictionary<string, string>
                {
                    ["code"] = ErrorCode ?? "EXECUTION_FAILED",
                    ["message"] = Message ?? "执行失败"
                };
            }
            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(dict);
        }

        public static MechPilotResult OkResult(string commandId, string message, Dictionary<string, object> data = null, string command = null)
        {
            return new MechPilotResult
            {
                CommandId = commandId,
                Command = command,
                Ok = true,
                Message = message ?? "执行完成",
                Data = data ?? new Dictionary<string, object>()
            };
        }

        public static MechPilotResult FailResult(string commandId, string message, string errorCode = null, string command = null)
        {
            return new MechPilotResult
            {
                CommandId = commandId,
                Command = command,
                Ok = false,
                Message = message ?? "执行失败",
                ErrorCode = errorCode ?? "EXECUTION_FAILED"
            };
        }
    }

    public class MechPilotWarning
    {
        public string Level { get; set; }
        public string Target { get; set; }
        public string Message { get; set; }
    }

    public class MechPilotArtifact
    {
        public string Type { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
        public string Description { get; set; }

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["type"] = Type ?? "",
                ["path"] = Path ?? "",
                ["size"] = Size,
                ["description"] = Description ?? ""
            };
        }
    }
}
