using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SwAgentAddin
{
    /// <summary>
    /// AgentServer 配置 — Hermes 连接参数
    /// </summary>
    public class AgentServerConfig
    {
        public string Provider { get; set; } = "hermes";
        public string BaseUrl { get; set; } = "http://127.0.0.1:8080";
        public string AuthMode { get; set; } = "none";
        public string ApiKey { get; set; } = "";
        public string TaskEndpoint { get; set; } = "/api/v1/task";
        public string StatusEndpointTemplate { get; set; } = "/api/v1/task/{task_id}";
        public string ResultEndpointTemplate { get; set; } = "/api/v1/task/{task_id}/result";
        public string StreamEndpointTemplate { get; set; } = "/api/v1/task/{task_id}/stream";
        public string InvokeEndpoint { get; set; } = "/api/v1/agent/invoke";
        public string JobSubmitEndpoint { get; set; } = "/v1/runs";
        public string JobStatusEndpointTemplate { get; set; } = "/v1/runs/{job_id}";
        public int JobPollIntervalSeconds { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 120;
        public int PollIntervalSeconds { get; set; } = 3;
        public string ContextModeDefault { get; set; } = "summary";
        public string Model { get; set; } = "hermes-agent";

        public static AgentServerConfig FromJson(Dictionary<string, object> dict)
        {
            var cfg = new AgentServerConfig();
            if (dict == null) return cfg;
            if (dict.ContainsKey("provider")) cfg.Provider = Convert.ToString(dict["provider"]);
            if (dict.ContainsKey("base_url")) cfg.BaseUrl = Convert.ToString(dict["base_url"]);
            if (dict.ContainsKey("auth_mode")) cfg.AuthMode = Convert.ToString(dict["auth_mode"]);
            if (dict.ContainsKey("api_key")) cfg.ApiKey = Convert.ToString(dict["api_key"]);
            if (dict.ContainsKey("task_endpoint")) cfg.TaskEndpoint = Convert.ToString(dict["task_endpoint"]);
            if (dict.ContainsKey("status_endpoint_template")) cfg.StatusEndpointTemplate = Convert.ToString(dict["status_endpoint_template"]);
            if (dict.ContainsKey("result_endpoint_template")) cfg.ResultEndpointTemplate = Convert.ToString(dict["result_endpoint_template"]);
            if (dict.ContainsKey("stream_endpoint_template")) cfg.StreamEndpointTemplate = Convert.ToString(dict["stream_endpoint_template"]);
            if (dict.ContainsKey("invoke_endpoint")) cfg.InvokeEndpoint = Convert.ToString(dict["invoke_endpoint"]);
            if (dict.ContainsKey("job_submit_endpoint")) cfg.JobSubmitEndpoint = Convert.ToString(dict["job_submit_endpoint"]);
            if (dict.ContainsKey("job_status_endpoint_template")) cfg.JobStatusEndpointTemplate = Convert.ToString(dict["job_status_endpoint_template"]);
            if (dict.ContainsKey("timeout_seconds")) cfg.TimeoutSeconds = Convert.ToInt32(dict["timeout_seconds"]);
            if (dict.ContainsKey("poll_interval_seconds")) cfg.PollIntervalSeconds = Convert.ToInt32(dict["poll_interval_seconds"]);
            cfg.JobPollIntervalSeconds = cfg.PollIntervalSeconds;
            if (dict.ContainsKey("job_poll_interval_seconds")) cfg.JobPollIntervalSeconds = Convert.ToInt32(dict["job_poll_interval_seconds"]);
            if (dict.ContainsKey("context_mode_default")) cfg.ContextModeDefault = Convert.ToString(dict["context_mode_default"]);
            if (dict.ContainsKey("model")) cfg.Model = Convert.ToString(dict["model"]);
            return cfg;
        }

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["provider"] = Provider,
                ["base_url"] = BaseUrl,
                ["auth_mode"] = AuthMode,
                ["api_key"] = ApiKey,
                ["task_endpoint"] = TaskEndpoint,
                ["status_endpoint_template"] = StatusEndpointTemplate,
                ["result_endpoint_template"] = ResultEndpointTemplate,
                ["stream_endpoint_template"] = StreamEndpointTemplate,
                ["invoke_endpoint"] = InvokeEndpoint,
                ["job_submit_endpoint"] = JobSubmitEndpoint,
                ["job_status_endpoint_template"] = JobStatusEndpointTemplate,
                ["timeout_seconds"] = TimeoutSeconds,
                ["poll_interval_seconds"] = PollIntervalSeconds,
                ["job_poll_interval_seconds"] = JobPollIntervalSeconds,
                ["context_mode_default"] = ContextModeDefault,
                ["model"] = Model,
            };
        }
    }

    /// <summary>
    /// Hermes Agent 客户端 — 异步 invoke / task / job
    /// </summary>
    internal class HermesClient
    {
        private readonly AgentServerConfig _server;
        private readonly HttpClient _http;
        private readonly Action<string> _log;

        public HermesClient(AgentServerConfig server, Action<string> log)
        {
            _server = server;
            _log = log;
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(server.TimeoutSeconds);
        }

        /// <summary>
        /// 异步 invoke — 用于简单问答、ping 等
        /// </summary>
        public async Task<string> InvokeAgentAsync(string action, object payload, string contextMode = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string url = _server.BaseUrl.TrimEnd('/') + _server.InvokeEndpoint;
            string requestId = Guid.NewGuid().ToString("N").Substring(0, 8);
            bool isOpenAiFormat = _server.InvokeEndpoint.Contains("chat/completions");

            try
            {
                string json;

                if (isOpenAiFormat)
                {
                    // Convert to OpenAI /v1/chat/completions format
                    string systemMsg = string.Format("你是 MechPilot 的 AI 助手。当前操作：{0}。上下文模式：{1}",
                        action, contextMode ?? _server.ContextModeDefault);

                    string userMsg = BuildUserMessageFromPayload(payload);

                    var openAiBody = new Dictionary<string, object>
                    {
                        ["model"] = _server.Model,
                        ["messages"] = new[]
                        {
                            new Dictionary<string, string> { ["role"] = "system", ["content"] = systemMsg },
                            new Dictionary<string, string> { ["role"] = "user", ["content"] = userMsg }
                        }
                    };
                    json = new JavaScriptSerializer().Serialize(openAiBody);
                }
                else
                {
                    // Original MechPilot format
                    var body = new Dictionary<string, object>
                    {
                        ["action"] = action,
                        ["request_id"] = requestId,
                        ["payload"] = payload,
                        ["context_mode"] = contextMode ?? _server.ContextModeDefault
                    };
                    json = new JavaScriptSerializer().Serialize(body);
                }

                _log(string.Format("[Hermes] invoke action={0} req={1} url={2}", action, requestId, url));

                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    AddAuthHeader(request);
                    var response = await _http.SendAsync(request).ConfigureAwait(false);
                    string httpResult = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    sw.Stop();
                    _log(string.Format("[Hermes] invoke action={0} req={1} status={2} elapsed={3:F0}ms",
                        action, requestId, (int)response.StatusCode, sw.Elapsed.TotalMilliseconds));

                    if (response.IsSuccessStatusCode)
                    {
                        if (isOpenAiFormat)
                            return ConvertOpenAiResultToMechPilot(httpResult, action, requestId);
                        return httpResult;
                    }

                    return MakeOfflineResult(action, requestId,
                        string.Format("Hermes returned {0}: {1}", (int)response.StatusCode, Shorten(httpResult, 200)));
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _log(string.Format("[Hermes] invoke action={0} req={1} FAILED elapsed={2:F0}ms err={3}",
                    action, requestId, sw.Elapsed.TotalMilliseconds, ex.Message));
                return MakeOfflineResult(action, requestId,
                    string.Format("Hermes offline or request failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// 兼容旧调用：同步包装（仅用于非 STA 场景）
        /// </summary>
        [Obsolete("Use InvokeAgentAsync instead to avoid blocking STA thread")]
        public string InvokeAgent(string action, object payload, string contextMode = null)
        {
            return Task.Run(() => InvokeAgentAsync(action, payload, contextMode)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 异步长任务：提交任务，返回 task_id
        /// </summary>
        public async Task<Dictionary<string, object>> SubmitTaskAsync(string taskType, object payload, string contextMode = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string url = _server.BaseUrl.TrimEnd('/') + _server.TaskEndpoint;
            string requestId = Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                var body = new Dictionary<string, object>
                {
                    ["task_type"] = taskType,
                    ["request_id"] = requestId,
                    ["payload"] = payload,
                    ["context_mode"] = contextMode ?? _server.ContextModeDefault,
                    ["client"] = new Dictionary<string, object>
                    {
                        ["name"] = "MechPilot",
                        ["version"] = "1.0.0",
                        ["machine"] = Environment.MachineName
                    }
                };

                string json = new JavaScriptSerializer().Serialize(body);
                _log(string.Format("[Hermes] submit task_type={0} req={1} url={2}", taskType, requestId, url));

                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    AddAuthHeader(request);
                    var response = await _http.SendAsync(request).ConfigureAwait(false);
                    string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    sw.Stop();
                    _log(string.Format("[Hermes] submit task_type={0} req={1} status={2} elapsed={3:F0}ms",
                        taskType, requestId, (int)response.StatusCode, sw.Elapsed.TotalMilliseconds));

                    if (response.IsSuccessStatusCode)
                    {
                        var parsed = DeserializeDict(result);
                        return new Dictionary<string, object>
                        {
                            ["success"] = true,
                            ["request_id"] = requestId,
                            ["task_id"] = GetString(parsed, "task_id"),
                            ["raw"] = result
                        };
                    }

                    return new Dictionary<string, object>
                    {
                        ["success"] = false,
                        ["request_id"] = requestId,
                        ["error"] = string.Format("Hermes returned {0}: {1}", (int)response.StatusCode, Shorten(result, 200))
                    };
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _log(string.Format("[Hermes] submit task_type={0} req={1} FAILED elapsed={2:F0}ms err={3}",
                    taskType, requestId, sw.Elapsed.TotalMilliseconds, ex.Message));
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["request_id"] = requestId,
                    ["error"] = string.Format("Hermes offline or submit failed: {0}", ex.Message)
                };
            }
        }

        /// <summary>
        /// 兼容旧调用：同步包装
        /// </summary>
        [Obsolete("Use SubmitTaskAsync instead to avoid blocking STA thread")]
        public Dictionary<string, object> SubmitTask(string taskType, object payload, string contextMode = null)
        {
            return Task.Run(() => SubmitTaskAsync(taskType, payload, contextMode)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Queue-style Hermes job submit. This is async so callers can avoid blocking the SolidWorks UI.
        /// </summary>
        public async Task<Dictionary<string, object>> SubmitJobAsync(string jobType, object payload, string contextMode = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string url = _server.BaseUrl.TrimEnd('/') + (string.IsNullOrWhiteSpace(_server.JobSubmitEndpoint) ? "/api/jobs" : _server.JobSubmitEndpoint);
            string requestId = Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                Dictionary<string, object> body;
                bool isRunsEndpoint = url.IndexOf("/v1/runs", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isRunsEndpoint)
                {
                    var metadata = new Dictionary<string, object>
                    {
                        ["source"] = "MechPilot",
                        ["job_type"] = string.IsNullOrWhiteSpace(jobType) ? "general" : jobType,
                        ["request_id"] = requestId,
                        ["context_mode"] = contextMode ?? _server.ContextModeDefault,
                        ["machine"] = Environment.MachineName
                    };
                    if (string.Equals(jobType, "assistant.chat", StringComparison.OrdinalIgnoreCase))
                    {
                        string chatMessage = ExtractPayloadString(payload, "message");
                        if (!string.IsNullOrWhiteSpace(chatMessage))
                            metadata["message"] = chatMessage.Trim();
                    }
                    body = new Dictionary<string, object>
                    {
                        ["input"] = BuildRunInput(jobType, payload),
                        ["instructions"] = string.Equals(jobType, "assistant.chat", StringComparison.OrdinalIgnoreCase)
                            ? "You are MechPilot AI assistant inside SolidWorks Cockpit. Answer the user message in concise Chinese."
                            : "You are MechPilot Agent. Analyze the SolidWorks/Cockpit task context and return concise Chinese findings and recommended actions.",
                        ["metadata"] = metadata
                    };
                }
                else
                {
                    body = new Dictionary<string, object>
                    {
                        ["job_type"] = string.IsNullOrWhiteSpace(jobType) ? "general" : jobType,
                        ["request_id"] = requestId,
                        ["payload"] = payload,
                        ["context_mode"] = contextMode ?? _server.ContextModeDefault,
                        ["client"] = new Dictionary<string, object>
                        {
                            ["name"] = "MechPilot",
                            ["version"] = "1.0.0",
                            ["machine"] = Environment.MachineName
                        }
                    };
                }

                string json = new JavaScriptSerializer().Serialize(body);
                _log(string.Format("[Hermes] submit job_type={0} req={1} url={2}", jobType, requestId, url));

                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    AddAuthHeader(request);
                    var response = await _http.SendAsync(request).ConfigureAwait(false);
                    string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    sw.Stop();
                    _log(string.Format("[Hermes] submit job_type={0} req={1} status={2} elapsed={3:F0}ms",
                        jobType, requestId, (int)response.StatusCode, sw.Elapsed.TotalMilliseconds));

                    if (response.IsSuccessStatusCode)
                    {
                        var parsed = DeserializeDict(result);
                        string jobId = GetString(parsed, "job_id");
                        if (string.IsNullOrEmpty(jobId)) jobId = GetString(parsed, "run_id");
                        if (string.IsNullOrEmpty(jobId)) jobId = GetString(parsed, "task_id");
                        if (string.IsNullOrEmpty(jobId)) jobId = GetString(parsed, "id");

                        return MakeJobResult(true, requestId, null, new Dictionary<string, object>
                        {
                            ["accepted"] = GetValue(parsed, "accepted", true),
                            ["job_id"] = jobId,
                            ["status"] = GetValue(parsed, "status", "queued"),
                            ["queue_position"] = GetValue(parsed, "queue_position", null),
                            ["estimated_wait_seconds"] = GetValue(parsed, "estimated_wait_seconds", null),
                            ["poll_interval_seconds"] = _server.JobPollIntervalSeconds,
                            ["raw"] = result
                        });
                    }

                    return MakeJobResult(false, requestId,
                        string.Format("Hermes returned {0}: {1}", (int)response.StatusCode, Shorten(result, 200)),
                        new Dictionary<string, object>
                        {
                            ["accepted"] = false,
                            ["job_id"] = "",
                            ["status"] = "failed",
                            ["queue_position"] = null,
                            ["estimated_wait_seconds"] = null
                        });
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _log(string.Format("[Hermes] submit job_type={0} req={1} FAILED elapsed={2:F0}ms err={3}",
                    jobType, requestId, sw.Elapsed.TotalMilliseconds, ex.Message));
                return MakeJobResult(false, requestId,
                    string.Format("Hermes offline or job submit failed: {0}", ex.Message),
                    new Dictionary<string, object>
                    {
                        ["accepted"] = false,
                        ["job_id"] = "",
                        ["status"] = "offline",
                        ["queue_position"] = null,
                        ["estimated_wait_seconds"] = null
                    },
                    true);
            }
        }

        /// <summary>
        /// Queue-style Hermes job poll. Network failures are returned as structured data, not thrown.
        /// </summary>
        public async Task<Dictionary<string, object>> PollJobAsync(string jobId)
        {
            string safeJobId = jobId ?? "";
            string template = string.IsNullOrWhiteSpace(_server.JobStatusEndpointTemplate)
                ? "/api/jobs/{job_id}"
                : _server.JobStatusEndpointTemplate;
            string endpoint = template
                .Replace("{job_id}", Uri.EscapeDataString(safeJobId))
                .Replace("{task_id}", Uri.EscapeDataString(safeJobId));
            string url = _server.BaseUrl.TrimEnd('/') + endpoint;

            try
            {
                _log(string.Format("[Hermes] poll job_id={0} url={1}", safeJobId, url));
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    AddAuthHeader(request);
                    var response = await _http.SendAsync(request).ConfigureAwait(false);
                    string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var parsed = DeserializeDict(result);
                        string returnedJobId = GetString(parsed, "job_id");
                        if (string.IsNullOrEmpty(returnedJobId)) returnedJobId = GetString(parsed, "run_id");
                        if (string.IsNullOrEmpty(returnedJobId)) returnedJobId = GetString(parsed, "task_id");
                        if (string.IsNullOrEmpty(returnedJobId)) returnedJobId = safeJobId;

                        var data = new Dictionary<string, object>
                        {
                            ["accepted"] = true,
                            ["job_id"] = returnedJobId,
                            ["status"] = GetValue(parsed, "status", "unknown"),
                            ["queue_position"] = GetValue(parsed, "queue_position", null),
                            ["estimated_wait_seconds"] = GetValue(parsed, "estimated_wait_seconds", null),
                            ["poll_interval_seconds"] = _server.JobPollIntervalSeconds
                        };

                        // Extract AI response content for chat and other jobs
                        if (parsed.ContainsKey("output")) data["output"] = parsed["output"];
                        if (parsed.ContainsKey("content")) data["content"] = parsed["content"];
                        if (parsed.ContainsKey("result")) data["result"] = parsed["result"];
                        if (parsed.ContainsKey("results")) data["results"] = parsed["results"];
                        if (parsed.ContainsKey("message")) data["message"] = parsed["message"];
                        if (parsed.ContainsKey("completed_items")) data["completed_items"] = parsed["completed_items"];
                        if (parsed.ContainsKey("failed_items")) data["failed_items"] = parsed["failed_items"];
                        if (parsed.ContainsKey("total_items")) data["total_items"] = parsed["total_items"];
                        if (parsed.ContainsKey("progress_percent")) data["progress_percent"] = parsed["progress_percent"];
                        if (parsed.ContainsKey("current_stage")) data["current_stage"] = parsed["current_stage"];

                        return MakeJobResult(true, null, null, data);
                    }

                    return MakeJobResult(false, null,
                        string.Format("Poll returned {0}: {1}", (int)response.StatusCode, Shorten(result, 200)),
                        new Dictionary<string, object>
                        {
                            ["accepted"] = false,
                            ["job_id"] = safeJobId,
                            ["status"] = "failed",
                            ["queue_position"] = null,
                            ["estimated_wait_seconds"] = null
                        });
                }
            }
            catch (Exception ex)
            {
                _log(string.Format("[Hermes] poll job_id={0} FAILED err={1}", safeJobId, ex.Message));
                return MakeJobResult(false, null,
                    string.Format("Hermes offline or job poll failed: {0}", ex.Message),
                    new Dictionary<string, object>
                    {
                        ["accepted"] = false,
                        ["job_id"] = safeJobId,
                        ["status"] = "offline",
                        ["queue_position"] = null,
                        ["estimated_wait_seconds"] = null
                    },
                    true);
            }
        }

        /// <summary>
        /// 异步轮询任务状态
        /// </summary>
        public async Task<Dictionary<string, object>> PollTaskStatusAsync(string taskId)
        {
            string url = _server.BaseUrl.TrimEnd('/') +
                _server.StatusEndpointTemplate.Replace("{task_id}", taskId);

            try
            {
                _log(string.Format("[Hermes] poll task_id={0} url={1}", taskId, url));
                var response = await _http.GetAsync(url).ConfigureAwait(false);
                string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var parsed = DeserializeDict(result);
                    return new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["task_id"] = taskId,
                        ["status"] = GetValue(parsed, "status", "unknown"),
                        ["raw"] = result
                    };
                }

                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["task_id"] = taskId,
                    ["error"] = string.Format("Poll returned {0}", (int)response.StatusCode)
                };
            }
            catch (Exception ex)
            {
                _log(string.Format("[Hermes] poll task_id={0} FAILED err={1}", taskId, ex.Message));
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["task_id"] = taskId,
                    ["error"] = string.Format("Hermes offline or poll failed: {0}", ex.Message)
                };
            }
        }

        /// <summary>
        /// 兼容旧调用：同步包装
        /// </summary>
        [Obsolete("Use PollTaskStatusAsync instead to avoid blocking STA thread")]
        public Dictionary<string, object> PollTaskStatus(string taskId)
        {
            return Task.Run(() => PollTaskStatusAsync(taskId)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 异步获取任务结果
        /// </summary>
        public async Task<Dictionary<string, object>> GetTaskResultAsync(string taskId)
        {
            string url = _server.BaseUrl.TrimEnd('/') +
                _server.ResultEndpointTemplate.Replace("{task_id}", taskId);

            try
            {
                _log(string.Format("[Hermes] result task_id={0} url={1}", taskId, url));
                var response = await _http.GetAsync(url).ConfigureAwait(false);
                string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var parsed = DeserializeDict(result);
                    return new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["task_id"] = taskId,
                        ["data"] = parsed,
                        ["raw"] = result
                    };
                }

                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["task_id"] = taskId,
                    ["error"] = string.Format("GetResult returned {0}", (int)response.StatusCode)
                };
            }
            catch (Exception ex)
            {
                _log(string.Format("[Hermes] result task_id={0} FAILED err={1}", taskId, ex.Message));
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["task_id"] = taskId,
                    ["error"] = string.Format("Hermes offline or get-result failed: {0}", ex.Message)
                };
            }
        }

        /// <summary>
        /// 兼容旧调用：同步包装
        /// </summary>
        [Obsolete("Use GetTaskResultAsync instead to avoid blocking STA thread")]
        public Dictionary<string, object> GetTaskResult(string taskId)
        {
            return Task.Run(() => GetTaskResultAsync(taskId)).GetAwaiter().GetResult();
        }


        private static string BuildRunInput(string jobType, object payload)
        {
            if (string.Equals(jobType, "assistant.chat", StringComparison.OrdinalIgnoreCase))
            {
                string message = ExtractPayloadString(payload, "message");
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return "User message:\n" + message.Trim()
                        + "\n\nReply in concise Chinese. Optional Cockpit context JSON follows:\n"
                        + SerializePayloadJson(payload);
                }
            }

            return "MechPilot Cockpit submitted an immediate execution task.\n"
                + "Task type: " + (string.IsNullOrWhiteSpace(jobType) ? "general" : jobType) + "\n"
                + "Analyze/review the following JSON context and return concise Chinese findings and recommended actions.\n"
                + SerializePayloadJson(payload);
        }

        private static string ExtractPayloadString(object payload, string key)
        {
            if (payload is Dictionary<string, object> dict && dict.ContainsKey(key))
                return Convert.ToString(dict[key]) ?? "";
            return "";
        }

        private static string SerializePayloadJson(object payload)
        {
            var serializer = new JavaScriptSerializer();
            try
            {
                return serializer.Serialize(payload ?? new Dictionary<string, object>());
            }
            catch
            {
                return Convert.ToString(payload) ?? "";
            }
        }

        private void AddAuthHeader(StringContent content)
        {
            if (!string.IsNullOrEmpty(_server.ApiKey) &&
                !string.Equals(_server.AuthMode, "none", StringComparison.OrdinalIgnoreCase))
            {
                content.Headers.Remove("Authorization");
                content.Headers.TryAddWithoutValidation("Authorization",
                    string.Equals(_server.AuthMode, "bearer", StringComparison.OrdinalIgnoreCase)
                        ? "Bearer " + _server.ApiKey
                        : _server.ApiKey);
            }
        }

        private void AddAuthHeader(HttpRequestMessage request)
        {
            if (!string.IsNullOrEmpty(_server.ApiKey) &&
                !string.Equals(_server.AuthMode, "none", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Remove("Authorization");
                request.Headers.TryAddWithoutValidation("Authorization",
                    string.Equals(_server.AuthMode, "bearer", StringComparison.OrdinalIgnoreCase)
                        ? "Bearer " + _server.ApiKey
                        : _server.ApiKey);
            }
        }

        private static Dictionary<string, object> MakeJobResult(bool success, string requestId, string errorMsg,
            Dictionary<string, object> data, bool offline = false)
        {
            var result = new Dictionary<string, object>
            {
                ["success"] = success,
                ["data"] = data ?? new Dictionary<string, object>()
            };
            if (!string.IsNullOrEmpty(requestId)) result["request_id"] = requestId;
            if (offline) result["offline"] = true;
            if (!success)
            {
                result["error"] = new Dictionary<string, string>
                {
                    ["code"] = offline ? "HERMES_OFFLINE" : "HERMES_JOB_FAILED",
                    ["message"] = errorMsg ?? "Hermes job request failed"
                };
            }
            return result;
        }

        private static Dictionary<string, object> DeserializeDict(string json)
        {
            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static object GetValue(Dictionary<string, object> dict, string key, object fallback)
        {
            if (dict != null && dict.ContainsKey(key)) return dict[key];
            return fallback;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            object value = GetValue(dict, key, "");
            return value == null ? "" : Convert.ToString(value);
        }

        private static string MakeOfflineResult(string action, string requestId, string errorMsg)
        {
            var serializer = new JavaScriptSerializer();
            return serializer.Serialize(new Dictionary<string, object>
            {
                ["request_id"] = requestId,
                ["success"] = false,
                ["offline"] = true,
                ["action"] = action,
                ["error"] = new Dictionary<string, string>
                {
                    ["code"] = "HERMES_OFFLINE",
                    ["message"] = errorMsg
                }
            });
        }

        /// <summary>
        /// 从 MechPilot payload 构建 OpenAI user message
        /// </summary>
        private static string BuildUserMessageFromPayload(object payload)
        {
            var sb = new StringBuilder();

            if (payload is Dictionary<string, object> dict)
            {
                if (dict.ContainsKey("context"))
                {
                    var ctx = dict["context"] as Dictionary<string, object>;
                    if (ctx != null)
                    {
                        if (ctx.ContainsKey("document"))
                        {
                            var doc = ctx["document"] as Dictionary<string, object>;
                            if (doc != null)
                            {
                                string title = doc.ContainsKey("title") ? Convert.ToString(doc["title"]) : "";
                                string docType = doc.ContainsKey("doc_type") ? Convert.ToString(doc["doc_type"]) : "";
                                if (!string.IsNullOrEmpty(title))
                                    sb.AppendLine("当前文档：" + title + " (" + docType + ")");
                            }
                        }
                        if (ctx.ContainsKey("summary"))
                        {
                            var summary = ctx["summary"] as Dictionary<string, object>;
                            if (summary != null && summary.ContainsKey("total_components"))
                                sb.AppendLine("零部件总数：" + Convert.ToString(summary["total_components"]));
                        }
                    }
                }

                if (dict.ContainsKey("payload"))
                {
                    var inner = dict["payload"];
                    if (inner is Dictionary<string, object> innerDict)
                    {
                        if (innerDict.ContainsKey("query"))
                            sb.AppendLine("查询：" + Convert.ToString(innerDict["query"]));
                        if (innerDict.ContainsKey("message"))
                            sb.AppendLine(Convert.ToString(innerDict["message"]));
                    }
                    else if (inner is string s)
                        sb.AppendLine(s);
                }
            }
            else if (payload is string s)
                sb.AppendLine(s);

            if (sb.Length == 0)
                sb.AppendLine("请帮助处理当前 SolidWorks 文档的相关任务。");

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 将 OpenAI /v1/chat/completions 响应转换为 MechPilot 格式
        /// </summary>
        private static string ConvertOpenAiResultToMechPilot(string openAiJson, string action, string requestId)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var parsed = serializer.Deserialize<Dictionary<string, object>>(openAiJson);
                string aiContent = "";

                if (parsed != null && parsed.ContainsKey("choices"))
                {
                    var choices = parsed["choices"] as System.Collections.ArrayList;
                    if (choices != null && choices.Count > 0)
                    {
                        var choice = choices[0] as Dictionary<string, object>;
                        if (choice != null && choice.ContainsKey("message"))
                        {
                            var msg = choice["message"] as Dictionary<string, object>;
                            if (msg != null && msg.ContainsKey("content"))
                                aiContent = Convert.ToString(msg["content"]);
                        }
                    }
                }

                return serializer.Serialize(new Dictionary<string, object>
                {
                    ["request_id"] = requestId,
                    ["success"] = true,
                    ["action"] = action,
                    ["data"] = new Dictionary<string, object>
                    {
                        ["source"] = "hermes",
                        ["content"] = aiContent,
                        ["model"] = parsed != null && parsed.ContainsKey("model") ? parsed["model"] : "unknown"
                    }
                });
            }
            catch
            {
                // If parsing fails, return raw as content
                return new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                {
                    ["request_id"] = requestId,
                    ["success"] = true,
                    ["action"] = action,
                    ["data"] = new Dictionary<string, object>
                    {
                        ["source"] = "hermes",
                        ["content"] = openAiJson,
                        ["model"] = "unknown"
                    }
                });
            }
        }


        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
            return text.Substring(0, max) + "...";
        }
    }
}
