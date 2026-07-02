using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace SwAgentAddin
{
    /// <summary>
    /// CKP-005-03: MCP HTTP Adapter — 通过 sw-remote-mcp 的 JSON-RPC 接口调用 PDM 工具。
    /// 默认走真实 MCP（_useMock=false），mock 仅作为显式回退。
    /// MCP base URL: http://10.254.60.31:19090/mcp
    /// </summary>
    public class PdmLifecycleHelper
    {
        private readonly string _vaultName;
        private readonly bool _useMock;
        private readonly string _mcpBaseUrl;
        private readonly int _timeoutSeconds;
        private string _sessionId;

        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public PdmLifecycleHelper(string vaultName = null, bool useMock = false,
            string mcpBaseUrl = null, int timeoutSeconds = 30)
        {
            _vaultName = vaultName ?? "XtalPi-PDM";
            _useMock = useMock;
            _mcpBaseUrl = mcpBaseUrl ?? "http://10.254.60.31:19090/mcp";
            _timeoutSeconds = timeoutSeconds;
        }

        /// <summary>
        /// 获取（或刷新）MCP 会话
        /// </summary>
        private string EnsureMpcSession()
        {
            if (_useMock) return "mock-session";
            if (!string.IsNullOrEmpty(_sessionId)) return _sessionId;

            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "initialize",
                    ["params"] = new Dictionary<string, object>
                    {
                        ["protocolVersion"] = "2024-11-05",
                        ["capabilities"] = new Dictionary<string, object>(),
                        ["clientInfo"] = new Dictionary<string, string>
                        {
                            ["name"] = "Cockpit-PDM-Adapter",
                            ["version"] = "1.0"
                        }
                    },
                    ["id"] = 1
                };
                string json = Serializer.Serialize(payload);
                var req = (HttpWebRequest)WebRequest.Create(_mcpBaseUrl);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Accept = "application/json, text/event-stream";
                req.Timeout = _timeoutSeconds * 1000;

                using (var stream = req.GetRequestStream())
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.Write(json);
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    _sessionId = resp.Headers["mcp-session-id"];
                    return _sessionId ?? "";
                }
            }
            catch (Exception ex)
            {
                SwAgentAddin.WriteTrace("[PdmLifecycleHelper] EnsureMpcSession failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// MCP 通用调用
        /// </summary>
        private Dictionary<string, object> McpCall(string toolName, Dictionary<string, object> arguments)
        {
            if (_useMock) return null; // caller will fall back

            string session = EnsureMpcSession();
            if (string.IsNullOrEmpty(session))
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = "MCP 会话建立失败 — 请检查 MCP 服务是否运行 (http://10.254.60.31:19090/mcp)"
                };

            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "tools/call",
                    ["params"] = new Dictionary<string, object>
                    {
                        ["name"] = toolName,
                        ["arguments"] = arguments ?? new Dictionary<string, object>()
                    },
                    ["id"] = DateTime.Now.Ticks % 10000
                };
                string json = Serializer.Serialize(payload);
                var req = (HttpWebRequest)WebRequest.Create(_mcpBaseUrl);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Accept = "application/json, text/event-stream";
                req.Headers["Mcp-Session-Id"] = session;
                req.Timeout = _timeoutSeconds * 1000;

                using (var stream = req.GetRequestStream())
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.Write(json);
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    // SSE 解析: "data: <json>"
                    string dataLine = null;
                    foreach (var line in body.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("data: "))
                        {
                            dataLine = line.Substring(6);
                            break;
                        }
                    }
                    if (dataLine == null) dataLine = body;

                    var outer = Serializer.Deserialize<Dictionary<string, object>>(dataLine);
                    if (outer == null) return null;

                    if (outer.ContainsKey("result"))
                    {
                        var result = outer["result"] as Dictionary<string, object>;
                        if (result != null && result.ContainsKey("content"))
                        {
                            var content = result["content"] as object[];
                            if (content != null && content.Length > 0)
                            {
                                var contentItem = content[0] as Dictionary<string, object>;
                                if (contentItem != null && contentItem.ContainsKey("text"))
                                {
                                    string text = Convert.ToString(contentItem["text"]);
                                    return Serializer.Deserialize<Dictionary<string, object>>(text) ?? new Dictionary<string, object> { ["raw"] = text };
                                }
                            }
                        }
                    }

                    if (outer.ContainsKey("error"))
                    {
                        var err = outer["error"] as Dictionary<string, object>;
                        return new Dictionary<string, object>
                        {
                            ["success"] = false,
                            ["error"] = err != null && err.ContainsKey("message") ? Convert.ToString(err["message"]) : "MCP 调用错误"
                        };
                    }

                    return new Dictionary<string, object> { ["success"] = true, ["raw_result"] = outer };
                }
            }
            catch (WebException wex)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = "MCP 不可达 (HTTP " + (wex.Response != null ? ((HttpWebResponse)wex.Response).StatusCode.ToString() : "?") + "): " + wex.Message
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = "MCP 调用异常: " + ex.Message
                };
            }
        }

        // ── Public API ─────────────────────────────────

        /// <summary>
        /// 获取 PDM 工作流状态名（如「图纸生效」「机械图纸拟定中」）。
        /// 优先 pdm_get_file_status；若无 current_state_name 则回退 pdm_batch_get_variables(state_only)。
        /// </summary>
        public string GetWorkflowStateName(string filePath)
        {
            if (_useMock || string.IsNullOrWhiteSpace(filePath)) return "";

            var statusResult = McpCall("pdm_get_file_status", new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["vault_name"] = _vaultName
            });
            var data = UnwrapMcpPayload(statusResult);
            if (data == null) return "";

            string stateName = GetPayloadString(data, "current_state_name");
            if (!string.IsNullOrWhiteSpace(stateName)) return stateName;

            int fileId = GetPayloadInt(data, "file_id");
            if (fileId <= 0) return "";
            return FetchWorkflowStateByFileId(fileId);
        }

        private string FetchWorkflowStateByFileId(int fileId)
        {
            if (fileId <= 0) return "";
            var batchResult = McpCall("pdm_batch_get_variables", new Dictionary<string, object>
            {
                ["vault_name"] = _vaultName,
                ["file_ids"] = new ArrayList { fileId },
                ["state_only"] = true
            });
            var batchData = UnwrapMcpPayload(batchResult);
            if (batchData == null) return "";

            var files = batchData.ContainsKey("files") ? batchData["files"] as object[] : null;
            if (files == null && batchData["files"] is ArrayList fileList)
                files = fileList.Cast<object>().ToArray();
            if (files == null || files.Length == 0) return "";

            var first = files[0] as Dictionary<string, object>;
            if (first == null) return "";
            return GetPayloadString(first, "current_state_name");
        }

        public Dictionary<string, object> GetFileStatus(string filePath)
        {
            if (_useMock) return MockGetFileStatus(filePath);

            var result = McpCall("pdm_get_file_status", new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["vault_name"] = _vaultName
            });

            if (result == null) return MockGetFileStatus(filePath); // fallback
            var data = UnwrapMcpPayload(result);
            if (data == null) data = result;

            if (!(data.ContainsKey("success") && Convert.ToBoolean(data["success"])))
            {
                return new Dictionary<string, object>
                {
                    ["status"] = "error",
                    ["checked_out_by"] = "",
                    ["current_state_name"] = "",
                    ["error"] = data.ContainsKey("error") ? Convert.ToString(data["error"]) : "MCP 状态查询失败"
                };
            }

            bool lockedByMe = data.ContainsKey("locked_by_me") && Convert.ToBoolean(data["locked_by_me"]);
            string lockedByUser = GetPayloadString(data, "locked_by_user");
            string stateName = GetPayloadString(data, "current_state_name");
            if (string.IsNullOrWhiteSpace(stateName))
            {
                int fileId = GetPayloadInt(data, "file_id");
                if (fileId > 0)
                    stateName = FetchWorkflowStateByFileId(fileId);
            }

            var cockpitStatus = "checked_in";
            if (lockedByMe) cockpitStatus = "checked_out_by_me";
            else if (!string.IsNullOrEmpty(lockedByUser)) cockpitStatus = "checked_out_by_other";

            return new Dictionary<string, object>
            {
                ["status"] = cockpitStatus,
                ["checked_out_by"] = lockedByMe ? System.Environment.UserName : lockedByUser,
                ["current_state_name"] = stateName ?? "",
                ["error"] = ""
            };
        }

        private static Dictionary<string, object> UnwrapMcpPayload(Dictionary<string, object> payload)
        {
            if (payload == null) return null;
            if (payload.ContainsKey("data") && payload["data"] is Dictionary<string, object> inner)
                return inner;
            return payload;
        }

        private static string GetPayloadString(Dictionary<string, object> data, string key)
        {
            if (data == null || !data.ContainsKey(key) || data[key] == null) return "";
            return Convert.ToString(data[key]) ?? "";
        }

        private static int GetPayloadInt(Dictionary<string, object> data, string key)
        {
            if (data == null || !data.ContainsKey(key) || data[key] == null) return 0;
            try { return Convert.ToInt32(data[key]); }
            catch { return 0; }
        }

        public Dictionary<string, object> CheckoutFile(string filePath, string comment = "")
        {
            if (_useMock) return MockCheckoutFile(filePath);

            var result = McpCall("pdm_checkout_file", new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["comment"] = comment ?? "MechPilot 属性审核"
            });

            if (result == null) return MockCheckoutFile(filePath);
            return new Dictionary<string, object>
            {
                ["success"] = result.ContainsKey("success") && Convert.ToBoolean(result["success"]),
                ["error"] = result.ContainsKey("error") ? Convert.ToString(result["error"]) : "",
                ["already_checked_out"] = result.ContainsKey("already_checked_out") ? Convert.ToBoolean(result["already_checked_out"]) : false
            };
        }

        public Dictionary<string, object> CheckinFile(string filePath, string comment = "", string newVersion = "")
        {
            if (_useMock) return MockCheckinFile(filePath);

            var args = new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["comment"] = string.IsNullOrEmpty(comment) ? "MechPilot 属性审核" : comment
            };
            if (!string.IsNullOrEmpty(newVersion)) args["new_version"] = newVersion;

            var result = McpCall("pdm_checkin_file", args);

            if (result == null) return MockCheckinFile(filePath);
            return new Dictionary<string, object>
            {
                ["success"] = result.ContainsKey("success") && Convert.ToBoolean(result["success"]),
                ["error"] = result.ContainsKey("error") ? Convert.ToString(result["error"]) : ""
            };
        }

        #region Mock fallback (仅 _useMock=true 时)

        private Dictionary<string, object> MockGetFileStatus(string filePath)
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists)
                return new Dictionary<string, object>
                {
                    ["status"] = "not_in_vault",
                    ["checked_out_by"] = "",
                    ["error"] = "文件不存在"
                };

            bool isReadOnly = fi.IsReadOnly;

            return new Dictionary<string, object>
            {
                ["status"] = isReadOnly ? "checked_in" : "checked_out_by_me",
                ["checked_out_by"] = isReadOnly ? "" : System.Environment.UserName,
                ["error"] = ""
            };
        }

        private Dictionary<string, object> MockCheckoutFile(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists)
                    return new Dictionary<string, object> { ["success"] = false, ["error"] = "文件不存在" };

                if (!fi.IsReadOnly)
                    return new Dictionary<string, object> { ["success"] = true, ["already_checked_out"] = true };

                fi.IsReadOnly = false;
                return new Dictionary<string, object> { ["success"] = true, ["already_checked_out"] = false };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { ["success"] = false, ["error"] = ex.Message };
            }
        }

        private Dictionary<string, object> MockCheckinFile(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists)
                    return new Dictionary<string, object> { ["success"] = false, ["error"] = "文件不存在" };

                fi.IsReadOnly = true;
                return new Dictionary<string, object> { ["success"] = true };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { ["success"] = false, ["error"] = ex.Message };
            }
        }

        #endregion
    }
}
