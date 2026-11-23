using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ComicManager
{
    /// <summary>
    /// 按模板重命名/归档：默认「系列名/册号」，如 `新世纪福音战士 碇真嗣育成计划/V01.cbz`。
    /// 预览和执行走同一套渲染，避免「看到的名字」和「真改成的名字」不一致。
    /// </summary>
    public static class Renamer
    {
        /// <summary>模板里能用的变量，界面上直接列给用户看。</summary>
        public const string DefaultTemplate = "{series}/{id}";

        public static readonly string[] Vars = { "{series}", "{id}", "{volume}", "{number}", "{title}", "{year}" };

        /// <summary>渲染相对路径（不含扩展名）。{series} 里带 / 会被当成目录层级。</summary>
        public static string Render(string template, string series, string year,
                                    string label, string volume, string number, string title)
        {
            var t = string.IsNullOrEmpty(template) ? DefaultTemplate : template;
            var s = string.IsNullOrEmpty(series) ? "未命名系列" : series;

            var r = t.Replace("{series}", s)
                     .Replace("{id}", label)
                     .Replace("{volume}", volume ?? "")
                     .Replace("{number}", number ?? "")
                     .Replace("{title}", title ?? "")
                     .Replace("{year}", year ?? "");

            return SanitizePath(r);
        }

        /// <summary>
        /// 逐段清理非法字符。`/` 是目录分隔符要留着，其余 Windows 不认的换成下划线；
        /// `..` 一律去掉，避免模板把文件挪到库外面去。
        /// </summary>
        static string SanitizePath(string rel)
        {
            var parts = new List<string>();
            foreach (var raw in (rel ?? "").Replace('\\', '/').Split('/'))
            {
                var seg = raw.Trim();
                if (seg.Length == 0 || seg == "." || seg == "..") continue;
                var sb = new StringBuilder(seg.Length);
                foreach (var c in seg)
                {
                    if (c < 32 || "\\:*?\"<>|".IndexOf(c) >= 0) sb.Append('_');
                    else sb.Append(c);
                }
                var clean = Tidy(sb.ToString());
                if (clean.Length == 0) continue;
                parts.Add(clean);
            }
            return string.Join("\\", parts.ToArray());
        }

        /// <summary>
        /// 模板里某个变量为空时会留下悬空的分隔符（`V02 -`、`系列 ()`），这里收干净。
        /// 不处理的话，{title} 为空的系列会得到一堆以「 -」结尾的文件名。
        /// </summary>
        static string Tidy(string seg)
        {
            var t = seg;
            for (int i = 0; i < 3; i++)
            {
                t = Regex.Replace(t, @"\s+", " ").Trim();
                t = Regex.Replace(t, @"\(\s*\)|（\s*）|\[\s*\]", "");
                t = t.Trim(' ', '-', '_', '.', '·', '—');
            }
            return t;
        }

        public sealed class Item
        {
            public string Src = "";
            public string Dst = "";
            public string Note = "";
            public bool Same { get { return string.Equals(Src, Dst, StringComparison.OrdinalIgnoreCase); } }
        }

        /// <summary>
        /// 算目标路径。库根 = 系列目录的上一级：`X/系列/a.cbz` 重命名成 `X/{series}/{id}.cbz`。
        /// 同目录内只改名时 Dst 会和 Src 指向同一个文件，调用方按 Same 跳过。
        /// </summary>
        public static List<Item> Plan(string template, string seriesDir, string series, string year,
                                      List<KeyValuePair<string, string>> files)
        {
            var list = new List<Item>();
            if (files == null) return list;

            var root = Path.GetDirectoryName(seriesDir);
            if (string.IsNullOrEmpty(root)) root = seriesDir;

            foreach (var f in files)
            {
                if (string.IsNullOrEmpty(f.Key)) continue;
                var parts = (f.Value ?? "").Split('|');
                var label = parts.Length > 0 ? parts[0] : "";
                var vol = parts.Length > 1 ? parts[1] : "";
                var num = parts.Length > 2 ? parts[2] : "";
                var title = parts.Length > 3 ? parts[3] : "";

                var rel = Render(template, series, year, label, vol, num, title);
                if (rel.Length == 0) rel = (label.Length > 0 ? label : "未命名");

                var ext = Path.GetExtension(f.Key);
                var dst = Path.Combine(root, rel + ext);
                list.Add(new Item { Src = f.Key, Dst = dst });
            }
            return list;
        }

        /// <summary>执行重命名。只在目标不存在时才动手，冲突一律跳过并报出来，不覆盖任何已有文件。</summary>
        public static Dictionary<string, object> Apply(List<Item> items, Action<string, string> log)
        {
            int moved = 0, skipped = 0, failed = 0;

            foreach (var it in items)
            {
                try
                {
                    if (it.Same) { skipped++; continue; }
                    if (!File.Exists(it.Src)) { failed++; it.Note = "源文件不存在"; continue; }
                    if (File.Exists(it.Dst)) { failed++; it.Note = "目标已存在，跳过"; 
                        if (log != null) log("跳过 " + Path.GetFileName(it.Dst) + "：同名文件已存在", "w");
                        continue; }

                    var dir = Path.GetDirectoryName(it.Dst);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    File.Move(it.Src, it.Dst);
                    moved++;
                    if (log != null) log("重命名 " + Path.GetFileName(it.Src) + " → " + Path.GetFileName(it.Dst), "i");
                }
                catch (Exception ex)
                {
                    failed++;
                    it.Note = ex.Message;
                    if (log != null) log("重命名失败 " + Path.GetFileName(it.Src) + "：" + ex.Message, "e");
                }
            }

            if (log != null) log("重命名完成：改了 " + moved + " 个，未变 " + skipped + "，失败 " + failed,
                                 failed > 0 ? "w" : "s");

            return new Dictionary<string, object>
            {
                {"ok", true}, {"moved", moved}, {"skipped", skipped}, {"failed", failed}
            };
        }
    }
}
