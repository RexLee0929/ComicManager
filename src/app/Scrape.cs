using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace ComicManager
{
    /// <summary>扫描到的一个文件。卷/话从包内 ComicInfo 或文件名解析，逐文件保留 —— 刮的是系列，写的是每本。</summary>
    public sealed class ScrapeFile
    {
        public string Path = "";
        public string Name = "";
        public string Volume = "";
        public string Number = "";
        public string Title = "";      // 单册标题：包内有就用包内的，没有从文件名括号里取
        public string Format = "";     // Special / Annual…：番外的标记，决定它算不算正传
        public string GTIN = "";       // ISBN：每本一个，系列级没有
        public string Web = "";        // 这一本自己的条目页（回读时带上，避免重写把单册链接冲掉）
        public string Summary = "";    // 本册简介：留空就用系列简介
        public string Label = "";      // 展示与命名用的册号：V01 / SP01
        public int Pages;
        public bool HasMeta;
        public string Err = "";

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>
            {
                {"path", Path}, {"name", Name},
                {"volume", Volume}, {"number", Number},
                {"title", Title}, {"format", Format}, {"gtin", GTIN}, {"label", Label},
                {"pages", Pages}, {"hasMeta", HasMeta}, {"err", Err},
                {"web", Web}, {"summary", Summary}
            };
        }
    }

    /// <summary>一个系列 = 一个文件夹。Komga/Kavita 都是这么认的，刮削也按这个粒度走。</summary>
    public sealed class ScrapeSeries
    {
        public string Dir = "";
        public string Name = "";
        public readonly List<ScrapeFile> Files = new List<ScrapeFile>();
        public bool HasMeta;
        public Metadata Existing;      // 第一个文件的既有元数据，给「读回现有」用

        public Dictionary<string, object> ToJson()
        {
            var files = new List<object>();
            foreach (var f in Files) files.Add(f.ToJson());
            return new Dictionary<string, object>
            {
                {"dir", Dir}, {"name", Name},
                {"hasMeta", HasMeta}, {"files", files.ToArray()},
                {"existing", Scrape.MetaToJson(Existing)}
            };
        }
    }

    /// <summary>一次写入任务：一个系列 + 用户确认后的元数据。</summary>
    public sealed class ScrapeJob
    {
        public string Dir = "";
        public string Series = "";
        public Metadata Meta = new Metadata();
        public int TotalIssues;
        /// <summary>
        /// 这个系列是不是「一本一册」的多本系列。写入时决定系列级的号 / 标题 / 特殊类型 / 书号
        /// 要不要清掉 —— 只挑一本写（仅应用本册）时，这个标志必须还是 true，
        /// 否则会把「系列级那份（往往来自第一本，也就是番外）的 Special 和标题」套到这一本上。
        /// </summary>
        public bool MultiSeries;
        public readonly List<KeyValuePair<string, string>> Files = new List<KeyValuePair<string, string>>();  // path → "vol|num|title|format|gtin|web|summary"
        /// <summary>用户在界面上亲手改过的文件（和盘上原值不同）。写入时这些册关掉「程序替你改值」的规则。</summary>
        public readonly List<string> ManualFiles = new List<string>();
        public string Err = "";
    }

    /// <summary>
    /// 刮削侧的编排：扫描目录成系列、渲染预览、批量写入。
    /// 状态独立于「转换」任务（Api 里那一套是转换的）：两边可以各自看各自的进度，
    /// 但页面同时只跑一个 —— 界面上按页面切，后端按 running 标志挡。
    /// </summary>
    public static class Scrape
    {
        static readonly object Sync = new object();
        static readonly List<Dictionary<string, object>> Logs = new List<Dictionary<string, object>>();
        static bool _running;
        static string _phase = "待命";
        static int _total, _done, _ok, _err, _skip;
        static string _current = "";
        static volatile bool _stop;

        public static bool Running { get { lock (Sync) return _running; } }

        public static void Log(string msg, string level)
        {
            lock (Sync)
            {
                Logs.Add(new Dictionary<string, object>
                {
                    {"t", DateTime.Now.ToString("HH:mm:ss")},
                    {"m", msg != null && msg.Length > 400 ? msg.Substring(0, 400) : (msg ?? "")},
                    {"l", level}
                });
                if (Logs.Count > 900) Logs.RemoveRange(0, 300);
            }
        }

        public static Dictionary<string, object> Status()
        {
            lock (Sync)
            {
                return new Dictionary<string, object>
                {
                    {"running", _running}, {"phase", _phase},
                    {"total", _total}, {"done", _done},
                    {"ok", _ok}, {"err", _err}, {"skip", _skip},
                    {"current", _current},
                    {"log", Logs.ToArray()}
                };
            }
        }

        public static void ClearLog()
        {
            lock (Sync) Logs.Clear();
        }

        // ------------------------------------------------------------ 扫描
        /// <summary>
        /// 扫源目录，按「父文件夹 = 系列」分组。只收 .cbz：CBR 写不回去（RAR 不支持写入），
        /// 列出来也只是让人误点，不如在提示里说清楚。
        /// </summary>
        public static List<ScrapeSeries> Scan(List<string> sources, Action<string> log)
        {
            var map = new Dictionary<string, ScrapeSeries>(StringComparer.OrdinalIgnoreCase);
            var files = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in sources)
            {
                if (string.IsNullOrEmpty(s) || !Directory.Exists(s)) continue;
                // 统一成完整路径：扫描目录可能写成 E:/lib、也可能写成 E:\lib，
                // 混在一起会让后面的「改名前后的路径比较」永远不相等
                foreach (var f in Directory.GetFiles(s, "*.cbz", SearchOption.AllDirectories))
                {
                    string full;
                    try { full = Path.GetFullPath(f); } catch { full = f; }
                    if (seen.Add(full)) files.Add(full);
                }
            }
            files.Sort(StringComparer.Ordinal);
            if (log != null) log("找到 " + files.Count + " 个 CBZ");

            foreach (var f in files)
            {
                var dir = Path.GetDirectoryName(f) ?? "";
                ScrapeSeries series;
                if (!map.TryGetValue(dir, out series))
                {
                    series = new ScrapeSeries { Dir = dir, Name = Path.GetFileName(dir) };
                    map[dir] = series;
                }

                var sf = new ScrapeFile { Path = f, Name = Path.GetFileName(f) };
                try
                {
                    using (var fs = File.OpenRead(f))
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                    {
                        var meta = ComicInfo.ReadFromArchive(zip);
                        if (meta != null)
                        {
                            sf.HasMeta = true;
                            sf.Volume = meta.Volume;
                            sf.Number = meta.Number;
                            sf.Title = CleanTitle(meta.Title, meta.Series);
                            sf.Format = meta.Format;
                            sf.GTIN = meta.GTIN;
                            // ReadFromArchive 已经把外部 id 链接拆成字段，这里拿到的是干净的主链接
                            sf.Web = meta.Web;
                            sf.Summary = meta.Summary;
                            int pc;
                            if (int.TryParse(meta.PageCount, out pc)) sf.Pages = pc;
                            if (series.Existing == null) series.Existing = meta;
                            if (!string.IsNullOrEmpty(meta.Series)) series.Name = meta.Series;
                        }

                        if (sf.Pages == 0) sf.Pages = CountPages(zip);
                    }
                }
                catch (Exception ex)
                {
                    sf.Err = ex.Message;
                }

                // 文件名解析：号从这来，单册标题和「这是番外」也从这来。
                // 包内没有 Title / Format 时才用文件名补 —— 文件里已经写明的更可信。
                string stem = Path.GetFileNameWithoutExtension(f);
                string pname, plabel; int? pvol; bool pspecial;
                Core.SplitStem(stem, out pname, out pvol, out plabel, out pspecial);

                // 包里已经有 ComicInfo 就是「准的」：里面没有号，说明用户就是要它空着。
                // 只有压根没有元数据的老文件，才从文件名里补 —— 否则在界面上把「话/号」
                // 删掉、应用之后扫一遍又原样长回来，看着像根本没写进去。
                if (!sf.HasMeta && string.IsNullOrEmpty(sf.Volume) && string.IsNullOrEmpty(sf.Number)
                    && pvol.HasValue)
                {
                    sf.Volume = pvol.Value.ToString();
                    sf.Number = pvol.Value.ToString();
                }
                if (string.IsNullOrEmpty(sf.Title)) sf.Title = GuessTitle(stem, plabel);
                if (string.IsNullOrEmpty(sf.Format) && pspecial) sf.Format = "Special";
                if (sf.Volume.Length == 0) sf.Volume = sf.Number;
                if (sf.Number.Length == 0) sf.Number = sf.Volume;

                if (sf.HasMeta) series.HasMeta = true;
                series.Files.Add(sf);
            }

            foreach (var s in map.Values)
                s.Files.Sort(CompareFiles);

            // 文件名里既没有卷也没有话（比如 001.cbz、002.cbz 之外的乱名）时按顺序补号，
            // 否则多本会全写成同一个 Number，服务器上会看到一堆同号的书
            foreach (var s in map.Values) FillMissingNumbers(s);

            // 番外被旧版写成 x.5 的，这里还原成卷号：界面先显示对，写出去才不会再错一次
            foreach (var s in map.Values) HealSpecialNumbers(s);

            // 系列链接取「多数票」：existing 原本是第一本的元数据，如果只有某一册
            // 填了自己的条目页，它会被当成系列链接，写整组时再套给每一本。
            foreach (var s in map.Values) FixSeriesWeb(s);

            // 再把「等于系列链接」的单册链接清空：那是上一轮兜底写进去的，不是单册页。
            // 必须排在 FixSeriesWeb 之后 —— 系列链接得先定下来才知道哪些是它。
            foreach (var s in map.Values) ClearInheritedWeb(s);

            // 排好序再发册号：番外按出现顺序 SP01/SP02，正传按卷号 V01/V02
            foreach (var s in map.Values) AssignLabels(s);

            var list = map.Values.OrderBy(x => x.Dir, StringComparer.OrdinalIgnoreCase).ToList();
            if (log != null) log("归成 " + list.Count + " 个系列");
            return list;
        }

        /// <summary>
        /// 缺号的按排序补：整组都没号就从 1 开始，部分有号就接在最大值后面。
        /// 番外那种 x.5 的小数号不参与补号（解析时只认整数）。
        ///
        /// 只补「包里没有元数据」的老文件。带 ComicInfo 的文件，号空着是用户的决定
        /// （他把号删了），补上去等于把删除动作又撤销一遍。
        /// </summary>
        static void FillMissingNumbers(ScrapeSeries s)
        {
            int max = 0;
            foreach (var f in s.Files)
            {
                int v;
                if (int.TryParse(f.Volume, out v) && v > max) max = v;
            }
            foreach (var f in s.Files)
            {
                if (f.HasMeta) continue;
                if (!string.IsNullOrEmpty(f.Volume) && !string.IsNullOrEmpty(f.Number)) continue;
                max++;
                if (string.IsNullOrEmpty(f.Volume)) f.Volume = max.ToString();
                if (string.IsNullOrEmpty(f.Number)) f.Number = max.ToString();
            }
        }

        /// <summary>
        /// 系列级 Web = 各册里出现次数最多的那个链接（通常就是全组共用的作品主页）。
        /// 只有某一册不同（单册条目页）时，不会让它顶替系列链接。
        /// </summary>
        static void FixSeriesWeb(ScrapeSeries s)
        {
            if (s.Existing == null || s.Files.Count == 0) return;

            var best = "";
            var bestCount = 0;
            foreach (var g in s.Files.Where(f => !string.IsNullOrEmpty(f.Web))
                                     .GroupBy(f => f.Web, StringComparer.OrdinalIgnoreCase))
            {
                if (g.Count() > bestCount) { bestCount = g.Count(); best = g.Key; }
            }
            if (best.Length == 0) return;

            // 全组只有一本时，它就是系列（不区分单册/系列链接）
            s.Existing.Web = s.Files.Count == 1 ? s.Files[0].Web : best;
        }

        /// <summary>
        /// 单册链接只留「这一本自己的条目页」。写出去时没填单册链接的那一册本来就会
        /// 沿用它所在系列的链接，所以包里读回来的、与系列链接一字不差的那些，属于
        /// 上一轮的兜底结果，不是抓到的单册页 —— 不清掉的话：
        /// 一是单册那栏永远显示着系列链接，看着像抓错了；二是「每卷链接」再想补进来时
        /// 会被这段旧值挡住（界面只在单册链接空着时才填），永远补不进去。
        /// </summary>
        static void ClearInheritedWeb(ScrapeSeries s)
        {
            if (s.Files.Count < 2 || s.Existing == null) return;   // 单本系列：那一本本来就是系列
            var series = (s.Existing.Web ?? "").Trim();
            if (series.Length == 0) return;                        // 系列链接都没有，别把唯一线索也清掉
            foreach (var f in s.Files)
                if (string.Equals((f.Web ?? "").Trim(), series, StringComparison.OrdinalIgnoreCase))
                    f.Web = "";
        }

        /// <summary>
        /// 番外的号曾经被写成「卷号 - 0.5」（Komga 档插队用）。开关默认关掉以后，
        /// 这些残留的 0.5 / 1.5 要让用户看见真实的卷号，而不是以为库里的号就是错的。
        /// </summary>
        static void HealSpecialNumbers(ScrapeSeries s)
        {
            foreach (var f in s.Files)
            {
                var fmt = (f.Format ?? "");
                if (fmt.IndexOf("special", StringComparison.OrdinalIgnoreCase) < 0) continue;

                decimal n, v;
                if (!decimal.TryParse(f.Number, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out n)) continue;
                if (!decimal.TryParse(f.Volume, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v)) continue;
                if (v > 0 && Math.Abs(n - (v - 0.5m)) < 0.0001m) f.Number = f.Volume;
            }
        }

        static readonly System.Text.RegularExpressions.Regex NumOnlyRe =
            new System.Text.RegularExpressions.Regex(
                @"^(卷|第)?\s*0*\d{1,4}\s*(卷|话|話|册|冊)?$|^v0*\d{1,4}$|^#\d{1,4}$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// 单册标题只应该有「这一本独有的东西」。源文件常写成「系列名 - 卷01」，
        /// 系列名在 Series 里已经有了；「卷01」这种纯号也没信息，交给服务器拼「系列名 + 号」。
        /// 但「系列名 - 增刊+番外+序章」的后半截是有用的，要留住。
        /// </summary>
        static string CleanTitle(string title, string series)
        {
            var t = (title ?? "").Trim();
            if (t.Length == 0) return "";

            var sr = Squash(series);
            if (sr.Length > 0)
            {
                foreach (var sep in new[] { " - ", " – ", " — ", "：", ": ", "-", "_" })
                {
                    int i = t.IndexOf(sep, StringComparison.Ordinal);
                    if (i <= 0) continue;
                    if (Squash(t.Substring(0, i)) != sr) continue;
                    t = t.Substring(i + sep.Length).Trim();
                    break;
                }
            }

            if (t.Length == 0 || NumOnlyRe.IsMatch(t)) return "";
            return t;
        }

        /// <summary>只留字母数字，用来比「这一截是不是系列名」。</summary>
        static string Squash(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// 从文件名里取单册标题。优先用解析出的 label，没有就取末尾括号里的内容。
        /// 纯数字、纯年份、纯卷号都不算标题 —— 那些是号，不是书名。
        /// </summary>
        static string GuessTitle(string stem, string label)
        {
            var t = (label ?? "").Trim(' ', '-', '_', '(', ')', '（', '）');
            if (t.Length == 0)
            {
                var m = System.Text.RegularExpressions.Regex.Match(stem, @"[\(（]([^\)）]{1,60})[\)）]\s*$");
                if (m.Success) t = m.Groups[1].Value.Trim();
            }
            if (t.Length == 0) return "";
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[\d\s\-\.]+$")) return "";
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(19|20)\d{2}$")) return "";
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^v?0*\d{1,4}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "";
            return t;
        }

        /// <summary>
        /// 正传在前、番外在后；各自按卷号排，同号再按文件名。
        /// 之前一律按文件名排，番外（v01 (增刊…)）会插在正传 v01 前面，
        /// 列表看起来就是 SP1、v1、SP2、v2 这种交错。
        /// </summary>
        static int CompareFiles(ScrapeFile a, ScrapeFile b)
        {
            bool sa = IsSpecialFormat(a.Format), sb = IsSpecialFormat(b.Format);
            if (sa != sb) return sa ? 1 : -1;

            decimal va, vb;
            bool oka = decimal.TryParse(a.Volume, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out va);
            bool okb = decimal.TryParse(b.Volume, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out vb);
            if (oka && okb && va != vb) return va.CompareTo(vb);
            if (oka != okb) return oka ? -1 : 1;

            return CompareName(a.Name, b.Name);
        }

        static bool IsSpecialFormat(string fmt)
        {
            return !string.IsNullOrEmpty(fmt) &&
                   fmt.IndexOf("special", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 册号：正传用卷号补两位（V01），番外按顺序 SP01/SP02。
        /// 番外不拿卷号当编号 —— 番外常常和正传同卷号，两个 V01 分不清谁是谁。
        /// </summary>
        static void AssignLabels(ScrapeSeries s)
        {
            int sp = 0, seq = 0;
            foreach (var f in s.Files)
            {
                if (IsSpecialFormat(f.Format))
                {
                    sp++;
                    f.Label = "SP" + sp.ToString("00");
                }
                else
                {
                    int v;
                    if (int.TryParse(f.Volume, out v) && v > 0) f.Label = "V" + v.ToString("00");
                    else { seq++; f.Label = "V" + seq.ToString("00"); }
                }
            }
        }

        static int CompareName(string a, string b)        {
            var ka = Core.NaturalKey(a);
            var kb = Core.NaturalKey(b);
            for (int i = 0; i < Math.Min(ka.Count, kb.Count); i++)
                if (ka[i] != kb[i]) return ka[i].CompareTo(kb[i]);
            return ka.Count.CompareTo(kb.Count);
        }

        static int CountPages(ZipArchive zip)
        {
            int n = 0;
            foreach (var e in zip.Entries)
            {
                var ext = Path.GetExtension(e.FullName).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" ||
                    ext == ".gif" || ext == ".bmp") n++;
            }
            return n;
        }

        /// <summary>
        /// 读回「写入前留档」里的原始元数据：系列里任意一本带留档就能还原。
        /// 用于「回归原始」——刮错了想退回文件原本的样子。
        /// </summary>
        public static Metadata ReadOriginal(string seriesDir, out string fromFile,
                                            out List<Dictionary<string, object>> perFile)
        {
            fromFile = "";
            perFile = new List<Dictionary<string, object>>();
            if (!Directory.Exists(seriesDir)) return null;

            Metadata series = null;
            var files = Directory.GetFiles(seriesDir, "*.cbz");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (var f in files)
            {
                try
                {
                    using (var fs = File.OpenRead(f))
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                    {
                        var raw = ComicInfo.ReadRawFromArchive(zip);
                        var orig = ComicInfo.ExtractOriginal(raw);
                        if (orig.Length == 0) continue;

                        var m = ComicInfo.Parse(orig);
                        if (m == null) continue;

                        // 逐本回滚：每本自己留档里的号与标题
                        perFile.Add(new Dictionary<string, object>
                        {
                            {"path", f}, {"volume", m.Volume}, {"number", m.Number},
                            {"title", m.Title}, {"format", m.Format}, {"gtin", m.GTIN}
                        });

                        if (series == null)
                        {
                            series = m;
                            fromFile = Path.GetFileName(f);
                        }
                    }
                }
                catch { }
            }
            return series;
        }

        // ------------------------------------------------------------ 预览
        public static string PreviewXml(Metadata meta, MetaOptions opt, int pageCount)
        {
            var m = meta.Clone();
            if (string.IsNullOrEmpty(m.PageCount) && pageCount > 0) m.PageCount = pageCount.ToString();
            return ComicInfo.Render(m, opt);
        }

        public static string PreviewSeriesJson(Metadata meta, int totalIssues, string target)
        {
            return SeriesJson.Build(meta.Clone(), totalIssues, target);
        }

        // ------------------------------------------------------------ 写入
        public static void StartWrite(List<ScrapeJob> jobs, MetaWriter.Options opt, int threads)
        {
            lock (Sync)
            {
                _running = true; _phase = "写入中";
                _total = jobs.Count; _done = 0; _ok = 0; _err = 0; _skip = 0;
                _current = "";
                Logs.Clear();
                _stop = false;
            }

            var th = new Thread(delegate() { RunWrite(jobs, opt, threads); });
            th.IsBackground = true;
            th.Start();
        }

        public static void Stop()
        {
            _stop = true;
            Log("收到停止指令…", "w");
        }

        static void RunWrite(List<ScrapeJob> jobs, MetaWriter.Options opt, int threads)
        {
            try
            {
                if (threads < 1) threads = 1;
                Log("开始写入 " + jobs.Count + " 个系列，" + threads + " 并发", "i");

                int idx = -1;
                var list = new List<Thread>();
                for (int t = 0; t < threads; t++)
                {
                    var th = new Thread(delegate()
                    {
                        while (true)
                        {
                            int i = Interlocked.Increment(ref idx);
                            if (i >= jobs.Count || _stop) break;
                            WriteOne(jobs[i], opt);
                        }
                    });
                    th.IsBackground = true;
                    list.Add(th);
                    th.Start();
                }
                foreach (var th in list) th.Join();

                int ok, err, skip;
                lock (Sync) { ok = _ok; err = _err; skip = _skip; _running = false; _phase = _stop ? "已停止" : "完成"; }
                Log("完成：成功 " + ok + " 个系列，跳过 " + skip + "，失败 " + err, err == 0 ? "s" : "w");
            }
            catch (Exception ex)
            {
                lock (Sync) { _running = false; _phase = "出错"; }
                Log("致命错误：" + ex.Message, "e");
            }
        }

        static void WriteOne(ScrapeJob job, MetaWriter.Options opt)
        {
            var name = string.IsNullOrEmpty(job.Series) ? Path.GetFileName(job.Dir) : job.Series;
            lock (Sync) _current = name;

            try
            {
                if (!Directory.Exists(job.Dir)) throw new Exception("系列目录不存在");

                // 系列级的那几项（号 / 标题 / 特殊类型 / 书号）是「每本自己的」，
                // 这里必须按**系列**判断是不是多本，而不是按这一批次发了几本：
                // 只应用一本时，若当成单本系列，就会把系列级那份（来自第一本 = 番外）的
                // Format=Special 和它的标题一起写进这一册，看起来就是「清空号以后变 SP、还多了标题」。
                var multi = job.MultiSeries;
                int changed = 0, skipped = 0, failed = 0;

                foreach (var f in job.Files)
                {
                    var m = job.Meta.Clone();
                    // pair 是 "卷|话|单册标题|特殊类型|ISBN|单册链接|本册简介"，前端固定发满 7 段。
                    // 7 段都在，就说明「这一段发的就是最终值」：空串代表用户特意清空了它，
                    // 必须覆盖系列级那份。以前用 Length>0 做守卫，清空会被系列级顶回来 ——
                    // 单本系列最明显（系列级就是这一本，删了号等于没删）。
                    var parts = (f.Value ?? "").Split('|');
                    var full = parts.Length >= 7;
                    if (parts.Length >= 2)
                    {
                        if (full || parts[0].Length > 0) m.Volume = parts[0];
                        if (full || parts[1].Length > 0) m.Number = parts[1];
                    }
                    if (parts.Length >= 3 && (full || parts[2].Length > 0)) m.Title = parts[2];
                    else if (multi) m.Title = "";   // 多本系列不共用同一个书名，让服务器用「系列名 + 号」

                    // 番外标记是本级的：同一系列里正传和番外混放时，
                    // 不能把系列级的 Special 套到每一本上，也不能让番外的标记漏掉。
                    if (parts.Length >= 4 && (full || parts[3].Length > 0)) m.Format = parts[3];
                    else if (multi) m.Format = "";

                    // ISBN 是每本一条：多本系列里，系列级那条（来源一般只给首卷或根本没有）
                    // 不能套到每一本上 —— 否则 18 卷共用一个 ISBN，查书全串味。
                    if (parts.Length >= 5 && (full || parts[4].Length > 0)) m.GTIN = parts[4];
                    else if (multi) m.GTIN = "";

                    // 单册链接：这一本自己的商品页/条目页。空着就沿用系列级的 Web。
                    if (parts.Length >= 6 && parts[5].Trim().Length > 0) m.Web = parts[5].Trim();

                    // 本册简介：只有显式填了才覆盖系列简介（简介里可能有 '|'，后面整段拼回去）
                    if (parts.Length >= 7)
                    {
                        var own = string.Join("|", parts.Skip(6).ToArray()).Trim();
                        if (own.Length > 0) m.Summary = own;
                    }

                    int pages = 0;
                    try
                    {
                        using (var fs = File.OpenRead(f.Key))
                        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                        {
                            var old = ComicInfo.ReadFromArchive(zip);
                            if (old != null) { int pc; if (int.TryParse(old.PageCount, out pc)) pages = pc; }
                        }
                    }
                    catch { }

                    // 用户明确发满了 7 段 → 这一段发的就是最终值，缺号互补必须关掉。
                    // 否则「清空话、留着卷」会被互补抄回来，渲染和磁盘一模一样 → 判定未变化 → 不写盘。
                    var oneOpt = opt;
                    if (full)
                    {
                        oneOpt = opt.Clone();
                        // 只有用户亲手改过的册才完全按「发的值就是最终值」处理：
                        // 缺号互补、番外清卷号全关。纯刮削写入的册仍走档位规则
                        //（番外照样不挂卷号，不能因为改成 7 段协议就把这条丢了）。
                        // 例外：「不写 Number」是写入时的明确开关，对手动册同样生效
                        //（Normalize 5b 不受 ManualEdit 门控）—— 开关开着，
                        // 表单里填的 Number 也不落盘。
                        if (job.ManualFiles.Contains(f.Key))
                        {
                            oneOpt.Meta.FillMissingNumbers = false;
                            oneOpt.Meta.ManualEdit = true;
                        }
                    }

                    var r = MetaWriter.WriteCbz(f.Key, m, oneOpt, pages);
                    // 每本一行：把「实际写进去的卷 / 话」摊在日志里。
                    // 界面上看不出来到底发了什么，只报「成功 N 本」时，
                    // 清空号没清掉根本无从判断是没发出去还是没写进去。
                    var shown = Path.GetFileName(r.File);
                    var volTxt = string.IsNullOrEmpty(m.Volume) ? "(空)" : m.Volume;
                    var numTxt = string.IsNullOrEmpty(m.Number) ? "(空)" : m.Number;
                    var tail = "　卷 " + volTxt + "　话 " + numTxt;
                    if (r.Err != null)
                    {
                        failed++;
                        Log("失败 " + shown + "：" + r.Err, "e");
                    }
                    else if (r.Skipped) { skipped++; Log(shown + tail + "　跳过（文件里已经是这样）", "i"); }
                    else { changed++; Log(shown + tail, "i"); }
                }

                MetaWriter.WriteSeriesAssets(job.Dir, job.Meta, job.TotalIssues, opt);

                if (failed > 0)
                {
                    lock (Sync) { _err++; _done++; }
                    Log("部分失败 " + name + "：" + failed + " 本没写成功", "e");
                    return;
                }
                if (changed == 0)
                {
                    lock (Sync) { _skip++; _done++; }
                    Log("跳过 " + name + "：元数据没有变化", "i");
                    return;
                }

                lock (Sync) { _ok++; _done++; }
                Log("写入 " + name + "：" + changed + " 本", "i");
            }
            catch (Exception ex)
            {
                lock (Sync) { _err++; _done++; }
                Log("失败 " + name + "：" + ex.Message, "e");
            }
        }

        // ------------------------------------------------------------ 模型 ⇄ JSON
        static readonly string[] Fields =
        {
            "series", "seriesSort", "localizedSeries",
            "seriesChinese", "seriesOriginal", "seriesEnglish",
            "volume", "number", "count",
            "title", "summary", "year", "month", "day", "pageCount",
            "publisher", "imprint",
            "writer", "penciller", "inker", "colorist", "letterer", "coverArtist", "editor", "translator",
            "genre", "tags", "characters", "teams", "locations", "web",
            "language", "ageRating", "format", "manga", "gtin", "scanInformation", "notes",
            "aniListId", "malId", "mangaBakaId", "hardcoverId",
            "comicVineId", "metronId", "comicBookRoundupId",
            "seriesGroup", "storyArc", "storyArcNumber", "alternateSeries", "alternateNumber",
            "coverUrl", "coverUrlZh", "coverUrlOriginal", "sourceName", "sourceId", "sourceUrl"
        };

        public static Metadata MetaFromJson(Dictionary<string, object> d)
        {
            var m = new Metadata();
            if (d == null) return m;

            Func<string, string> g = delegate(string k)
            {
                object v;
                if (!d.TryGetValue(k, out v) || v == null) return "";
                return Convert.ToString(v).Trim();
            };

            m.Series = g("series"); m.SeriesSort = g("seriesSort"); m.LocalizedSeries = g("localizedSeries");
            m.SeriesChinese = g("seriesChinese"); m.SeriesOriginal = g("seriesOriginal");
            m.SeriesEnglish = g("seriesEnglish");
            m.Volume = g("volume"); m.Number = g("number"); m.Count = g("count");
            m.Title = g("title"); m.Summary = g("summary");
            m.Year = g("year"); m.Month = g("month"); m.Day = g("day"); m.PageCount = g("pageCount");
            m.Publisher = g("publisher"); m.Imprint = g("imprint");
            m.Writer = g("writer"); m.Penciller = g("penciller"); m.Inker = g("inker");
            m.Colorist = g("colorist"); m.Letterer = g("letterer"); m.CoverArtist = g("coverArtist");
            m.Editor = g("editor"); m.Translator = g("translator");
            m.Genre = g("genre"); m.Tags = g("tags");
            m.Characters = g("characters"); m.Teams = g("teams"); m.Locations = g("locations");
            m.Web = g("web");
            m.LanguageISO = g("language"); m.AgeRating = g("ageRating"); m.Format = g("format");
            m.Manga = g("manga"); m.GTIN = g("gtin");
            m.AniListId = g("aniListId"); m.MalId = g("malId"); m.MangaBakaId = g("mangaBakaId");
            m.HardcoverId = g("hardcoverId"); m.ComicVineId = g("comicVineId");
            m.MetronId = g("metronId"); m.ComicBookRoundupId = g("comicBookRoundupId");
            m.ScanInformation = g("scanInformation"); m.Notes = g("notes");
            m.SeriesGroup = g("seriesGroup"); m.StoryArc = g("storyArc"); m.StoryArcNumber = g("storyArcNumber");
            m.AlternateSeries = g("alternateSeries"); m.AlternateNumber = g("alternateNumber");
            m.CoverUrl = g("coverUrl"); m.CoverUrlZh = g("coverUrlZh");
            m.CoverUrlOriginal = g("coverUrlOriginal"); m.SourceName = g("sourceName");
            m.SourceId = g("sourceId"); m.SourceUrl = g("sourceUrl");
            return m;
        }

        public static Dictionary<string, object> MetaToJson(Metadata m)
        {
            var d = new Dictionary<string, object>();
            if (m == null)
            {
                foreach (var k in Fields) d[k] = "";
                return d;
            }
            d["series"] = m.Series; d["seriesSort"] = m.SeriesSort; d["localizedSeries"] = m.LocalizedSeries;
            d["seriesChinese"] = m.SeriesChinese; d["seriesOriginal"] = m.SeriesOriginal;
            d["seriesEnglish"] = m.SeriesEnglish;
            d["volume"] = m.Volume; d["number"] = m.Number; d["count"] = m.Count;
            d["title"] = m.Title; d["summary"] = m.Summary;
            d["year"] = m.Year; d["month"] = m.Month; d["day"] = m.Day; d["pageCount"] = m.PageCount;
            d["publisher"] = m.Publisher; d["imprint"] = m.Imprint;
            d["writer"] = m.Writer; d["penciller"] = m.Penciller; d["inker"] = m.Inker;
            d["colorist"] = m.Colorist; d["letterer"] = m.Letterer; d["coverArtist"] = m.CoverArtist;
            d["editor"] = m.Editor; d["translator"] = m.Translator;
            d["genre"] = m.Genre; d["tags"] = m.Tags;
            d["characters"] = m.Characters; d["teams"] = m.Teams; d["locations"] = m.Locations;
            d["web"] = m.Web;
            d["language"] = m.LanguageISO; d["ageRating"] = m.AgeRating; d["format"] = m.Format;
            d["manga"] = m.Manga; d["gtin"] = m.GTIN;
            d["aniListId"] = m.AniListId; d["malId"] = m.MalId; d["mangaBakaId"] = m.MangaBakaId;
            d["hardcoverId"] = m.HardcoverId; d["comicVineId"] = m.ComicVineId;
            d["metronId"] = m.MetronId; d["comicBookRoundupId"] = m.ComicBookRoundupId;
            d["scanInformation"] = m.ScanInformation; d["notes"] = m.Notes;
            d["seriesGroup"] = m.SeriesGroup; d["storyArc"] = m.StoryArc; d["storyArcNumber"] = m.StoryArcNumber;
            d["alternateSeries"] = m.AlternateSeries; d["alternateNumber"] = m.AlternateNumber;
            d["coverUrl"] = m.CoverUrl; d["coverUrlZh"] = m.CoverUrlZh;
            d["coverUrlOriginal"] = m.CoverUrlOriginal; d["sourceName"] = m.SourceName;
            d["sourceId"] = m.SourceId; d["sourceUrl"] = m.SourceUrl;
            return d;
        }

        public static Dictionary<string, object> CandidateToJson(Candidate c)
        {
            return new Dictionary<string, object>
            {
                {"source", c.Source}, {"sourceLabel", MetaSources.LabelOf(c.Source)},
                {"id", c.Id}, {"title", c.Title}, {"titleAlt", c.TitleAlt},
                {"year", c.Year}, {"type", c.Type}, {"cover", c.CoverUrl},
                {"summary", c.Summary}, {"score", c.Score}, {"sub", c.Sub}
            };
        }
    }
}
