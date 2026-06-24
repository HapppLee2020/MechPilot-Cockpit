using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Web.Script.Serialization;

namespace SwAgentAddin
{
    /// <summary>
    /// Hindsight RAG 配置
    /// </summary>
    public class HindsightConfig
    {
        public bool Enabled { get; set; } = false;
        public string BaseUrl { get; set; } = "http://192.168.31.115:8888";
        public string ApiKey { get; set; } = "";
        public string Bank { get; set; } = "davis";
        public string SourceDbPath { get; set; } = "";
        public int TopK { get; set; } = 5;
        public double ScoreThreshold { get; set; } = 0.35;
        public int TimeoutSeconds { get; set; } = 30;
        public bool ExplainWithHermes { get; set; } = false;

        public static HindsightConfig FromJson(Dictionary<string, object> dict)
        {
            var cfg = new HindsightConfig();
            if (dict == null) return cfg;
            if (dict.ContainsKey("enabled")) cfg.Enabled = Convert.ToBoolean(dict["enabled"]);
            if (dict.ContainsKey("base_url")) cfg.BaseUrl = Convert.ToString(dict["base_url"]);
            if (dict.ContainsKey("api_key")) cfg.ApiKey = Convert.ToString(dict["api_key"]);
            if (dict.ContainsKey("bank")) cfg.Bank = Convert.ToString(dict["bank"]);
            if (dict.ContainsKey("source_db_path")) cfg.SourceDbPath = Convert.ToString(dict["source_db_path"]);
            if (dict.ContainsKey("top_k")) cfg.TopK = Convert.ToInt32(dict["top_k"]);
            if (dict.ContainsKey("score_threshold")) cfg.ScoreThreshold = Convert.ToDouble(dict["score_threshold"]);
            if (dict.ContainsKey("timeout_seconds")) cfg.TimeoutSeconds = Convert.ToInt32(dict["timeout_seconds"]);
            if (dict.ContainsKey("explain_with_hermes")) cfg.ExplainWithHermes = Convert.ToBoolean(dict["explain_with_hermes"]);
            return cfg;
        }

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["enabled"] = Enabled,
                ["base_url"] = BaseUrl,
                ["api_key"] = ApiKey,
                ["bank"] = Bank,
                ["source_db_path"] = SourceDbPath,
                ["top_k"] = TopK,
                ["score_threshold"] = ScoreThreshold,
                ["timeout_seconds"] = TimeoutSeconds,
                ["explain_with_hermes"] = ExplainWithHermes
            };
        }
    }

    /// <summary>
    /// Hindsight RAG 客户端：查询知识库，返回相关文档片段。
    /// 优先用于 ai.material.search（物料检索）
    /// </summary>
    internal class HindsightRagClient
    {
        private readonly HindsightConfig _config;
        private readonly HttpClient _http;
        private readonly Action<string> _log;

        public HindsightRagClient(HindsightConfig config, Action<string> log)
        {
            _config = config;
            _log = log;
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        }

        /// <summary>
        /// 查询 Hindsight 知识库
        /// </summary>
        public Dictionary<string, object> Query(string query, int? topK = null, string bankOverride = null,
            double? scoreThresholdOverride = null, Dictionary<string, object> extraPayload = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string url = _config.BaseUrl.TrimEnd('/') + "/api/v1/query";
            string bank = string.IsNullOrWhiteSpace(bankOverride) ? _config.Bank : bankOverride;
            double scoreThreshold = scoreThresholdOverride ?? _config.ScoreThreshold;

            try
            {
                var body = new Dictionary<string, object>
                {
                    ["query"] = query,
                    ["bank"] = bank,
                    ["top_k"] = topK ?? _config.TopK,
                    ["score_threshold"] = scoreThreshold
                };

                if (!string.IsNullOrEmpty(_config.SourceDbPath))
                    body["source_db_path"] = _config.SourceDbPath;

                if (extraPayload != null)
                {
                    foreach (var kv in extraPayload)
                    {
                        if (!body.ContainsKey(kv.Key) && kv.Value != null)
                            body[kv.Key] = kv.Value;
                    }
                }

                string json = new JavaScriptSerializer().Serialize(body);
                _log(string.Format("[Hindsight] query bank={0} top_k={1} threshold={2} q={3} url={4}",
                    bank, topK ?? _config.TopK, scoreThreshold, Shorten(query, 60), url));

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(_config.ApiKey))
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _config.ApiKey);

                var response = _http.SendAsync(request).GetAwaiter().GetResult();
                string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                sw.Stop();
                _log(string.Format("[Hindsight] query status={0} elapsed={1:F0}ms", (int)response.StatusCode, sw.Elapsed.TotalMilliseconds));

                if (response.IsSuccessStatusCode)
                {
                    var serializer = new JavaScriptSerializer();
                    var parsed = serializer.Deserialize<Dictionary<string, object>>(result);
                    return new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["source"] = "hindsight",
                        ["data"] = parsed,
                        ["elapsed_ms"] = (int)sw.Elapsed.TotalMilliseconds
                    };
                }

                _log(string.Format("[Hindsight] query failed: {0} {1}", (int)response.StatusCode, Shorten(result, 200)));
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["source"] = "hindsight",
                    ["error"] = string.Format("Hindsight returned {0}", (int)response.StatusCode)
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _log(string.Format("[Hindsight] query FAILED elapsed={0:F0}ms err={1}", sw.Elapsed.TotalMilliseconds, ex.Message));
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["source"] = "hindsight",
                    ["error"] = string.Format("Hindsight offline or query failed: {0}", ex.Message)
                };
            }
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
            return text.Substring(0, max) + "...";
        }
    }
}
