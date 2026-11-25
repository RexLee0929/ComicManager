using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ComicManager
{
    /// <summary>一卷的 ISBN。来源只按系列给一条时，靠它把书号发到每一本上。</summary>
    public sealed class VolumeIsbn
    {
        public string Volume = "";
        public string Title = "";
        public string Gtin = "";

        /// <summary>这一卷（单行本子条目）自己的页面地址，写进那一本的 Web。</summary>
        public string Url = "";
    }

    /// <summary>其他数据库里的同一条目：名字 + id + 链接（链接要写进 Notes 给 Kavita 认）。</summary>
    public sealed class ExternalId
    {
        public string Name = "";
        public string Id = "";
        public string Url = "";
    }
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
    /// <summary>外部数据库 id 的查询结果。只带 id，不碰别的字段。</summary>
    public sealed class ExternalIdResult
    {
        public string AniListId = "", MalId = "", MangaBakaId = "", HardcoverId = "",
                      ComicVineId = "", MetronId = "", ComicBookRoundupId = "";
        public string Matched = "";
        public double Score;
        public readonly List<string> Titles = new List<string>();
        public readonly List<string> Missed = new List<string>();

        public bool Any
        {
            get
            {
                return AniListId.Length + MalId.Length + MangaBakaId.Length
                     + HardcoverId.Length + ComicVineId.Length + MetronId.Length
                     + ComicBookRoundupId.Length > 0;
            }
        }

        public string Describe()
        {
            var parts = new List<string>();
            if (AniListId.Length > 0) parts.Add("AniList " + AniListId);
            if (MalId.Length > 0) parts.Add("MAL " + MalId);
            if (MangaBakaId.Length > 0) parts.Add("MangaBaka " + MangaBakaId);
            if (HardcoverId.Length > 0) parts.Add("Hardcover " + HardcoverId);
            if (ComicVineId.Length > 0) parts.Add("ComicVine " + ComicVineId);
            if (MetronId.Length > 0) parts.Add("Metron " + MetronId);
            if (ComicBookRoundupId.Length > 0) parts.Add("CBR " + ComicBookRoundupId);
            return string.Join("、", parts.ToArray());
        }

        /// <summary>只把查到的 id 写进元数据，原有字段一律不动。</summary>
        public void ApplyTo(Metadata m)
        {
            if (m == null) return;
            if (AniListId.Length > 0) m.AniListId = AniListId;
            if (MalId.Length > 0) m.MalId = MalId;
            if (MangaBakaId.Length > 0) m.MangaBakaId = MangaBakaId;
            if (HardcoverId.Length > 0) m.HardcoverId = HardcoverId;
            if (ComicVineId.Length > 0) m.ComicVineId = ComicVineId;
            if (MetronId.Length > 0) m.MetronId = MetronId;
            if (ComicBookRoundupId.Length > 0) m.ComicBookRoundupId = ComicBookRoundupId;
        }
    }

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

        // ------------------------------------------------------------ 每卷 ISBN
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<VolumeIsbn>> VolIsbnCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, List<VolumeIsbn>>();

        /// <summary>
        /// 取一个系列里每卷的 ISBN。
        /// Bangumi 的系列条目（书籍、册数 N）下面挂着 N 个「单行本」子条目，每个子条目的
        /// infobox 里有自己的 ISBN —— 这是唯一能拿到「每卷一条书号」的地方；
        /// 系列条目本身通常连 ISBN 字段都没有（多卷本本来就没有系列级 ISBN）。
        /// </summary>
        public static List<VolumeIsbn> FetchVolumeIsbns(string source, string id, Action<string> log)
        {
            var res = new List<VolumeIsbn>();
            if (string.IsNullOrEmpty(id)) return res;
            if (source != Bangumi) return res;      // 只有 Bangumi 给单行本级 ISBN

            List<VolumeIsbn> cached;
            if (VolIsbnCache.TryGetValue(id, out cached)) return cached;

            var txt = Net.GetText("https://api.bgm.tv/v0/subjects/" + id + "/subjects", 15000);
            if (string.IsNullOrEmpty(txt)) return res;

            var vols = new List<KeyValuePair<string, string>>();   // 卷号 → 子条目 id
            foreach (var it in Objs(MiniJson.Parse(txt)))
            {
                var relation = S(it, "relation");
                if (S(it, "type") != "1" || relation.IndexOf("单行本", StringComparison.Ordinal) < 0) continue;

                var v = VolumeFromName(S(it, "name"));
                var sid = S(it, "id");
                if (v.Length == 0 || sid.Length == 0) continue;
                if (!vols.Any(x => x.Key == v)) vols.Add(new KeyValuePair<string, string>(v, sid));
            }
            if (vols.Count == 0) { VolIsbnCache[id] = res; return res; }

            var got = new Dictionary<string, VolumeIsbn>();
            int idx = -1;
            var threads = new List<Thread>();
            for (int t = 0; t < Math.Min(4, vols.Count); t++)
            {
                var th = new Thread(delegate()
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref idx);
                        if (i >= vols.Count) break;
                        var gtin = FetchOneIsbn(vols[i].Value);
                        // 就算这一卷没有 ISBN，链接也有用：单册页要能跳到它自己的条目
                        var one = new VolumeIsbn
                        {
                            Volume = vols[i].Key,
                            Gtin = gtin,
                            Url = "https://bgm.tv/subject/" + vols[i].Value
                        };
                        if (one.Gtin.Length == 0 && one.Url.Length == 0) continue;
                        lock (got) got[vols[i].Key] = one;
                    }
                });
                th.IsBackground = true;
                threads.Add(th);
                th.Start();
            }
            foreach (var th in threads) th.Join();

            foreach (var kv in got) res.Add(kv.Value);
            res.Sort(delegate(VolumeIsbn a, VolumeIsbn b)
            {
                int x, y;
                int.TryParse(a.Volume, out x); int.TryParse(b.Volume, out y);
                return x.CompareTo(y);
            });

            if (log != null)
            {
                int withIsbn = res.Count(x => x.Gtin.Length > 0);
                log("Bangumi 单行本里查到 " + withIsbn + " 条 ISBN、" + res.Count
                    + " 条单册链接（共 " + vols.Count + " 卷）");
            }
            VolIsbnCache[id] = res;
            return res;
        }

        static string FetchOneIsbn(string subjectId)
        {
            var txt = Net.GetText("https://api.bgm.tv/v0/subjects/" + subjectId, 15000);
            if (string.IsNullOrEmpty(txt)) return "";
            var d = MiniJson.Parse(txt) as Dictionary<string, object>;
            if (d == null) return "";

            string isbn10 = "";
            foreach (var box in Objs(Get(d, "infobox")))
            {
                var key = S(box, "key");
                var val = InfoboxValue(Get(box, "value"));
                if (IndexOfIgnore(key, "isbn-10") >= 0) { isbn10 = FirstDigits(val); continue; }
                if (IndexOfIgnore(key, "isbn") >= 0)
                {
                    var d13 = FirstDigits(val);
                    if (d13.Length == 13) return d13;
                    if (d13.Length == 10) return Isbn10To13(d13);
                }
            }
            return isbn10.Length == 10 ? Isbn10To13(isbn10) : "";
        }

        /// <summary>ISBN-10 → ISBN-13（978 + 前 9 位 + 重算校验位）。ComicInfo 的 GTIN 按 13 位存。</summary>
        static string Isbn10To13(string isbn10)
        {
            var d = Regex.Replace(isbn10 ?? "", "[^0-9Xx]", "");
            if (d.Length != 10) return isbn10 ?? "";
            var core = "978" + d.Substring(0, 9);
            int sum = 0;
            for (int i = 0; i < 12; i++) sum += (core[i] - '0') * (i % 2 == 0 ? 1 : 3);
            return core + ((10 - sum % 10) % 10);
        }

        /// <summary>单行本条目名里的卷号：「… (03)」「… 3巻」「… 第3巻」。</summary>
        static string VolumeFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var m = Regex.Match(name, @"[\(（]\s*0*(\d{1,3})\s*[\)）]\s*$");
            if (!m.Success) m = Regex.Match(name, @"第?\s*0*(\d{1,3})\s*[巻卷册冊]\s*$");
            if (!m.Success) m = Regex.Match(name, @"(\d{1,3})\s*$");
            if (!m.Success) return "";
            int v;
            if (!int.TryParse(m.Groups[1].Value, out v) || v <= 0) return "";
            return v.ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------ 额外数据库 id
        /// <summary>
        /// 外部数据库 id：AniList / MAL / MangaBaka 能直接查（公开接口，不要 key），
        /// Hardcover / ComicVine / Metron / Comic Book Roundup 要注册 key，这里查不了。
        ///
        /// 为什么要单独一步：Kavita 的「外部元数据」显示的是 AniList / MAL / ComicVine 这些库的 id，
        /// 而 Bangumi / MangaUpdates / MangaDex 三个源里根本没有这些数字 ——
        /// 只能拿已经刮到的「原名 / 英文名」去对应的库里反查。
        /// </summary>
        public static ExternalIdResult FindExternalIds(List<string> terms, Action<string> log)
        {
            var res = new ExternalIdResult();
            var list = new List<string>();
            foreach (var t in terms)
            {
                var s = (t ?? "").Trim();
                if (s.Length > 0 && !list.Contains(s)) list.Add(s);
            }
            if (list.Count == 0) return res;

            LookupAniList(list, res, log);
            LookupMangaBaka(list, res, log);

            res.Missed.Add("Hardcover（要 API key）");
            res.Missed.Add("ComicVine（要 API key）");
            res.Missed.Add("Metron（要账号）");
            res.Missed.Add("Comic Book Roundup（无公开接口）");

            if (log != null)
                log(res.Any ? "外部 id：" + res.Describe() : "外部 id：没找到");
            return res;
        }

        const string AniListQuery =
            "query($s:String){Page(perPage:6){media(search:$s,type:MANGA){id idMal "
            + "title{romaji english native}}}}";

        /// <summary>AniList 公开 GraphQL，不用 key；顺带把 idMal 带回来，MAL 那条也就有了。</summary>
        static void LookupAniList(List<string> terms, ExternalIdResult res, Action<string> log)
        {
            foreach (var term in terms)
            {
                if (!string.IsNullOrEmpty(res.AniListId)) break;
                try
                {
                    var body = "{\"query\":" + MiniJson.Serialize(AniListQuery)
                             + ",\"variables\":{\"s\":" + MiniJson.Serialize(term) + "}}";
                    var txt = Net.PostJson("https://graphql.anilist.co", body, 20000);
                    if (string.IsNullOrEmpty(txt)) continue;

                    var root = MiniJson.Parse(txt) as Dictionary<string, object>;
                    var page = Get(Get(root, "data"), "Page");
                    double best = 0; Dictionary<string, object> pick = null;
                    foreach (var it in Objs(Get(page, "media")))
                    {
                        var t = Get(it, "title");
                        var score = Score(term, S(t, "romaji"), S(t, "english"), S(t, "native"));
                        if (score > best) { best = score; pick = it; }
                    }
                    if (pick == null || best < 0.72) continue;

                    res.AniListId = S(pick, "id");
                    res.MalId = S(pick, "idMal");
                    var title = Get(pick, "title");
                    res.Titles.Add(S(title, "romaji"));
                    res.Titles.Add(S(title, "english"));
                    res.Titles.Add(S(title, "native"));
                    if (res.Matched.Length == 0) res.Matched = S(title, "english");
                    if (best > res.Score) res.Score = best;
                    if (log != null)
                        log("AniList 命中：" + S(title, "romaji") + "（" + Math.Round(best * 100) + "%）");
                }
                catch (Exception ex) { if (log != null) log("查 AniList 失败：" + ex.Message); }
            }
        }

        /// <summary>MangaBaka 公开 REST（旧域名 api.mangabaka.dev 已停用，走 .org）。</summary>
        static void LookupMangaBaka(List<string> terms, ExternalIdResult res, Action<string> log)
        {
            foreach (var term in terms)
            {
                if (!string.IsNullOrEmpty(res.MangaBakaId)) break;
                try
                {
                    var url = "https://api.mangabaka.org/v1/series/search?limit=6&q=" + Uri.EscapeDataString(term);
                    var txt = Net.GetText(url, 20000);
                    if (string.IsNullOrEmpty(txt)) continue;

                    var root = MiniJson.Parse(txt) as Dictionary<string, object>;
                    double best = 0; Dictionary<string, object> pick = null;
                    foreach (var it in Objs(Get(root, "data")))
                    {
                        var alts = new List<string>();
                        alts.Add(S(it, "native_title"));
                        var sec = Get(it, "secondary_titles") as Dictionary<string, object>;
                        if (sec != null)
                            foreach (var kv in sec)
                                foreach (var one in Objs(kv.Value))
                                    alts.Add(S(one, "title"));
                        var score = Score(term, S(it, "title"), S(it, "romanized_title"),
                                          string.Join("|", alts.ToArray()));
                        if (score > best) { best = score; pick = it; }
                    }
                    if (pick == null || best < 0.72) continue;

                    res.MangaBakaId = S(pick, "id");
                    res.Titles.Add(S(pick, "title"));
                    res.Titles.Add(S(pick, "native_title"));
                    if (res.Matched.Length == 0) res.Matched = S(pick, "title");
                    if (best > res.Score) res.Score = best;
                    if (log != null)
                        log("MangaBaka 命中：" + S(pick, "title") + "（" + Math.Round(best * 100) + "%）");
                }
                catch (Exception ex) { if (log != null) log("查 MangaBaka 失败：" + ex.Message); }
            }
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
        /// 三种名字各留一份：中文译名 / 原名 / 英文名。
        /// 显示名取中文优先 —— 这是你自己看的名字；另外两个库里查不到中文名时还要靠它们反查，
        /// 所以不能只留一个。
        /// </summary>
        static void SetNames(Metadata m, string zh, string original, string english, string fallback)
        {
            m.SeriesChinese = (zh ?? "").Trim();
            m.SeriesOriginal = (original ?? "").Trim();
            m.SeriesEnglish = (english ?? "").Trim();

            var display = m.SeriesChinese.Length > 0 ? m.SeriesChinese
                        : (m.SeriesOriginal.Length > 0 ? m.SeriesOriginal
                        : (m.SeriesEnglish.Length > 0 ? m.SeriesEnglish : (fallback ?? "")));
            m.Series = display;
            // Kavita 的本地化名：显示的不是中文时把中文名放这里；两者相同就留空
            m.LocalizedSeries = (m.SeriesChinese.Length > 0 && m.SeriesChinese != display) ? m.SeriesChinese : "";
            m.SeriesSort = display;
        }

        /// <summary>纯拉丁字母（英文 / 罗马字），用来把「英文名」从别名堆里挑出来。</summary>
        static bool IsLatinTitle(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            bool any = false;
            foreach (var c in s)
            {
                if (c >= '\u0250') return false;      // 假名、汉字、谚文都不算
                if (char.IsLetter(c)) any = true;
            }
            return any;
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
            SetNames(m, nameCn, name, "", name);
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
                    // 别名里往往藏着英文名（有的作品 Bangumi 只在别名里写英文），
                    // 那是去 AniList / MangaBaka 反查时命中率最高的一个词，单独留下来
                    noise.AddRange(SplitWords(val));
                    foreach (var a in SplitWords(val))
                    {
                        var s = a.Trim();
                        if (s.Length < 2) continue;
                        if (IsLatinTitle(s)) { if (string.IsNullOrEmpty(m.SeriesEnglish)) m.SeriesEnglish = s; }
                        else if (HasKana(s)) { if (string.IsNullOrEmpty(m.SeriesOriginal)) m.SeriesOriginal = s; }
                    }
                }
                else if (key == "册数" || key == "卷数") { m.Count = FirstInt(val); }
                else if (key == "话数") { if (string.IsNullOrEmpty(m.Count)) m.Count = FirstInt(val); }
            }

            // 标签：全量保留，但要分「类型」和「标签」两批 —— 两边同值就不是分类了。
            // Bangumi 的标签没有类别信息，只能靠词表分：命中类型词表的进 Genre，
            // 其余（作品名、梗、状态、卷数、出版社…）进 Tags。词表宁全勿缺：
            // 漏一个只是掉进 Tags，不会丢，也不会像以前「前 6 个算类型」那样把无关词硬塞进分类。
            var tags = new List<string>();
            foreach (var t in Objs(Get(d, "tags")))
            {
                var tn = S(t, "name");
                if (!string.IsNullOrEmpty(tn) && !IsNoiseTag(tn, m, noise)) tags.Add(tn);
            }
            if (tags.Count > 0)
            {
                var genre = new List<string>();
                var rest = new List<string>();
                foreach (var t in tags)
                    (GenreWords.Contains(t.Trim()) ? genre : rest).Add(t);
                m.Genre = string.Join(", ", genre.ToArray());
                m.Tags = string.Join(", ", rest.ToArray());
            }

            var volumes = S(d, "volumes");
            if (string.IsNullOrEmpty(m.Count) && volumes != "0") m.Count = volumes;

            // 合集/书单：infobox 的「系列」只有一部分条目填过。
            // 没填就按关联条目推：单行本取「系列」关系，主条目取同 IP 的动画/游戏名。
            if (string.IsNullOrEmpty(m.SeriesGroup)) m.SeriesGroup = BangumiSeriesGroup(id, m);

            // 语言不猜：LanguageISO 表示「你收藏的这一本是什么语言」，
            // 而 Bangumi 记的是原作的语言。中文版漫画被标成 ja，Komga 里筛选就全乱了。
            // 语言来源只有两个：包内已有的值、用户在下拉里选的。
            Finalize(m);
            return m;
        }

        /// <summary>
        /// 合集 / 书单的名字。两级判断：
        /// 1. 关联里有 relation=系列 的（单行本 → 主系列），直接拿来用；
        /// 2. 否则在关联的**动画 / 游戏**条目里挑「IP 主名」：必须是当前系列名的前缀，
        ///    且不能跟当前作品同名（改编游戏的标题常和漫画一模一样，那不叫合集）。
        ///    多个候选取最短的 —— 「新世纪福音战士」比「新世纪福音战士 钢铁的女朋友」更像合集名。
        /// 都不满足就留空，不硬凑一个错合集。
        /// </summary>
        static string BangumiSeriesGroup(string id, Metadata m)
        {
            var txt = Net.GetText("https://api.bgm.tv/v0/subjects/" + id + "/subjects", 15000);
            if (string.IsNullOrEmpty(txt)) return "";
            var list = MiniJson.Parse(txt) as object[];
            if (list == null) return "";

            // 1. 明确的「系列」关系
            foreach (var o in list)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                if (IndexOfIgnore(S(d, "relation"), "系列") < 0) continue;
                var n = S(d, "name_cn");
                if (n.Length == 0) n = S(d, "name");
                if (n.Length > 0) return n;
            }

            // 2. IP 主名。只认动画(2) / 游戏(4)：书籍类多半是单行本或公式书，拿来当合集必错。
            var selves = new List<string>();
            foreach (var s in new[] { m.Series, m.SeriesChinese, m.SeriesOriginal })
            {
                var v = (s ?? "").Trim();
                if (v.Length > 0 && !selves.Contains(v)) selves.Add(v);
            }

            string best = "";
            foreach (var o in list)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var type = S(d, "type");
                if (type != "2" && type != "4") continue;

                var n = S(d, "name_cn");
                if (n.Length == 0) n = S(d, "name");
                if (n.Length == 0) continue;

                if (!IsIpHolder(n, selves)) continue;
                if (best.Length == 0 || n.Length < best.Length) best = n;
            }
            return best;
        }

        /// <summary>
        /// 候选名是不是「当前作品的上级 IP」：当前系列名以它开头，且比它长。
        /// 「新世纪福音战士」是「新世纪福音战士 碇真嗣育成计划」的前缀 → 可以当合集；
        /// 同名条目（改编游戏）归一化后与当前名等长 → 不算。
        /// </summary>
        static bool IsIpHolder(string candidate, List<string> selves)
        {
            var c = Norm(candidate);
            if (c.Length < 2) return false;
            foreach (var s in selves)
            {
                var v = Norm(s);
                if (v.Length <= c.Length) continue;
                if (v.StartsWith(c, StringComparison.Ordinal)) return true;
            }
            return false;
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

        /// <summary>
        /// 类型词表：命中这里的标签算「类型」进 Genre，其余算「标签」进 Tags。
        /// Bangumi 的标签没有类别字段，只能靠词表分。宁全勿缺：漏掉的只是掉进 Tags，
        /// 不会丢，也不会像旧版「前 6 个算类型」那样把「全18卷」「补标」硬塞进分类。
        /// 简繁都收，日文常用写法也收 —— 同一个条目不同人打的字不一样。
        /// </summary>
        static readonly HashSet<string> GenreWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 大方向
            "剧情", "故事", "叙事", "群像", "热血", "黑暗", "虐心", "黑深残", "励志", "燃",
            // 恋爱
            "恋爱", "戀愛", "恋愛", "爱情", "愛情", "純愛", "纯爱", "青梅竹马", "三角恋", "后宫", "ハーレム", "逆后宫",
            // 喜剧
            "喜剧", "喜劇", "搞笑", "幽默", "コメディ", "恶搞", "惡搞", "吐槽", "四格",
            // 校园 / 青春
            "校园", "校園", "學園", "学園", "学校", "青春", "社团", "社團",
            // 日常 / 治愈
            "日常", "治愈", "治癒", "空氣系", "空气系", "温馨", "溫馨",
            // 奇幻
            "奇幻", "ファンタジー", "魔法", "魔法少女", "异世界", "異世界", "转生", "轉生", "妖怪", "怪物",
            "神话", "神話", "恶魔", "惡魔", "吸血鬼", "僵尸", "喪屍", "丧尸", "龙", "龍",
            // 科幻
            "科幻", "SF", "赛博朋克", "賽博龐克", "废土", "廢土", "末日", "末世", "未来", "未來", "时空", "時空",
            // 机甲 / 战斗
            "机战", "機戰", "机器人", "機器人", "机甲", "機甲", "メカ", "战斗", "戰鬥", "バトル", "动作", "動作",
            "アクション", "格斗", "格鬥", "武斗", "武侠", "武俠",
            // 冒险
            "冒险", "冒險", "冒険", "探险", "探險",
            // 悬疑 / 恐怖
            "悬疑", "懸疑", "推理", "ミステリ", "侦探", "偵探", "惊悚", "驚悚", "サスペンス", "恐怖", "horror", "猎奇", "獵奇", "グロ",
            // 运动 / 音乐 / 美食
            "运动", "運動", "スポーツ", "体育", "體育", "竞技", "競技", "音乐", "音樂", "音楽", "乐队", "樂隊",
            "美食", "グルメ", "料理", "烹饪",
            "篮球", "足球", "棒球", "网球", "排球", "游泳", "拳击", "剑道", "柔道", "空手道", "相扑", "田径",
            "登山", "钓鱼", "赛车", "自行车", "围棋", "将棋", "麻将", "电竞", "舞蹈", "摄影", "美术",
            // 社会向
            "职场", "職場", "仕事", "社畜", "历史", "歷史", "歴史", "军事", "軍事", "战争", "戰爭", "戦争",
            "政治", "犯罪", "黑帮", "黑幫", "黑道", "不良", "极道", "極道", "悬疑推理",
            // 情感向
            "情感", "催泪", "催淚", "感动", "感動", "致郁", "鬱",
            // 百合 / BL
            "百合", "GL", "耽美", "BL", "Yaoi", "Yuri",
            // 设定 / 属性
            "超能力", "能力", "异能", "異能", "变身", "變身", "变装", "變裝", "女装", "女裝", "偽娘", "伪娘",
            "猫娘", "獸耳", "兽耳", "时间循环", "平行世界", "轮回", "灵异", "靈異", "超自然", "幽灵", "咒术",
            "忍者", "武士", "骑士", "海盗", "宫廷", "王族", "贵族", "宿舍", "社团", "打工", "克苏鲁", "蒸汽朋克",
            // 行业 / 职业
            "偶像", "演艺", "娱乐圈", "声优", "动画制作", "游戏制作", "漫画业界", "编辑", "医疗", "医生",
            "律师", "警察", "教师", "厨师", "旅行", "文学", "心理", "社会", "经济", "商业", "金融",
            // 受众 / 分级
            "少年", "少女", "青年", "成人", "成人向", "R-18", "エロ", "18禁", "一般向", "全年龄", "全年齡"
        };

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
            // MU 的主标题一般是罗马字/英文，先当作英文名
            if (IsLatinTitle(m.Series)) m.SeriesEnglish = m.Series;
            foreach (var a in Objs(Get(d, "associated")))
            {
                var n = S(a, "title");
                if (!string.IsNullOrEmpty(n) && !alts.Contains(n)) alts.Add(n);
            }
            // 关联作品名里挑：带假名的当日文原名、纯汉字的当中文名。
            // MU 主标题是罗马字/英文，中文名和原名都只可能出现在这里。
            string zh = "", ja = "";
            foreach (var a in alts)
            {
                if (a == m.Series) continue;
                if (HasKana(a)) { if (ja.Length == 0) ja = a; }
                else if (HasCjk(a)) { if (zh.Length == 0) zh = a; }
                else if (IsLatinTitle(a)) { if (string.IsNullOrEmpty(m.SeriesEnglish)) m.SeriesEnglish = a; }
            }
            SetNames(m, zh, ja, m.SeriesEnglish, m.Series);

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
            SetNames(m, FirstStr(titles, "zh", "zh-hk"), FirstStr(titles, "ja", "ko"),
                     FirstStr(titles, "en"), BestTitle(titles));

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

            // 标签：MangaDex 自带分组（genre / theme / format / content），
            // 正好能把「类型」与「题材」分开，而且这里一个都不截断。
            var genres = new List<string>();
            var extras = new List<string>();
            foreach (var t in Objs(Get(attr, "tags")))
            {
                var ta = Get(t, "attributes");
                var name = Get(ta, "name");     // {en:.., ja:..} 字典
                var en = S(name, "en");
                if (string.IsNullOrEmpty(en)) en = S(name, "ja");
                if (string.IsNullOrEmpty(en)) continue;
                if (string.Equals(S(ta, "group"), "genre", StringComparison.OrdinalIgnoreCase)) genres.Add(en);
                else extras.Add(en);
            }
            if (genres.Count == 0 && extras.Count > 0) { genres.AddRange(extras); extras.Clear(); }
            if (genres.Count > 0) m.Genre = string.Join(", ", genres.ToArray());
            if (extras.Count > 0) m.Tags = string.Join(", ", extras.ToArray());

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

        /// <summary>
        /// 简介里的 HTML 与换行去掉：ComicInfo 是纯文本字段，塞标签进去只会在阅读器里露出来。
        ///
        /// 换行先统一成 \n：源站（Bangumi 尤其）给的是 \r\n，而 XML 解析器读回来时
        /// 会把 CRLF 折成 LF —— 不统一的话，同一段简介写进去再读出来就"变"了，
        /// 「对比」每次都报一排假差异。
        /// </summary>
        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = s.Replace("\r\n", "\n").Replace('\r', '\n');
            t = Regex.Replace(t, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
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
