using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ComicManager
{
    /// <summary>
    /// 按模板重命名：**原地改**现有系列文件夹的名字 + 每一本的文件名。
    /// 默认 `{series}/{prefix}{id}` → 文件夹改成系列名、里面的书改名成 V01 / SP01。
    ///
    /// 和上一版的区别：上一版把「库根 + 模板」当成新路径，于是「重命名」变成了
    /// 「把书搬到另一个新文件夹」——旧文件夹里剩下一堆 cover.jpg / series.json，
    /// 看起来就像刮削写错了地方。现在只有「文件夹改名」和「文件改名」两步，
    /// 文件永远不出原来的那个系列目录（除非模板自己写了子目录）。
    /// </summary>
    public static class Renamer
    {
        /// <summary>模板默认值：第一段 = 文件夹名，最后一段 = 文件名。</summary>
        public const string DefaultTemplate = "{series}/{prefix}{id}";

        /// <summary>模板里能用的变量，界面上直接列给用户看（变量名不分大小写）。</summary>
        public static readonly string[] Vars =
        {
            "{series}", "{prefix}", "{id}", "{id3}", "{vol}", "{num}", "{title}", "{year}"
        };

        /// <summary>`{xxx}` 或 `{xxx:3}`：变量名 + 可选的补零位数。</summary>
        static readonly Regex VarRe = new Regex(@"\{([A-Za-z0-9_]+)(?::(\d{1,2}))?\}", RegexOptions.Compiled);

        /// <summary>
        /// 渲染相对路径（不含扩展名）。`/` 分段：**第一段是文件夹名，最后一段是文件名**。
        /// 变量值只给「数字本身」：{id} = 01、{id3} = 001、{prefix} = V / SP，
        /// 想拼成 V01 就写 `{prefix}{id}` —— 前缀和数字分开，才不会出现 V0V1 这种拼法。
        /// </summary>
        public static string Render(string template, string series, string year,
                                    string label, string volume, string number, string title)
        {
            var t = string.IsNullOrEmpty(template) ? DefaultTemplate : template;
            var s = string.IsNullOrEmpty(series) ? "未命名系列" : series;

            var prefix = (label ?? "").Trim().StartsWith("SP", StringComparison.OrdinalIgnoreCase)
                ? "SP" : "V";
            var digits = Digits(volume, number, label);

            var r = VarRe.Replace(t, delegate(Match m)
            {
                var key = m.Groups[1].Value.ToLowerInvariant();
                int pad = 0;
                if (m.Groups[2].Success) int.TryParse(m.Groups[2].Value, out pad);

                switch (key)
                {
                    case "series": return s;
                    case "year": return year ?? "";
                    case "title": return title ?? "";
                    case "label": return label ?? "";              // 旧变量：V01 / SP01
                    case "prefix": case "type": return prefix;
                    case "volume": case "vol": return Pad(volume, pad);
                    case "number": case "num": case "issue": return Pad(number, pad);
                    case "id": return Pad(digits, pad > 0 ? pad : 2);   // 默认两位：01
                    case "id3": return Pad(digits, 3);                 // 三位：001
                    case "id2": return Pad(digits, 2);
                }
                return m.Value;   // 不认识的变量原样留着，用户一眼能看出自己写错了
            });

            return SanitizePath(r);
        }

        /// <summary>取这一本的「号」：优先卷号，其次话号；都没有就从册号 V01/SP01 里抠数字。</summary>
        static string Digits(string volume, string number, string label)
        {
            if (!string.IsNullOrEmpty(volume)) return volume.Trim();
            if (!string.IsNullOrEmpty(number)) return number.Trim();

            var m = Regex.Match(label ?? "", @"\d{1,4}");
            return m.Success ? m.Value : "";
        }

        /// <summary>按位数补零；pad = 0 表示保持原样。</summary>
        static string Pad(string v, int pad)
        {
            var s = (v ?? "").Trim();
            if (pad <= 0 || s.Length == 0) return s;
            if (s.Length >= pad) return s;
            return s.PadLeft(pad, '0');
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

        /// <summary>一次重命名的计划：先改文件夹（可选），再改文件。</summary>
        public sealed class Plan
        {
            /// <summary>现在的系列目录。</summary>
            public string DirSrc = "";

            /// <summary>要改成的新目录名；空 = 文件夹名不用动。</summary>
            public string DirDst = "";

            /// <summary>文件夹层面的说明（目标已存在之类），不为空时只改文件名。</summary>
            public string Note = "";

            public readonly List<Item> Items = new List<Item>();
        }

        /// <summary>
        /// 算目标路径。第一段（如果不止一段且所有文件都一样）当作**系列文件夹的新名字**，
        /// 剩下的当作文件夹内的相对路径。
        /// </summary>
        public static Plan Build(string template, string seriesDir, string series, string year,
                                 List<KeyValuePair<string, string>> files)
        {
            var plan = new Plan { DirSrc = seriesDir };
            if (files == null || files.Count == 0) return plan;

            var segs = new List<string[]>();
            var srcs = new List<string>();
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

                srcs.Add(Full(f.Key));
                segs.Add(rel.Split('\\'));
            }
            if (srcs.Count == 0) return plan;

            seriesDir = Full(seriesDir);
            plan.DirSrc = seriesDir;

            var root = Path.GetDirectoryName(seriesDir);
            if (string.IsNullOrEmpty(root)) root = seriesDir;

            // 第一段是不是「文件夹名」：所有文件都不止一段，且第一段完全一样
            bool folder = true;
            for (int i = 0; i < segs.Count; i++)
                if (segs[i].Length < 2 || segs[i][0] != segs[0][0]) { folder = false; break; }

            var workDir = seriesDir;
            if (folder)
            {
                var target = Path.Combine(root, segs[0][0]).TrimEnd('\\');
                var current = seriesDir.TrimEnd('\\');

                if (string.Equals(target, current, StringComparison.Ordinal))
                {
                    plan.DirDst = "";                   // 名字一样，不用动
                }
                else if (string.Equals(target, current, StringComparison.OrdinalIgnoreCase))
                {
                    plan.DirDst = target;               // 只差大小写：仍要改（走临时名）
                    workDir = target;
                }
                else if (Directory.Exists(target))
                {
                    // 目标文件夹已经存在：不动文件夹，改成「在当前文件夹里改文件名」，
                    // 免得把两批书混在一起
                    plan.Note = "目标文件夹「" + Path.GetFileName(target) + "」已存在，本次只改文件名";
                }
                else
                {
                    plan.DirDst = target;
                    workDir = target;
                }
            }

            for (int i = 0; i < srcs.Count; i++)
            {
                var s = segs[i];
                IEnumerable<string> keep = s;
                if (folder) keep = Skip(s, 1);         // 第一段已经变成文件夹名了

                var rel = string.Join("\\", new List<string>(keep).ToArray());
                if (rel.Length == 0) rel = Path.GetFileNameWithoutExtension(srcs[i]);

                var ext = Path.GetExtension(srcs[i]);
                plan.Items.Add(new Item { Src = srcs[i], Dst = Path.Combine(workDir, rel + ext) });
            }
            return plan;
        }

        static IEnumerable<string> Skip(string[] a, int n)
        {
            for (int i = n; i < a.Length; i++) yield return a[i];
        }

        /// <summary>统一成完整路径：E:/lib 与 E:\lib 混着写时，字符串比较会把「没变」当成「变了」。</summary>
        static string Full(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            try { return Path.GetFullPath(p); } catch { return p; }
        }

        /// <summary>
        /// 执行：先改文件夹，再逐本改名。只在目标不存在时才动手，冲突一律跳过并报出来，不覆盖任何已有文件。
        /// 文件夹改名失败就整组停下 —— 文件路径都是按新文件夹算的，硬改会把书搬到不存在的位置。
        /// </summary>
        public static Dictionary<string, object> Apply(Plan plan, Action<string, string> log)
        {
            int moved = 0, skipped = 0, failed = 0;
            string err = "";
            string curDir = plan.DirSrc;

            if (plan != null && !string.IsNullOrEmpty(plan.DirDst) && !string.IsNullOrEmpty(plan.DirSrc))
            {
                try
                {
                    var dst = plan.DirDst;
                    if (Directory.Exists(dst))
                    {
                        // 只差大小写：Windows 不允许「改成同名但不同大小写」，借一个临时名走两步
                        if (!string.Equals(dst, plan.DirSrc, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("目标文件夹已存在");
                        var tmp = dst + ".renaming";
                        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                        Directory.Move(plan.DirSrc, tmp);
                        Directory.Move(tmp, dst);
                    }
                    else Directory.Move(plan.DirSrc, dst);

                    curDir = dst;
                    if (log != null)
                        log("文件夹 " + Path.GetFileName(plan.DirSrc) + " → " + Path.GetFileName(dst), "i");
                }
                catch (Exception ex)
                {
                    err = "文件夹改名失败：" + ex.Message;
                    if (log != null) log(err + "（这一组没有动）", "e");
                    return new Dictionary<string, object>
                    {
                        {"ok", false}, {"moved", 0}, {"skipped", 0}, {"failed", plan.Items.Count},
                        {"folder", ""}, {"err", err}
                    };
                }
            }

            if (plan != null)
            foreach (var it in plan.Items)
            {
                try
                {
                    // 文件夹改过名以后，源文件也跟着换了目录
                    var src = Path.Combine(curDir, Path.GetFileName(it.Src));
                    if (string.Equals(src, it.Dst, StringComparison.Ordinal)) { skipped++; continue; }
                    if (string.Equals(src, it.Dst, StringComparison.OrdinalIgnoreCase))
                    {
                        // 只差大小写：借临时名走两步，否则 Windows 认为「目标已存在」
                        var tmp = it.Dst + ".renaming";
                        File.Move(src, tmp);
                        File.Move(tmp, it.Dst);
                        moved++;
                        if (log != null) log("改名 " + Path.GetFileName(src) + " → " + Path.GetFileName(it.Dst), "i");
                        continue;
                    }

                    if (!File.Exists(src)) { failed++; it.Note = "源文件不存在"; continue; }
                    if (File.Exists(it.Dst))
                    {
                        failed++; it.Note = "目标已存在，跳过";
                        if (log != null) log("跳过 " + Path.GetFileName(it.Dst) + "：同名文件已存在", "w");
                        continue;
                    }

                    var dir = Path.GetDirectoryName(it.Dst);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    File.Move(src, it.Dst);
                    moved++;
                    if (log != null) log("改名 " + Path.GetFileName(src) + " → " + Path.GetFileName(it.Dst), "i");
                }
                catch (Exception ex)
                {
                    failed++;
                    it.Note = ex.Message;
                    if (log != null) log("改名失败 " + Path.GetFileName(it.Src) + "：" + ex.Message, "e");
                }
            }

            if (log != null) log("重命名完成：改了 " + moved + " 个，未变 " + skipped + "，失败 " + failed,
                                 failed > 0 ? "w" : "s");

            return new Dictionary<string, object>
            {
                {"ok", true}, {"moved", moved}, {"skipped", skipped}, {"failed", failed},
                {"folder", plan == null ? "" : plan.DirDst}, {"err", err}
            };
        }
    }
}
