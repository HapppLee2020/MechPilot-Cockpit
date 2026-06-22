using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace SwAgentAddin
{
    /// <summary>
    /// 上下文裁剪：summary / selected / full 三档
    /// </summary>
    internal static class ContextTrimmer
    {
        /// <summary>
        /// 根据 contextMode 裁剪 CockpitContext JSON
        /// </summary>
        public static string Trim(string contextJson, string contextMode)
        {
            if (string.IsNullOrEmpty(contextJson)) return "{}";
            if (string.Equals(contextMode, "full", StringComparison.OrdinalIgnoreCase))
                return contextJson;

            var serializer = new JavaScriptSerializer();
            var ctx = serializer.Deserialize<Dictionary<string, object>>(contextJson);
            if (ctx == null) return "{}";

            if (string.Equals(contextMode, "selected", StringComparison.OrdinalIgnoreCase))
                return TrimSelected(ctx, serializer);

            // default: summary
            return TrimSummary(ctx, serializer);
        }

        private static string TrimSummary(Dictionary<string, object> ctx, JavaScriptSerializer serializer)
        {
            var summary = new Dictionary<string, object>();

            if (ctx.ContainsKey("schema_version")) summary["schema_version"] = ctx["schema_version"];
            if (ctx.ContainsKey("client")) summary["client"] = ctx["client"];

            if (ctx.ContainsKey("document"))
            {
                var doc = ctx["document"] as Dictionary<string, object>;
                if (doc != null)
                {
                    summary["document"] = new Dictionary<string, object>
                    {
                        ["title"] = GetStr(doc, "title"),
                        ["doc_type"] = GetStr(doc, "doc_type"),
                        ["file_path"] = GetStr(doc, "file_path")
                    };
                }
            }

            if (ctx.ContainsKey("selection"))
            {
                var sel = ctx["selection"] as Dictionary<string, object>;
                if (sel != null)
                {
                    summary["selection"] = new Dictionary<string, object>
                    {
                        ["count"] = GetInt(sel, "count"),
                        ["mode"] = GetStr(sel, "mode")
                    };
                }
            }

            if (ctx.ContainsKey("property_table"))
            {
                var pt = ctx["property_table"] as Dictionary<string, object>;
                if (pt != null)
                {
                    var ptSummary = new Dictionary<string, object>();
                    if (pt.ContainsKey("columns"))
                    {
                        var cols = pt["columns"] as System.Collections.ArrayList;
                        ptSummary["column_count"] = cols != null ? cols.Count : 0;
                        ptSummary["columns"] = pt["columns"];
                    }
                    if (pt.ContainsKey("rows"))
                    {
                        var rows = pt["rows"] as System.Collections.ArrayList;
                        ptSummary["row_count"] = rows != null ? rows.Count : 0;
                    }
                    summary["property_table"] = ptSummary;
                }
            }

            if (ctx.ContainsKey("assembly_tree"))
            {
                var tree = ctx["assembly_tree"] as Dictionary<string, object>;
                if (tree != null)
                {
                    summary["assembly_tree"] = new Dictionary<string, object>
                    {
                        ["has_tree"] = true,
                        ["node_count"] = CountTreeNodes(tree)
                    };
                }
            }

            if (ctx.ContainsKey("summary")) summary["summary"] = ctx["summary"];
            if (ctx.ContainsKey("warnings")) summary["warnings"] = ctx["warnings"];

            return serializer.Serialize(summary);
        }

        private static string TrimSelected(Dictionary<string, object> ctx, JavaScriptSerializer serializer)
        {
            var result = new Dictionary<string, object>();

            if (ctx.ContainsKey("schema_version")) result["schema_version"] = ctx["schema_version"];
            if (ctx.ContainsKey("client")) result["client"] = ctx["client"];
            if (ctx.ContainsKey("document")) result["document"] = ctx["document"];
            if (ctx.ContainsKey("selection")) result["selection"] = ctx["selection"];

            if (ctx.ContainsKey("property_table") && ctx.ContainsKey("selection"))
            {
                var pt = ctx["property_table"] as Dictionary<string, object>;
                var sel = ctx["selection"] as Dictionary<string, object>;
                if (pt != null && sel != null)
                {
                    var selectedNames = new HashSet<string>();
                    var selectedItems = sel.ContainsKey("items") ? sel["items"] as System.Collections.ArrayList : null;
                    if (selectedItems != null)
                    {
                        foreach (var item in selectedItems)
                        {
                            var dict = item as Dictionary<string, object>;
                            if (dict != null && dict.ContainsKey("name"))
                                selectedNames.Add(GetStr(dict, "name"));
                        }
                    }

                    if (selectedNames.Count > 0)
                    {
                        var filtered = new Dictionary<string, object>();
                        if (pt.ContainsKey("columns")) filtered["columns"] = pt["columns"];
                        if (pt.ContainsKey("rows"))
                        {
                            var rows = pt["rows"] as System.Collections.ArrayList;
                            if (rows != null)
                            {
                                var filteredRows = new System.Collections.ArrayList();
                                foreach (var row in rows)
                                {
                                    var rowDict = row as Dictionary<string, object>;
                                    if (rowDict != null && rowDict.ContainsKey("target_name"))
                                    {
                                        string name = GetStr(rowDict, "target_name");
                                        if (selectedNames.Contains(name))
                                            filteredRows.Add(row);
                                    }
                                }
                                filtered["rows"] = filteredRows;
                            }
                        }
                        result["property_table"] = filtered;
                    }
                    else
                    {
                        result["property_table"] = pt;
                    }
                }
            }
            else if (ctx.ContainsKey("property_table"))
            {
                result["property_table"] = ctx["property_table"];
            }

            if (ctx.ContainsKey("summary")) result["summary"] = ctx["summary"];
            if (ctx.ContainsKey("warnings")) result["warnings"] = ctx["warnings"];

            return serializer.Serialize(result);
        }

        private static string GetStr(Dictionary<string, object> d, string key)
        {
            return d.ContainsKey(key) ? Convert.ToString(d[key]) : "";
        }

        private static int GetInt(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key)) return 0;
            try { return Convert.ToInt32(d[key]); } catch { return 0; }
        }

        private static int CountTreeNodes(Dictionary<string, object> node)
        {
            int count = 1;
            if (node.ContainsKey("children"))
            {
                var children = node["children"] as System.Collections.ArrayList;
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        var childDict = child as Dictionary<string, object>;
                        if (childDict != null) count += CountTreeNodes(childDict);
                    }
                }
            }
            return count;
        }
    }
}
