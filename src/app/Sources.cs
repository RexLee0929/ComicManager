using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ComicManager
{
    /// <summary>一条搜索结果。列表里只显示标题/年份/来源，详情等选中后再拉。</summary>
    public sealed class Candidate
    {
        public string Source = "";
        public string Id = "";
        public string Title = "";
        public string TitleAlt = "";
        public string Aliases = "";   // 全部别名（"A | B | C"），跨语言匹配靠它
        public string Year = "";
        public string Type = "";
        public string CoverUrl = "";
        public string Summary = "";
        public double Score;

        /// <summary>给界面看的一行小字：年份 · 类型 · 匹配度。</summary>
        public string Sub
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(Year)) parts.Add(Year);
                if (!string.IsNullOrEmpty(Type)) parts.Add(Type);
                parts.Add(Math.Round(Score * 100) + "%");
                return string.Join(" · ", parts.ToArray());
            }
        }
    }

    /// <summary>
    /// 元数据源。每个源只做两件事：按关键词搜（返回候选）、按 id 拉详情（返回统一模型）。
    /// 网络相关的收口在 Net 里，这里只管字段映射。
    ///
    /// 三个源的分工（也是界面上的排序依据）：
    ///   bangumi     中文书名 / 出版社 / 卷数最全，中文漫画库首选
    ///   mangaupdates 英文与日文条目最全，作者、出版社、年份稳
    ///   mangadex    封面质量最好，简介多语言，条目标题语言最杂
    /// </summary>
    public static class MetaSources
    {
        public const string Bangumi = "bangumi";
        public const string MangaUpdates = "mangaupdates";
        public const string MangaDex = "mangadex";

        public static readonly string[] All = { Bangumi, MangaUpdates, MangaDex };

        public static string LabelOf(string key)
        {
            switch (key)
            {
                case Bangumi: return "Bangumi";
                case MangaUpdates: return "MangaUpdates";
                case MangaDex: return "MangaDex";
            }
            return key;
        }

        public static List<Candidate> Search(string term, List<string> sources, Action<string> log)
        {
            var all = new List<Candidate>();
            if (string.IsNullOrEmpty(term)) return all;
            if (sources == null || sources.Count == 0) sources = All.ToList();

            foreach (var src in sources)
            {
                List<Candidate> part = null;
                try
                {
                    if (src == Bangumi) part = SearchBangumi(term, log);
                    else if (src == MangaUpdates) part = SearchMangaUpdates(term, log);
                    else if (src == MangaDex) part = SearchMangaDex(term, log);
                }
                catch (Exception ex)
                {
                    if (log != null) log("查询 " + LabelOf(src) + " 失败：" + ex.Message);
                }

                if (part == null) continue;
                foreach (var c in part) { c.Score = Score(term, c.Title, c.TitleAlt, c.Aliases); all.Add(c); }
            }

            // 按匹配度排；同分时保持「源顺序」（Bangumi 在前），因为源顺序就是优先级
            return all.OrderByDescending(x => x.Score)
                      .ThenBy(x => Array.IndexOf(All, x.Source))
                      .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                      .ToList();
        }

        // ------------------------------------------------------------ 匹配度
        /// <summary>
        /// 关键词与标题的相似度。中文没有词边界，做不了分词，所以用「去掉标点后的整串比较 + 二元组重合度」，
        /// 比单纯 contains 稳，也不需要额外词典。
        /// </summary>
        public static double Score(string term, string title, string alt)
        {
            return Score(term, title, alt, "");
        }

        /// <summary>
        /// aliases 是候选的全部别名（各语言标题）。中文名去搜 MangaDex 时主标题是英文，
        /// 只比主标题必然 0 分；别名里有原文名就能命中，补字段才不会白跑。
        /// </summary>
        public static double Score(string term, string title, string alt, string aliases)
        {
            var t = Norm(term);
            if (t.Length == 0) return 0;

            var pool = new List<string>();
            pool.Add(title);
            pool.Add(alt);
            if (!string.IsNullOrEmpty(aliases))
                pool.AddRange(aliases.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));

            double best = 0;
            foreach (var raw in pool)
            {
                var s = Norm(raw);
                if (s.Length == 0) continue;

                if (s == t) best = Math.Max(best, 1.0);
                else if (s.StartsWith(t) || t.StartsWith(s)) best = Math.Max(best, 0.86);
                else if (s.IndexOf(t, StringComparison.Ordinal) >= 0) best = Math.Max(best, 0.76);
                else best = Math.Max(best, Dice(t, s) * 0.9);
            }
            return Math.Round(best, 3);
        }

        static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        static double Dice(string a, string b)
        {
            if (a.Length < 2 || b.Length < 2) return 0;
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < a.Length - 1; i++)
            {
                var g = a.Substring(i, 2);
                map[g] = map.ContainsKey(g) ? map[g] + 1 : 1;
            }
            int hit = 0, total = 0;
            for (int i = 0; i < b.Length - 1; i++)
            {
                var g = b.Substring(i, 2);
                total++;
                if (map.ContainsKey(g) && map[g] > 0) { hit++; map[g]--; }
            }
            return total == 0 ? 0 : 2.0 * hit / (a.Length - 1 + b.Length - 1);
        }

        // ------------------------------------------------------------ 详情
        public static Metadata Fetch(string source, string id, Action<string> log)
        {
            try
            {
                Metadata m = null;
                if (source == Bangumi) m = FetchBangumi(id);
                else if (source == MangaUpdates) m = FetchMangaUpdates(id);
                else if (source == MangaDex) m = FetchMangaDex(id);
                if (m != null) Finalize(m);
                return m;
            }
            catch (Exception ex)
            {
                if (log != null) log("拉取详情失败：" + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// 拉详情，并可选地让其他已勾选的来源「补空」：
        /// 某个条目在 Bangumi 上没有作者、MangaDex 没有出版社，这是数据源本身的缺口，
        /// 不该让用户手填。只在字段为空时补，已有值一律不动。
        /// </summary>
        public static Metadata FetchMerged(string source, string id, string term,
                                           List<string> sources, Action<string> log)
        {
            var m = Fetch(source, id, log);
            if (m == null) return null;
            if (sources == null || sources.Count == 0) return m;

            // 搜索词先给用户输入的那个，不行再换原名/译名：
            // 中文译名在 MangaUpdates / MangaDex 上常常搜不到，反倒是日文原名一搜一个准。
            var terms = new List<string>();
            foreach (var t in new[] { term, m.LocalizedSeries, m.Series })
                if (!string.IsNullOrEmpty(t) && !terms.Contains(t)) terms.Add(t);
            if (terms.Count == 0) return m;

            foreach (var t in terms)
            {
                foreach (var s in sources)
                {
                    if (s == source) continue;
                    try
                    {
                        var best = Search(t, new List<string> { s }, null).FirstOrDefault();
                        if (best == null || best.Score < 0.75) continue;

                        var other = Fetch(s, best.Id, null);
                        if (other == null) continue;

                        var gaps = GapFields(m);
                        if (gaps.Count > 0)
                        {
                            m.FillFrom(other);
                            var done = new List<string>();
                            foreach (var g in gaps)
                                if (g.Value(m) != null && g.Value(m).Trim().Length > 0) done.Add(g.Key);
                            if (done.Count > 0 && log != null)
                                log("用 " + LabelOf(s) + " 补了 " + done.Count + " 项：" + string.Join("、", done.ToArray()));
                        }
                    }
                    catch { }
                }
                // 关键字段齐了就不再换词重搜，别把时间花在多余的请求上
                if (!HasGaps(m)) break;
            }
            Finalize(m);   // 合并后语言可能才补上，这时才能正确推阅读方向
            return m;
        }

        /// <summary>
        /// 当前还空着的字段（带中文名和取值器），用来做两件事：
        /// 判断要不要换词再搜、以及补完之后告诉用户具体补了什么。
        /// </summary>
        static List<KeyValuePair<string, Func<Metadata, string>>> GapFields(Metadata m)
        {
            var all = new List<KeyValuePair<string, Func<Metadata, string>>>
            {
                Pair("作者", x => x.Writer), Pair("画师", x => x.Penciller),
                Pair("出版社", x => x.Publisher), Pair("品牌/书系", x => x.Imprint),
                Pair("简介", x => x.Summary), Pair("年份", x => x.Year),
                Pair("类型", x => x.Genre), Pair("标签", x => x.Tags),
                Pair("分级", x => x.AgeRating), Pair("阅读方向", x => x.Manga),
                Pair("总卷数", x => x.Count), Pair("语言", x => x.LanguageISO),
                Pair("合集", x => x.SeriesGroup), Pair("译者", x => x.Translator),
                Pair("ISBN", x => x.GTIN), Pair("链接", x => x.Web)
            };

            var gaps = new List<KeyValuePair<string, Func<Metadata, string>>>();
            foreach (var p in all)
            {
                var v = p.Value(m);
                if (string.IsNullOrEmpty(v) || v.Trim().Length == 0) gaps.Add(p);
            }
            return gaps;
        }

        static KeyValuePair<string, Func<Metadata, string>> Pair(string key, Func<Metadata, string> f)
        {
            return new KeyValuePair<string, Func<Metadata, string>>(key, f);
        }

        /// <summary>作者/出版社/简介/年份/分类还缺着吗 —— 决定要不要再换一个搜索词试。</summary>
        static bool HasGaps(Metadata m)
        {
            // 分级也算缺项：Bangumi 从不给分级，只有 MangaDex 的 contentRating 靠谱，
            // 提前 break 就永远补不到（用户看到的「年龄分级没了」就是这么来的）。
            return string.IsNullOrEmpty(m.Writer) || string.IsNullOrEmpty(m.Publisher)
                || string.IsNullOrEmpty(m.Summary) || string.IsNullOrEmpty(m.Year)
                || string.IsNullOrEmpty(m.Genre) || string.IsNullOrEmpty(m.AgeRating);
        }

        /// <summary>数一下关键字段填了几个，用来判断「补齐」有没有真的补到东西。</summary>
        static int Filled(Metadata m)
        {
            int n = 0;
            foreach (var v in new[] { m.Series, m.LocalizedSeries, m.Summary, m.Year, m.Publisher,
                                      m.Imprint, m.Writer, m.Penciller, m.Genre, m.Tags, m.Count,
                                      m.LanguageISO, m.Web })
                if (!string.IsNullOrEmpty(v)) n++;
            return n;
        }

        /// <summary>
        /// 展示名与原始名：有译名就展示译名、原始名留在 LocalizedSeries；
        /// 没有译名（或译名与原名相同）就只用原名，LocalizedSeries 留空 —— 「非译名的不算」。
        /// </summary>
        static void SetNames(Metadata m, string translated, string original, string fallback)
        {
            var t = (translated ?? "").Trim();
            var o = (original ?? "").Trim();
            if (t.Length > 0 && o.Length > 0 && t != o)
            {
                m.Series = t;
                m.LocalizedSeries = o;
            }
            else
            {
                m.Series = o.Length > 0 ? o : (t.Length > 0 ? t : fallback);
                m.LocalizedSeries = "";
            }
            if (string.IsNullOrEmpty(m.Series)) m.Series = fallback ?? "";
            m.SeriesSort = m.Series;
        }

        static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s)
                if (c >= '\u3041' && c <= '\u9fff') return true;
            return false;
        }

        // ------------------------------------------------------------ Bangumi
        static List<Candidate> SearchBangumi(string term, Action<string> log)
        {
            var body = "{\"keyword\":" + MiniJson.Serialize(term) + ",\"filter\":{\"type\":[1]},\"limit\":15}";
            var txt = Net.PostJson("https://api.bgm.tv/v0/search/subjects", body, 15000);
            if (string.IsNullOrEmpty(txt)) { if (log != null) log("Bangumi 没有响应"); return null; }

            var root = MiniJson.Parse(txt) as Dictionary<string, object>;
            var list = new List<Candidate>();
            foreach (var it in Objs(Get(root, "data")))
            {
                var nameCn = S(it, "name_cn");
                var name = S(it, "name");
                list.Add(new Candidate
                {
                    Source = Bangumi,
                    Id = S(it, "id"),
                    Title = string.IsNullOrEmpty(nameCn) || nameCn == name ? name : nameCn,
                    TitleAlt = string.IsNullOrEmpty(nameCn) || nameCn == name ? "" : name,
                    Year = YearOf(S(it, "date")),
                    Type = S(it, "platform"),
                    CoverUrl = S(Get(it, "images"), "large"),
                    Summary = S(it, "summary")
                });
            }
            return list;
        }

        static Metadata FetchBangumi(string id)
        {
            var txt = Net.GetText("https://api.bgm.tv/v0/subjects/" + id, 15000);
            if (string.IsNullOrEmpty(txt)) return null;
            var d = MiniJson.Parse(txt) as Dictionary<string, object>;
            if (d == null) return null;

            var m = new Metadata { SourceName = LabelOf(Bangumi), SourceId = id };
            var nameCn = S(d, "name_cn");
            var name = S(d, "name");
            SetNames(m, nameCn, name, name);
            m.Summary = Clean(S(d, "summary"));
            m.Year = YearOf(S(d, "date"));
            m.CoverUrl = S(Get(d, "images"), "large");
            m.Web = "https://bgm.tv/subject/" + id;
            m.SourceUrl = m.Web;

            // 先解析 infobox：作者、出版社、别名都要先拿到，后面过滤标签时才知道该剔掉哪些词
            var noise = new List<string>();
            foreach (var box in Objs(Get(d, "infobox")))
            {
                var key = S(box, "key");
                var val = InfoboxValue(Get(box, "value"));
                if (string.IsNullOrEmpty(val)) continue;

                if (key == "作者" || key == "漫画家" || key == "漫画" || key == "作画"
                    || key == "イラスト" || key == "画")
                {
                    // 画的人：ComicInfo 里 Writer 是「主要创作者」，Penciller 是「作画」，
                    // 单作者漫画两者都填，服务器显示的「作者」才是对的
                    m.Writer = Join(m.Writer, val);
                    m.Penciller = Join(m.Penciller, val);
                    noise.AddRange(SplitWords(val));
                }
                else if (key == "原作" || key == "脚本" || key == "原作・脚本" || key == "シナリオ")
                {
                    // 原作不是作者：ComicInfo 没有这个字段，塞进 Writer 会让「作者」栏变成两个人。
                    // 写进备注留痕，界面上可以在「更多 → 备注」里看到和修改。
                    m.Notes = Join(m.Notes, "原作：" + val);
                    noise.AddRange(SplitWords(val));
                }
                else if (key == "出版社")
                {
                    // Bangumi 常把多家出版社塞进一个值（用「、」分隔），统一成 "A / B"
                    m.Publisher = JoinDelimited(m.Publisher, val);
                    noise.AddRange(SplitWords(val));
                }
                else if (key == "书系" || key == "文库" || key == "丛书" || key == "文庫"
                      || key == "叢書" || key == "レーベル" || key == "ブランド" || key == "label")
                {
                    // 品牌/书系：角川的「電撃コミックス」、集英社的「ジャンプ・コミックス」都在这
                    m.Imprint = Join(m.Imprint, val);
                }
                else if (key == "系列" || key == "シリーズ" || key == "所属系列")
                {
                    // 合集/书单：Komga 的 SeriesGroup、Kavita 的 Collection 都用它归类
                    m.SeriesGroup = Join(m.SeriesGroup, val);
                }
                else if (key == "ISBN" || key == "isbn")
                {
                    m.GTIN = FirstDigits(val);
                }
                else if (key == "发售日" || key == "连载开始") { if (string.IsNullOrEmpty(m.Year)) m.Year = YearOf(val); }
                else if (key == "别名")
                {
                    noise.AddRange(SplitWords(val));
                    if (string.IsNullOrEmpty(m.LocalizedSeries)) m.LocalizedSeries = val;
                }
                else if (key == "册数" || key == "卷数") { m.Count = FirstInt(val); }
                else if (key == "话数") { if (string.IsNullOrEmpty(m.Count)) m.Count = FirstInt(val); }
            }

            // 标签：Bangumi 的 tags 带 count，按热度取前若干个。前一半进 Genre，后一半进 Tags ——
            // Komga 用 Genre 做系列分类，Kavita 的筛选看的是 Tags，两边都喂一点。
            // Bangumi 的 tags 里混着出版社、作者、别名、纯数字，这些不是分类，要剔掉，
            // 否则「集英社」「尾田栄一郎」会跑到类型里，筛选面板立刻变成垃圾场。
            var tags = new List<string>();
            foreach (var t in Objs(Get(d, "tags")))
            {
                var tn = S(t, "name");
                if (!string.IsNullOrEmpty(tn) && !IsNoiseTag(tn, m, noise)) tags.Add(tn);
            }
            if (tags.Count > 0)
            {
                int g = Math.Min(6, tags.Count);
                m.Genre = string.Join(", ", tags.Take(g).ToArray());
                if (tags.Count > g) m.Tags = string.Join(", ", tags.Skip(g).Take(10).ToArray());
            }

            var volumes = S(d, "volumes");
            if (string.IsNullOrEmpty(m.Count) && volumes != "0") m.Count = volumes;

            // 语言不猜：LanguageISO 表示「你收藏的这一本是什么语言」，
            // 而 Bangumi 记的是原作的语言。中文版漫画被标成 ja，Komga 里筛选就全乱了。
            // 语言来源只有两个：包内已有的值、用户在下拉里选的。
            Finalize(m);
            return m;
        }

        /// <summary>从「ISBN 978-4-04-…」这类值里抠出数字（ComicInfo 的 GTIN 只认纯数字）。</summary>
        static string FirstDigits(string s)
        {
            var m = Regex.Match(s ?? "", @"\d[\d\-\s]{8,20}\d");
            if (!m.Success) return "";
            var d = Regex.Replace(m.Value, @"[^0-9]", "");
            return (d.Length == 10 || d.Length == 13) ? d : "";
        }

        static int IndexOfIgnore(string hay, string needle)
        {
            return (hay ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>有没有假名 —— 有就基本能断定是日文原版。</summary>
        static bool HasKana(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s)
                if ((c >= '\u3041' && c <= '\u309f') || (c >= '\u30a1' && c <= '\u30fa')) return true;
            return false;
        }

        /// <summary>
        /// 收尾归一：把来源没给、但能从别的线索推出来的字段补上。
        /// 只在为空时填 —— 来源明确给了值就以来源为准。
        /// </summary>
        static void Finalize(Metadata m)
        {
            // 阅读方向：日文原版右开（YesAndRightToLeft），中/韩/英左开（No）。
            // 留空在 Komga 里等于 Unknown，某些阅读器会按左开处理，日漫就整本倒着翻。
            // 只在语言已知时推：Bangumi 没有语言字段，早推会把日漫错标成左开，
            // 而且非空之后别的来源就补不进来了（FillFrom 只补空）。
            if (string.IsNullOrEmpty(m.Manga) && !string.IsNullOrEmpty(m.LanguageISO))
                m.Manga = (m.LanguageISO == "ja") ? "YesAndRightToLeft" : "No";

            // 分级：来源没给就从标签里找最重的那一档
            if (string.IsNullOrEmpty(m.AgeRating))
            {
                var pool = ((m.Genre ?? "") + "," + (m.Tags ?? "")).ToLowerInvariant();
                if (pool.IndexOf("r-18") >= 0 || pool.IndexOf("成人") >= 0
                    || pool.IndexOf("18禁") >= 0 || pool.IndexOf("成人向") >= 0)
                    m.AgeRating = "Adults Only";
                else if (pool.IndexOf("青年") >= 0 || pool.IndexOf("mature") >= 0)
                    m.AgeRating = "Mature";
            }
        }

        static string[] SplitWords(string s)
        {
            return (s ?? "").Split(new[] { ',', '/', '、', '，', '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>标签里混进来的非分类词：纯数字、系列名/别名、作者名、出版社名。</summary>
        static bool IsNoiseTag(string tag, Metadata m, List<string> extra)
        {
            var t = (tag ?? "").Trim();
            if (t.Length == 0) return true;
            if (Regex.IsMatch(t, "^\\d+$")) return true;

            var pool = new List<string>();
            foreach (var v in new[] { m.Series, m.LocalizedSeries, m.Writer, m.Publisher })
                if (!string.IsNullOrEmpty(v)) pool.AddRange(SplitWords(v));
            if (extra != null) pool.AddRange(extra);

            foreach (var p in pool)
            {
                var word = (p ?? "").Trim();
                if (word.Length < 2) continue;
                if (t.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (word.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>Bangumi infobox 的 value 有三种形态：字符串、{v:...} 列表、{k:..,v:..} 列表。</summary>
        static string InfoboxValue(object o)
        {
            if (o == null) return "";
            if (o is string) return ((string)o).Trim();

            var parts = new List<string>();
            foreach (var it in Objs(o))
            {
                var v = S(it, "v");
                if (!string.IsNullOrEmpty(v)) parts.Add(v.Trim());
            }
            if (parts.Count == 0 && o is object[])
            {
                foreach (var it in (object[])o)
                    if (it is string && ((string)it).Trim().Length > 0) parts.Add(((string)it).Trim());
            }
            return string.Join(" / ", parts.ToArray());
        }

        // ------------------------------------------------------------ MangaUpdates
        static List<Candidate> SearchMangaUpdates(string term, Action<string> log)
        {
            var body = "{\"search\":" + MiniJson.Serialize(term) + ",\"perpage\":15}";
            var txt = Net.PostJson("https://api.mangaupdates.com/v1/series/search", body, 15000);
            if (string.IsNullOrEmpty(txt)) { if (log != null) log("MangaUpdates 没有响应"); return null; }

            var root = MiniJson.Parse(txt) as Dictionary<string, object>;
            var list = new List<Candidate>();
            foreach (var wrap in Objs(Get(root, "results")))
            {
                var r = Get(wrap, "record");
                if (r == null) continue;
                list.Add(new Candidate
                {
                    Source = MangaUpdates,
                    Id = S(r, "series_id"),
                    Title = S(r, "title"),
                    Year = S(r, "year"),
                    Type = S(r, "type"),
                    CoverUrl = S(Get(r, "image"), "url") != ""
                        ? S(Get(Get(r, "image"), "url"), "original")
                        : "",
                    Summary = Clean(S(r, "description"))
                });
            }
            return list;
        }

        static Metadata FetchMangaUpdates(string id)
        {
            var txt = Net.GetText("https://api.mangaupdates.com/v1/series/" + id, 15000);
            if (string.IsNullOrEmpty(txt)) return null;
            var d = MiniJson.Parse(txt) as Dictionary<string, object>;
            if (d == null) return null;

            var m = new Metadata { SourceName = LabelOf(MangaUpdates), SourceId = id };
            m.Series = S(d, "title");
            m.SeriesSort = m.Series;
            m.Summary = Clean(S(d, "description"));
            m.Year = S(d, "year");
            m.Web = S(d, "url");
            m.SourceUrl = m.Web;

            var img = Get(d, "image");
            m.CoverUrl = S(Get(img, "url"), "original");

            var authors = new List<string>();
            foreach (var a in Objs(Get(d, "authors")))
            {
                var n = S(a, "name");
                if (!string.IsNullOrEmpty(n) && !authors.Contains(n)) authors.Add(n);
            }
            m.Writer = string.Join(", ", authors.ToArray());

            var pubs = new List<string>();
            foreach (var p in Objs(Get(d, "publishers")))
            {
                var n = S(p, "publisher_name");
                if (!string.IsNullOrEmpty(n) && !pubs.Contains(n)) pubs.Add(n);
            }
            m.Publisher = string.Join(" / ", pubs.Take(2).ToArray());

            var genres = new List<string>();
            foreach (var g in Objs(Get(d, "genres")))
            {
                var n = S(g, "genre");
                if (!string.IsNullOrEmpty(n)) genres.Add(n);
            }
            m.Genre = string.Join(", ", genres.ToArray());

            // type 决定阅读方向：Manga 是日式右开，Manhwa/Manhua 是左开
            var stype = S(d, "type");
            if (stype == "Manga") m.Manga = "YesAndRightToLeft";
            else if (stype == "Manhwa" || stype == "Manhua") m.Manga = "No";

            // 分级：MU 把成人向作品标 Adult / Mature / Smut
            foreach (var g in genres)
            {
                if (IndexOfIgnore(g, "adult") >= 0) { m.AgeRating = "Adults Only"; break; }
                if (IndexOfIgnore(g, "mature") >= 0 || IndexOfIgnore(g, "smut") >= 0) m.AgeRating = "Mature";
            }

            var alts = new List<string>();
            foreach (var a in Objs(Get(d, "associated")))
            {
                var n = S(a, "title");
                if (!string.IsNullOrEmpty(n) && !alts.Contains(n)) alts.Add(n);
            }
            // 关联作品名里挑一个 CJK 的当原名（MU 主标题是拉丁字母，原名通常在这里）
            foreach (var a in alts)
                if (HasCjk(a) && a != m.Series) { m.LocalizedSeries = a; break; }

            Finalize(m);
            return m;
        }

        // ------------------------------------------------------------ MangaDex
        static List<Candidate> SearchMangaDex(string term, Action<string> log)
        {
            var url = "https://api.mangadex.org/manga?limit=12"
                    + "&title=" + Uri.EscapeDataString(term)
                    + "&includes[]=cover_art&includes[]=author"
                    + "&contentRating[]=safe&contentRating[]=suggestive&contentRating[]=erotica"
                    + "&order[relevance]=desc";
            var txt = Net.GetText(url, 15000);
            if (string.IsNullOrEmpty(txt)) { if (log != null) log("MangaDex 没有响应"); return null; }

            var root = MiniJson.Parse(txt) as Dictionary<string, object>;
            var list = new List<Candidate>();
            foreach (var it in Objs(Get(root, "data")))
            {
                var attr = Get(it, "attributes");
                var titles = Get(attr, "title");
                var zhTitle = FirstStr(titles, "zh", "zh-hk");
                list.Add(new Candidate
                {
                    Source = MangaDex,
                    Id = S(it, "id"),
                    Title = zhTitle.Length > 0 ? zhTitle : BestTitle(titles),
                    TitleAlt = AltTitle(attr),
                    Aliases = AllTitles(attr),
                    Year = S(attr, "year"),
                    Type = S(attr, "originalLanguage"),
                    CoverUrl = CoverOf(it),
                    Summary = Clean(FirstStr(Get(attr, "description"), "zh", "zh-hk", "en", "ja"))
                });
            }
            return list;
        }

        static Metadata FetchMangaDex(string id)
        {
            var url = "https://api.mangadex.org/manga/" + id
                    + "?includes[]=cover_art&includes[]=author&includes[]=artist";
            var txt = Net.GetText(url, 15000);
            if (string.IsNullOrEmpty(txt)) return null;
            var root = MiniJson.Parse(txt) as Dictionary<string, object>;
            var it = Get(root, "data");
            if (it == null) return null;

            var attr = Get(it, "attributes");
            var m = new Metadata { SourceName = LabelOf(MangaDex), SourceId = id };
            var titles = Get(attr, "title");
            SetNames(m, FirstStr(titles, "zh", "zh-hk"), FirstStr(titles, "ja"), BestTitle(titles));

            var desc = Get(attr, "description");
            m.Summary = Clean(FirstStr(desc, "zh", "zh-hk", "en", "ja"));
            m.Year = S(attr, "year");
            FillCovers(m, it);
            m.Web = "https://mangadex.org/title/" + id;
            m.SourceUrl = m.Web;

            // originalLanguage 只用来推阅读方向（日式右开 / 韩式左开），不写进 LanguageISO：
            // 那是原作语言，不是用户收藏的这一版的语言。
            var lang = S(attr, "originalLanguage");
            if (lang == "ja") m.Manga = "YesAndRightToLeft";
            else if (lang == "ko" || lang == "zh" || lang == "zh-hk") m.Manga = "No";

            // 分级：contentRating 是 MangaDex 明确给的，比猜标签可靠
            var cr = S(attr, "contentRating");
            if (cr == "safe") m.AgeRating = "Everyone";
            else if (cr == "suggestive") m.AgeRating = "Teen";
            else if (cr == "erotica") m.AgeRating = "Mature";
            else if (cr == "pornographic") m.AgeRating = "Adults Only";

            var tags = new List<string>();
            foreach (var t in Objs(Get(attr, "tags")))
            {
                var name = Get(Get(t, "attributes"), "name");     // {en:.., ja:..} 字典
                var en = S(name, "en");
                if (string.IsNullOrEmpty(en)) en = S(name, "ja");
                if (!string.IsNullOrEmpty(en)) tags.Add(en);
            }
            if (tags.Count > 0) m.Genre = string.Join(", ", tags.Take(8).ToArray());

            var writers = new List<string>();
            var artists = new List<string>();
            foreach (var rel in Objs(Get(it, "relationships")))
            {
                var type = S(rel, "type");
                var name = S(Get(rel, "attributes"), "name");
                if (string.IsNullOrEmpty(name)) continue;
                if (type == "author" && !writers.Contains(name)) writers.Add(name);
                else if (type == "artist" && !artists.Contains(name)) artists.Add(name);
            }
            m.Writer = string.Join(", ", writers.ToArray());
            m.Penciller = string.Join(", ", artists.ToArray());
            Finalize(m);
            return m;
        }

        /// <summary>标题字典里按 zh → ja-ro → en → 第一个 的顺序取。</summary>
        static string BestTitle(object titleDict)
        {
            var d = titleDict as Dictionary<string, object>;
            if (d == null) return "";
            foreach (var key in new[] { "zh", "zh-hk", "ja-ro", "en", "ja", "ko" })
            {
                var v = S(d, key);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            foreach (var kv in d)
            {
                var v = kv.Value as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return "";
        }

        /// <summary>把 title 各语言值 + 全部 altTitles 拼成一行，供匹配打分用。</summary>
        static string AllTitles(object attr)
        {
            var parts = new List<string>();
            var main = Get(attr, "title") as Dictionary<string, object>;
            if (main != null)
                foreach (var kv in main)
                {
                    var v = kv.Value as string;
                    if (!string.IsNullOrEmpty(v) && !parts.Contains(v)) parts.Add(v);
                }

            foreach (var a in Objs(Get(attr, "altTitles")))
            {
                var d = a as Dictionary<string, object>;
                if (d == null) continue;
                foreach (var kv in d)
                {
                    var v = kv.Value as string;
                    if (!string.IsNullOrEmpty(v) && !parts.Contains(v)) parts.Add(v);
                }
            }
            return string.Join(" | ", parts.ToArray());
        }

        /// <summary>候选列表里显示的第二标题：取第一个异于主标题的 altTitle。</summary>
        static string AltTitle(object attr)
        {
            var main = BestTitle(Get(attr, "title"));
            foreach (var a in Objs(Get(attr, "altTitles")))
            {
                var d = a as Dictionary<string, object>;
                if (d == null) continue;
                foreach (var kv in d)
                {
                    var v = kv.Value as string;
                    if (!string.IsNullOrEmpty(v) && v != main) return v;
                }
            }
            return "";
        }

        static string CoverOf(object manga)
        {
            var id = S(manga, "id");
            string first = "", zh = "";
            foreach (var rel in Objs(Get(manga, "relationships")))
            {
                if (S(rel, "type") != "cover_art") continue;
                var attrs = Get(rel, "attributes");
                var file = S(attrs, "fileName");
                if (string.IsNullOrEmpty(file)) continue;
                var url = "https://uploads.mangadex.org/covers/" + id + "/" + file + ".512.jpg";
                if (first.Length == 0) first = url;
                var loc = S(attrs, "locale");
                if ((loc == "zh" || loc == "zh-hk") && zh.Length == 0) zh = url;
            }
            return zh.Length > 0 ? zh : first;
        }

        /// <summary>
        /// MangaDex 的封面是按语言分开的：locale=zh 是中文版、ja 是原版。
        /// 这里把两种都存下来（全分辨率，不带 .512 后缀），写入时按用户选的版本取。
        /// </summary>
        static void FillCovers(Metadata m, object manga)
        {
            var id = S(manga, "id");
            string first = "", zh = "", ja = "";
            foreach (var rel in Objs(Get(manga, "relationships")))
            {
                if (S(rel, "type") != "cover_art") continue;
                var attrs = Get(rel, "attributes");
                var file = S(attrs, "fileName");
                if (string.IsNullOrEmpty(file)) continue;

                var url = "https://uploads.mangadex.org/covers/" + id + "/" + file;
                if (first.Length == 0) first = url;
                var loc = S(attrs, "locale");
                if ((loc == "zh" || loc == "zh-hk") && zh.Length == 0) zh = url;
                if (loc == "ja" && ja.Length == 0) ja = url;
            }
            m.CoverUrlZh = zh;
            m.CoverUrlOriginal = ja.Length > 0 ? ja : first;
            m.CoverUrl = zh.Length > 0 ? zh : m.CoverUrlOriginal;
        }

        // ------------------------------------------------------------ JSON 小工具
        static object Get(object dict, string key)
        {
            var d = dict as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        static string S(object dict, string key)
        {
            var v = Get(dict, key);
            if (v == null) return "";
            if (v is string) return ((string)v).Trim();
            if (v is long || v is double) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return "";
        }

        /// <summary>多语言字典里按顺序取第一个非空值（MangaDex 的 title / description 都是这种结构）。</summary>
        static string FirstStr(object dict, params string[] keys)
        {
            foreach (var k in keys)
            {
                var v = S(dict, k);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return "";
        }

        static IEnumerable<Dictionary<string, object>> Objs(object arr)        {
            var res = new List<Dictionary<string, object>>();
            var a = arr as object[];
            if (a == null) return res;
            foreach (var o in a)
            {
                var d = o as Dictionary<string, object>;
                if (d != null) res.Add(d);
            }
            return res;
        }

        static string YearOf(string date)
        {
            if (string.IsNullOrEmpty(date)) return "";
            var m = Regex.Match(date, "(19|20)\\d{2}");
            return m.Success ? m.Value : "";
        }

        static string FirstInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var m = Regex.Match(s, "\\d+");
            return m.Success ? m.Value : "";
        }

        /// <summary>把「A、B」拆开再用 " / " 合并，避免同一个字段里两种分隔符混着。</summary>
        static string JoinDelimited(string a, string b)
        {
            foreach (var part in SplitWords(b))
            {
                var p = part.Trim();
                if (p.Length == 0) continue;
                if (string.IsNullOrEmpty(a)) a = p;
                else if (a.IndexOf(p, StringComparison.Ordinal) < 0) a += " / " + p;
            }
            return a;
        }

        static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b) || a.IndexOf(b, StringComparison.Ordinal) >= 0) return a;
            return a + ", " + b;
        }

        /// <summary>简介里的 HTML 与换行去掉：ComicInfo 是纯文本字段，塞标签进去只会在阅读器里露出来。</summary>
        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = Regex.Replace(s, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "</p>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "<[^>]+>", "");
            t = t.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&quot;", "\"")
                 .Replace("&#39;", "'").Replace("&lt;", "<").Replace("&gt;", ">");
            t = Regex.Replace(t, "[ \\t]+", " ");
            t = Regex.Replace(t, "\\n{3,}", "\n\n");
            return t.Trim();
        }
    }
}
