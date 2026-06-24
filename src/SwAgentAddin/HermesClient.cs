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
        public string JobSubmitEndpoint { get; set; } = "/api/jobs";
        public string JobStatusEndpointTemplate { get; set; } = "/api/jobs/{job_id}";
        public int JobPollIntervalSeconds { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 120;
        public int PollIntervalSeconds { get; set; } = 3;
        public string ContextModeDefault { get; set; } = "summary";

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
                ["context_mode_default"] = ContextModeDefault
            };
        }
    }

    /// <summary>
    /// Hermes Agent 客户端 — 同步 invoke / 异步 task submit+poll+result
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
        /// 同步 invoke — 用于简单问答、ping 等
        /// </summary>
        public string InvokeAgent(string action, object payload, string contextMode = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string url = _server.BaseUrl.TrimEnd('/') + _server.InvokeEndpoint;
            string requestId = Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                var body = new Dictionary<string, object>
                {
                    ["action"] = action,
                    ["request_id"] = requestId,
                    ["payload"] = payload,
                    ["context_mode"] = contextMode ?? _server.ContextModeDefault
                };

                string json = new JavaScriptSerializer().Serialize(body);
                _log(string.Format("[Hermes] invoke action={0} req={1} url={2}", action, requestId, url));

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                AddAuthHeader(content);
                var response = _http.PostAsync(url, content).GetAwaiter().GetResult();
                string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                sw.Stop();
                _log(string.Format("[Hermes] invoke action={0} req={1} status={2} elapsed={3:F0}ms",
                    action, requestId, (int)response.StatusCode, sw.Elapsed.TotalMilliseconds));

                if (response.IsSuccessStatusCode)
                    return result;

                return MakeOfflineResult(action, requestId,
                    string.Format("Hermes returned {0}: {1}", (int)response.StatusCode, Shorten(result, 200)));
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
        /// 长任务：提交任务，返回 task_id
        /// </summary>
        public Dictionary<string, object> SubmitTask(string taskType, object payload, string contextMode = null)
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

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                AddAuthHeader(content);
                var response = _http.PostAsync(url, content).GetAwaiter().GetResult();
                string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                sw.Stop();
                _log(string.Format("[Hermes] submit task_type={0} req={1} status={2} elapsed={3:F0}ms",
                    taskType, requestId, (int)response.StatusCode, sw.Elapsed.TotalMilliseconds));

                if (response.IsSuccessStatusCode)
                {
                    var serializer = new JavaScriptSerializer();
                    var parsed = serializer.Deserialize<Dictionary<string, object>>(result);
                    return new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["request_id"] = requestId,
                        ["task_id"] = parsed != null && parsed.ContainsKey("task_id") ? parsed["task_id"] : "",
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
        /// Queue-style Hermes job submit. This is async so callers can avoid blocking the SolidWorks UI.
        /// </summary>
        public async Task<Dictionary<string, object>> SubmitJobAsync(string jobType, object payload, string contextMode = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string url = _server.BaseUrl.TrimEnd('/') + (string.IsNullOrWhiteSpace(_server.JobSubmitEndpoint) ? "/api/jobs" : _server.JobSubmitEndpoint);
            string requestId = Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                var body = new Dictionary<string, object>
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
                        if (string.IsNullOrEmpty(returnedJobId)) returnedJobId = GetString(parsed, "task_id");
                        if (string.IsNullOrEmpty(returnedJobId)) returnedJobId = safeJobId;

                        return MakeJobResult(true, null, null, new Dictionary<string, object>
                        {
                            ["accepted"] = true,
                            ["job_id"] = returnedJobId,
                            ["status"] = GetValue(parsed, "status", "unknown"),
                            ["queue_position"] = GetValue(parsed, "queue_position", null),
                            ["estimated_wait_seconds"] = GetValue(parsed, "estimated_wait_seconds", null),
                            ["poll_interval_seconds"] = _server.JobPollIntervalSeconds,
                            ["raw"] = result
                        });
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
        /// 轮询任务状态
        /// </summary>
        public Dictionary<string, object> PollTaskStatus(string taskId)
        {
            string url = _server.BaseUrl.TrimEnd('/') +
                _server.StatusEndpointTemplate.Replace("{task_id}", taskId);

            try
            {
                _log(string.Format("[Hermes] poll task_id={0} url={1}", taskId, url));
                var response = _http.GetAsync(url).GetAwaiter().GetResult();
                string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    var serializer = new JavaScriptSerializer();
                    var parsed = serializer.Deserialize<Dictionary<string, object>>(result);
                    return new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["task_id"] = taskId,
                        ["status"] = parsed != null && parsed.ContainsKey("status") ? parsed["status"] : "unknown",
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
        /// 获取任务结果
        /// </summary>
        public Dictionary<string, object> GetTaskResult(string taskId)
        {
            string url = _server.BaseUrl.TrimEnd('/') +
                _server.ResultEndpointTemplate.Replace("{task_id}", taskId);

            try
            {
                _log(string.Format("[Hermes] result task_id={0} url={1}", taskId, url));
                var response = _http.GetAsync(url).GetAwaiter().GetResult();
                string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    var serializer = new JavaScriptSerializer();
                    var parsed = serializer.Deserialize<Dictionary<string, object>>(result);
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

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
            return text.Substring(0, max) + "...";
        }
    }
}
