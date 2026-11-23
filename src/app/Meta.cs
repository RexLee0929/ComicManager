using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ComicManager
{
    /// <summary>
    /// 元数据的统一模型：ComicInfo.xml 的超集。
    /// 所有网络源、本地源都先落到这里，再按「目标格式」渲染成对 Komga / Kavita 友好的 XML。
    ///
    /// 为什么不直接拼 XML：两个服务器的字段诉求不一样（Komga 按 Number 排、Kavita 按 Volume 排，
    /// 番外的表示法也不同），先有中间模型，才能做到「同一份数据、两种档位输出、互相转换」。
    /// </summary>
    public sealed class Metadata
    {
        // 系列
        public string Series = "";
        public string SeriesSort = "";
        public string LocalizedSeries = "";   // Kavita 专有：中日英混排时用来合并同一系列

        // 卷 / 话
        public string Volume = "";
        public string Number = "";
        public string Count = "";             // 总卷（话）数：Komga 用来显示「共 N 卷」，Kavita 用来推完结/缺卷

        // 单册
        public string Title = "";
        public string Summary = "";
        public string Year = "", Month = "", Day = "";
        public string PageCount = "";

        // 出版
        public string Publisher = "";
        public string Imprint = "";

        // 人物
        public string Writer = "", Penciller = "", Inker = "", Colorist = "",
                      Letterer = "", CoverArtist = "", Editor = "", Translator = "";

        // 分类
        public string Genre = "";             // 逗号分隔，两边都认
        public string Tags = "";              // 逗号分隔，Komga 进标签、Kavita 进 Tag
        public string Characters = "", Teams = "", Locations = "";
        public string Web = "";               // 官方/资料页

        // 其它
        /// <summary>
        /// 这一本的语言（不是原作语言）。默认留空 —— 来源站只会告诉你原作是什么语言，
        /// 你收藏的可能是中文版；写死 zh 会把日文原版扫成中文。空值由界面负责补。
        /// </summary>
        public string LanguageISO = "";
        public string AgeRating = "";         // ComicInfo 标准枚举
        public string Format = "";            // Special / TPB / Annual…：Kavita 用它判特刊
        public string Manga = "";             // Unknown / No / Yes / YesAndRightToLeft
        public string GTIN = "";              // ISBN
        public string Notes = "";
        public string ScanInformation = "";

        // 合集 / 书单（Komga 与 Kavita 都读，只是叫法不同：Collection / Reading List）
        public string SeriesGroup = "";
        public string StoryArc = "";
        public string StoryArcNumber = "";
        public string AlternateSeries = "";
        public string AlternateNumber = "";

        // 来源留痕：不进 ComicInfo 标准字段，写进 Notes 末尾，方便回溯是哪条刮削结果
        public string SourceName = "";
        public string SourceId = "";
        public string SourceUrl = "";
        public string CoverUrl = "";          // 默认封面（中文版优先，没有就是原版）
        public string CoverUrlZh = "";        // 中文版封面（MangaDex 按 locale 给）
        public string CoverUrlOriginal = "";  // 原始版（日文/原版）

        public Metadata Clone()
        {
            return (Metadata)MemberwiseClone();
        }

        /// <summary>把一份数据里「空着的」字段用另一份补上（已有值不覆盖）。</summary>
        public void FillFrom(Metadata other)
        {
            if (other == null) return;
            Fill(ref Series, other.Series); Fill(ref SeriesSort, other.SeriesSort);
            Fill(ref LocalizedSeries, other.LocalizedSeries);
            Fill(ref Volume, other.Volume); Fill(ref Number, other.Number); Fill(ref Count, other.Count);
            Fill(ref Title, other.Title); Fill(ref Summary, other.Summary);
            Fill(ref Year, other.Year); Fill(ref Month, other.Month); Fill(ref Day, other.Day);
            Fill(ref Publisher, other.Publisher); Fill(ref Imprint, other.Imprint);
            Fill(ref Writer, other.Writer); Fill(ref Penciller, other.Penciller); Fill(ref Inker, other.Inker);
            Fill(ref Colorist, other.Colorist); Fill(ref Letterer, other.Letterer);
            Fill(ref CoverArtist, other.CoverArtist); Fill(ref Editor, other.Editor); Fill(ref Translator, other.Translator);
            Fill(ref Genre, other.Genre); Fill(ref Tags, other.Tags);
            Fill(ref Characters, other.Characters); Fill(ref Teams, other.Teams); Fill(ref Locations, other.Locations);
            Fill(ref Web, other.Web);
            Fill(ref LanguageISO, other.LanguageISO); Fill(ref AgeRating, other.AgeRating);
            Fill(ref Format, other.Format); Fill(ref Manga, other.Manga); Fill(ref GTIN, other.GTIN);
            Fill(ref ScanInformation, other.ScanInformation); Fill(ref Notes, other.Notes);
            Fill(ref SeriesGroup, other.SeriesGroup);
            Fill(ref StoryArc, other.StoryArc); Fill(ref StoryArcNumber, other.StoryArcNumber);
            Fill(ref AlternateSeries, other.AlternateSeries); Fill(ref AlternateNumber, other.AlternateNumber);
            Fill(ref CoverUrl, other.CoverUrl);
            Fill(ref CoverUrlZh, other.CoverUrlZh);
            Fill(ref CoverUrlOriginal, other.CoverUrlOriginal);
            Fill(ref SourceName, other.SourceName); Fill(ref SourceId, other.SourceId); Fill(ref SourceUrl, other.SourceUrl);
        }

        static void Fill(ref string field, string value)
        {
            if (string.IsNullOrEmpty(field) && !string.IsNullOrEmpty(value)) field = value;
        }
    }

    /// <summary>渲染 XML 时的档位与开关。</summary>
    public sealed class MetaOptions
    {
        /// <summary>both / komga / kavita。</summary>
        public string Target = "both";

        /// <summary>系列名末尾的「(3)」拆成 Volume=3。Kavita 档默认开，Komga 档默认关。</summary>
        public bool SplitSeriesVolume;

        /// <summary>
        /// 番外（Format=Special）在 Komga 档里写成 x.5，插进正传中间。
        /// 默认关：真按库里的号算，3 → 2.5、1 → 0.5 会让人以为号错了；
        /// 想要「插在正传之间」的排序效果时再显式打开。
        /// </summary>
        public bool SpecialAsNumber;

        public static MetaOptions ForTarget(string target)
        {
            return new MetaOptions
            {
                Target = target,
                SplitSeriesVolume = target == "kavita"
            };
        }
    }

    /// <summary>ComicInfo.xml 的读写。字段清单按 Anansi v2.1 草案，只覆盖两个服务器真正会读的部分。</summary>
    public static class ComicInfo
    {
        public const string EntryName = "ComicInfo.xml";

        static readonly Regex TrailingVolume = new Regex(@"[\(（]\s*(\d{1,3})\s*[\)）]\s*$", RegexOptions.Compiled);
        static readonly Regex InvalidXmlChar = new Regex("[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F]", RegexOptions.Compiled);

        /// <summary>
        /// 按档位把统一模型收拾成「这个服务器能正确理解的一组值」。
        /// 只改会被服务器用来排序 / 归类的字段，不动标题、简介这类纯展示文本。
        /// </summary>
        public static void Normalize(Metadata m, MetaOptions opt)
        {
            if (m == null) return;

            // 1. 系列名里的卷号后缀。Komga 默认会把 Series + Volume 显示成「系列名 (卷号)」，
            //    用户可能把那个显示名直接抄进了 Series 字段，于是卷号在名字里出现两次。
            if (opt.SplitSeriesVolume)
            {
                var hit = TrailingVolume.Match(m.Series ?? "");
                if (hit.Success)
                {
                    var name = m.Series.Substring(0, hit.Index).Trim();
                    if (name.Length > 0)
                    {
                        m.Series = name;
                        if (string.IsNullOrEmpty(m.Volume)) m.Volume = hit.Groups[1].Value;
                    }
                }
            }

            // 2. 两边排序依据不同，缺哪个补哪个
            bool hasVol = !string.IsNullOrEmpty(m.Volume);
            bool hasNum = !string.IsNullOrEmpty(m.Number);

            if (opt.Target == "kavita")
            {
                if (!hasVol && hasNum)
                {
                    int v;
                    if (TryFirstInt(m.Number, out v)) m.Volume = v.ToString(CultureInfo.InvariantCulture);
                }
                // Kavita 认 Format=Special 判特刊，不靠小数号
                if (string.IsNullOrEmpty(m.Format) && IsSpecial(m)) m.Format = "Special";
            }
            else if (opt.Target == "komga")
            {
                if (!hasNum && hasVol) m.Number = m.Volume;
            }
            else
            {
                if (!hasNum && hasVol) m.Number = m.Volume;
                if (!hasVol && hasNum)
                {
                    int v;
                    if (TryFirstInt(m.Number, out v)) m.Volume = v.ToString(CultureInfo.InvariantCulture);
                }
            }

            // 3. 番外的小数号只在 Komga 档用：Kavita 会把 x.5 当成「第 x.5 话」照排，
            //    但它本来就有 Format=Special，不需要靠号位插队。
            if (opt.SpecialAsNumber && opt.Target != "kavita" && IsSpecial(m))
            {
                int n;
                if (TryFirstInt(m.Number, out n) && n > 0)
                    m.Number = (n - 0.5).ToString("0.0", CultureInfo.InvariantCulture);
                else if (string.IsNullOrEmpty(m.Number))
                    m.Number = "0.5";
            }
            // 4. 反向修复：上一版默认开过「番外写小数号」，把番外写成了 Number = 卷号-0.5。
            //    关掉开关以后这些号不会自己回来，重写一百遍还是 0.5。
            //    只在「恰好等于 卷号-0.5」时还原 —— 用户自己填成 0.5 的（比如真·序章）不动。
            else if (IsSpecial(m))
            {
                decimal n, v;
                if (decimal.TryParse(m.Number, NumberStyles.Float, CultureInfo.InvariantCulture, out n)
                    && decimal.TryParse(m.Volume, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                    && v > 0 && Math.Abs(n - (v - 0.5m)) < 0.0001m)
                    m.Number = m.Volume;
            }
        }

        /// <summary>特刊判定：Format 已标明，或标签里写了番外/特别篇。</summary>
        public static bool IsSpecial(Metadata m)
        {
            if (m == null) return false;
            if (!string.IsNullOrEmpty(m.Format) &&
                m.Format.IndexOf("special", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var t = (m.Tags ?? "") + "," + (m.Genre ?? "");
            return t.IndexOf("番外", StringComparison.Ordinal) >= 0 ||
                   t.IndexOf("特別篇", StringComparison.Ordinal) >= 0 ||
                   t.IndexOf("特别篇", StringComparison.Ordinal) >= 0;
        }

        public static bool TryFirstInt(string s, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s)) return false;
            var m = Regex.Match(s, "-?\\d+");
            if (!m.Success) return false;
            return int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        // ------------------------------------------------------------ 写
        /// <summary>渲染 ComicInfo.xml。字段顺序照 Anansi 的表排，人读也顺。</summary>
        public static string Render(Metadata m, MetaOptions opt)
        {
            var data = m.Clone();
            Normalize(data, opt);

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append("<ComicInfo xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" ");
            sb.Append("xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n");

            Tag(sb, "Series", data.Series);
            Tag(sb, "SeriesSort", data.SeriesSort);
            Tag(sb, "LocalizedSeries", data.LocalizedSeries);
            Tag(sb, "Title", data.Title);
            Tag(sb, "Number", data.Number);
            Tag(sb, "Count", data.Count);
            Tag(sb, "Volume", data.Volume);
            Tag(sb, "AlternateSeries", data.AlternateSeries);
            Tag(sb, "AlternateNumber", data.AlternateNumber);
            Tag(sb, "Summary", data.Summary);
            Tag(sb, "Year", data.Year);
            Tag(sb, "Month", data.Month);
            Tag(sb, "Day", data.Day);
            Tag(sb, "Writer", data.Writer);
            Tag(sb, "Penciller", data.Penciller);
            Tag(sb, "Inker", data.Inker);
            Tag(sb, "Colorist", data.Colorist);
            Tag(sb, "Letterer", data.Letterer);
            Tag(sb, "CoverArtist", data.CoverArtist);
            Tag(sb, "Editor", data.Editor);
            Tag(sb, "Translator", data.Translator);
            Tag(sb, "Publisher", data.Publisher);
            Tag(sb, "Imprint", data.Imprint);
            Tag(sb, "Genre", data.Genre);
            Tag(sb, "Tags", data.Tags);
            Tag(sb, "Web", data.Web);
            Tag(sb, "PageCount", data.PageCount);
            Tag(sb, "LanguageISO", data.LanguageISO);
            Tag(sb, "Characters", data.Characters);
            Tag(sb, "Teams", data.Teams);
            Tag(sb, "Locations", data.Locations);
            Tag(sb, "ScanInformation", data.ScanInformation);
            Tag(sb, "StoryArc", data.StoryArc);
            Tag(sb, "StoryArcNumber", data.StoryArcNumber);
            Tag(sb, "SeriesGroup", data.SeriesGroup);
            // Manga 只认三个字面值：Yes / No / YesAndRightToLeft（Unknown 时不写，免得覆盖服务器判断）
            if (!string.IsNullOrEmpty(data.Manga) && data.Manga != "Unknown") Tag(sb, "Manga", data.Manga);
            Tag(sb, "AgeRating", data.AgeRating);
            Tag(sb, "Format", data.Format);
            Tag(sb, "GTIN", data.GTIN);
            Tag(sb, "Notes", BuildNotes(data));

            sb.Append("</ComicInfo>");
            return sb.ToString();
        }

        /// <summary>Notes 里带上刮削来源，日后对不上时能一眼看出数据是哪来的。</summary>
        static string BuildNotes(Metadata m)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(m.Notes)) parts.Add(m.Notes.Trim());
            if (!string.IsNullOrEmpty(m.SourceName) && !string.IsNullOrEmpty(m.SourceId))
                parts.Add("Source: " + m.SourceName + " #" + m.SourceId);
            else if (!string.IsNullOrEmpty(m.SourceName))
                parts.Add("Source: " + m.SourceName);
            if (!string.IsNullOrEmpty(m.SourceUrl)) parts.Add(m.SourceUrl.Trim());
            return string.Join(" | ", parts.ToArray());
        }

        static void Tag(StringBuilder sb, string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var v = Clean(value);
            if (v.Length == 0) return;
            sb.Append("  <").Append(name).Append(">").Append(v)
              .Append("</").Append(name).Append(">\n");
        }

        static string Clean(string s)
        {
            s = InvalidXmlChar.Replace(s, "");
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Trim();
        }

        // ------------------------------------------------------------ 读
        /// <summary>读压缩包里的 ComicInfo.xml；没有就返回 null。任何解析错误都当「没有」，不阻断转换。</summary>
        public static Metadata ReadFromArchive(ZipArchive zip)
        {
            if (zip == null) return null;
            ZipArchiveEntry entry = null;
            foreach (var e in zip.Entries)
            {
                // 必须在根目录：两个服务器都只认根目录下的 ComicInfo.xml
                if (e.FullName.IndexOf('/') >= 0) continue;
                if (string.Equals(e.FullName, EntryName, StringComparison.OrdinalIgnoreCase)) { entry = e; break; }
            }
            if (entry == null) return null;

            try
            {
                string text;
                using (var s = entry.Open())
                using (var r = new StreamReader(s, Encoding.UTF8, true))
                    text = r.ReadToEnd();
                return Parse(text);
            }
            catch { return null; }
        }

        /// <summary>原样读出包内 XML 的文本（不解析），用于写入前的留档。</summary>
        public static string ReadRawFromArchive(ZipArchive zip)
        {
            if (zip == null) return "";
            foreach (var e in zip.Entries)
            {
                if (e.FullName.IndexOf('/') >= 0) continue;
                if (!string.Equals(e.FullName, EntryName, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using (var s = e.Open())
                    using (var r = new StreamReader(s, Encoding.UTF8, true))
                        return r.ReadToEnd();
                }
                catch { return ""; }
            }
            return "";
        }

        /// <summary>
        /// 把旧 XML 全文以注释附在新 XML 后面。
        /// 重复写入不会累积：先剥掉上一次的留档块（标记固定），再追加本次的。
        /// XML 注释里不允许出现「--」，逐行把双横线拆开，避免整份 XML 变成非法。
        /// </summary>
        public static string KeepOriginal(string xml, string oldRaw)
        {
            if (string.IsNullOrEmpty(xml)) return xml;
            if (string.IsNullOrEmpty(oldRaw) || oldRaw.Trim().Length == 0) return xml;

            // 旧文件里已经有留档块（上一次写入留下的）——原样带过来。
            // 这样反复写入始终保留「最初那一版」，而不是滚雪球一样一层层叠注释；
            // 「上一次」的完整文件另有 .bak 兜着。
            var old = ExistingBlock(oldRaw);
            if (old != null) return xml.TrimEnd() + "\r\n" + old + "\r\n";

            var body = oldRaw.Trim().Replace("--", "- -");
            if (body.Length > 8000) body = body.Substring(0, 8000) + "\r\n…（原文过长，后面省略）";

            var sb = new StringBuilder();
            sb.Append(xml.TrimEnd());
            sb.Append("\r\n<!-- ===== 写入前留档（ComicManager ").Append(Brand.Version)
              .Append(" / ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("）=====\r\n");
            sb.Append("     以下为本次覆盖前的完整 ComicInfo，仅作留档用，解析时会忽略：\r\n");
            foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
                sb.Append("     ").Append(line).Append("\r\n");
            sb.Append("===== 留档结束 ===== -->\r\n");
            return sb.ToString();
        }

        static readonly Regex BlockRe =
            new Regex(@"<!--\s*=+\s*写入前留档[\s\S]*?留档结束\s*=+\s*-->", RegexOptions.Compiled);

        /// <summary>
        /// 从留档注释里把原始 XML 抠出来（「回归原始」用）。
        /// 留档时把 "--" 写成了 "- -"，这里换回去，否则还原出来的注释会缺字符。
        /// </summary>
        public static string ExtractOriginal(string archiveXml)
        {
            var blk = ExistingBlock(archiveXml);
            if (blk == null) return "";

            int a = blk.IndexOf("<?xml", StringComparison.Ordinal);
            int b = blk.LastIndexOf("</ComicInfo>", StringComparison.Ordinal);
            if (a < 0 || b < a) return "";

            var body = blk.Substring(a, b - a + "</ComicInfo>".Length);
            var lines = body.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                // 去掉留档时加的 5 个空格缩进
                int k = 0;
                while (k < lines[i].Length && k < 5 && lines[i][k] == ' ') k++;
                lines[i] = lines[i].Substring(k);
            }
            return string.Join("\n", lines).Replace("- -", "--").Trim();
        }

        static string ExistingBlock(string raw)
        {
            var m = BlockRe.Match(raw ?? "");
            return m.Success ? m.Value : null;
        }

        public static Metadata ReadFromCbz(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                    return ReadFromArchive(zip);
            }
            catch { return null; }
        }

        public static Metadata Parse(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch { return null; }

            var root = doc.Root;
            if (root == null) return null;

            var m = new Metadata();
            foreach (var e in root.Elements())
            {
                var v = (e.Value ?? "").Trim();
                if (v.Length == 0) continue;
                switch (e.Name.LocalName)
                {
                    case "Series": m.Series = v; break;
                    case "SeriesSort": m.SeriesSort = v; break;
                    case "LocalizedSeries": m.LocalizedSeries = v; break;
                    case "Volume": m.Volume = v; break;
                    case "Number": m.Number = v; break;
                    case "Count": m.Count = v; break;
                    case "Title": m.Title = v; break;
                    case "Summary": m.Summary = v; break;
                    case "Year": m.Year = v; break;
                    case "Month": m.Month = v; break;
                    case "Day": m.Day = v; break;
                    case "PageCount": m.PageCount = v; break;
                    case "Publisher": m.Publisher = v; break;
                    case "Imprint": m.Imprint = v; break;
                    case "Writer": m.Writer = v; break;
                    case "Penciller": m.Penciller = v; break;
                    case "Inker": m.Inker = v; break;
                    case "Colorist": m.Colorist = v; break;
                    case "Letterer": m.Letterer = v; break;
                    case "CoverArtist": m.CoverArtist = v; break;
                    case "Editor": m.Editor = v; break;
                    case "Translator": m.Translator = v; break;
                    case "Genre": m.Genre = v; break;
                    case "Tags": m.Tags = v; break;
                    case "Web": m.Web = v; break;
                    case "LanguageISO": m.LanguageISO = v; break;
                    case "Characters": m.Characters = v; break;
                    case "Teams": m.Teams = v; break;
                    case "Locations": m.Locations = v; break;
                    case "ScanInformation": m.ScanInformation = v; break;
                    case "StoryArc": m.StoryArc = v; break;
                    case "StoryArcNumber": m.StoryArcNumber = v; break;
                    case "SeriesGroup": m.SeriesGroup = v; break;
                    case "Manga": m.Manga = v; break;
                    case "AgeRating": m.AgeRating = v; break;
                    case "Format": m.Format = v; break;
                    case "GTIN": m.GTIN = v; break;
                    case "Notes": m.Notes = v; break;
                }
            }
            return m;
        }
    }

    /// <summary>Komga 的系列级元数据文件：Mylar 的 series.json 格式，放系列目录根部。</summary>
    public static class SeriesJson
    {
        public const string FileName = "series.json";

        public static string Build(Metadata m, int totalIssues)
        {
            var o = new Dictionary<string, object>
            {
                // volume 固定写 1：Komga 在 volume != 1 时会把年份拼进系列名（「航海王 (1997)」）
                {"volume", 1},
                {"name", m.Series ?? ""},
                {"publisher", m.Publisher ?? ""},
                {"status", Status(m)},
                {"description_text", StripHtml(m.Summary)},
                {"description_formatted", ""},
                {"age_rating", m.AgeRating ?? ""},
                {"total_issues", totalIssues},
                {"year", ParseYear(m.Year)}
            };
            if (!string.IsNullOrEmpty(m.SeriesSort)) o["name_sort"] = m.SeriesSort;
            return MiniJson.Serialize(o);
        }

        static string Status(Metadata m)
        {
            if (!string.IsNullOrEmpty(m.Format) &&
                m.Format.IndexOf("special", StringComparison.OrdinalIgnoreCase) >= 0) return "Ended";
            return "Continuing";
        }

        static int ParseYear(string y)
        {
            int v;
            return int.TryParse(y, out v) ? v : 0;
        }

        static string StripHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = Regex.Replace(s, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "<[^>]+>", "");
            return t.Replace("&nbsp;", " ").Trim();
        }
    }

    /// <summary>写入结果：写没写、跳到哪一步、为什么失败。</summary>
    public sealed class WriteResult
    {
        public string File;
        public bool Skipped;
        public bool Changed;
        public string Err;
        public string Note;
    }

    /// <summary>
    /// 把元数据写回文件：只替换包内的 ComicInfo.xml，图片字节原样搬运。
    /// 先在同一个目录写 .tmp，写完再替换原文件；开启备份时原文件先留一份 .bak。
    /// </summary>
    public static class MetaWriter
    {
        public sealed class Options
        {
            public bool Backup = true;
            public bool WriteSeriesJson = true;
            public bool WriteCover = true;
            public bool OverwriteCover;

            /// <summary>
            /// 把覆盖前的完整 ComicInfo 以注释形式附在新 XML 后面。
            /// 被改掉、被清空的值都还能查回来；Komga / Kavita 解析时忽略注释，不影响识别。
            /// </summary>
            public bool KeepOriginal = true;

        /// <summary>封面挑哪一版：auto（中文优先，没有就原版）/ zh / original。</summary>
        public string CoverPick = "auto";

        /// <summary>封面宽度上限，0 = 原图尺寸（只重编码成 JPEG）。</summary>
        public int CoverMaxWidth = 800;
            public MetaOptions Meta = new MetaOptions();
        }

        /// <summary>改一个 CBZ：替换/新增 ComicInfo.xml。</summary>
        public static WriteResult WriteCbz(string path, Metadata meta, Options opt, int pageCount)
        {
            var res = new WriteResult { File = path };
            if (opt == null) opt = new Options();
            if (opt.Meta == null) opt.Meta = new MetaOptions();

            var data = meta.Clone();
            if (string.IsNullOrEmpty(data.PageCount) && pageCount > 0)
                data.PageCount = pageCount.ToString(CultureInfo.InvariantCulture);

            var xml = ComicInfo.Render(data, opt.Meta);

            string tmp = path + ".tmp";
            try
            {
                using (var src = ZipFile.OpenRead(path))
                {
                    // 内容没变就不重打包：大文件重打包一次几十秒，白跑
                    var old = ComicInfo.ReadFromArchive(src);
                    if (old != null)
                    {
                        var oldXml = ComicInfo.Render(old, opt.Meta);
                        if (oldXml == xml) { res.Skipped = true; res.Note = "元数据未变化"; return res; }
                    }

                    // 旧 XML 原文先留一份：等会儿附在文末当留档
                    var oldRaw = opt.KeepOriginal ? ComicInfo.ReadRawFromArchive(src) : "";
                    var finalXml = opt.KeepOriginal ? ComicInfo.KeepOriginal(xml, oldRaw) : xml;
                    var xmlBytes = new UTF8Encoding(false).GetBytes(finalXml);

                    if (File.Exists(tmp)) File.Delete(tmp);
                    using (var fs = File.Create(tmp))
                    using (var dst = new ZipArchive(fs, ZipArchiveMode.Create))
                    {
                        foreach (var e in src.Entries)
                        {
                            if (e.FullName.IndexOf('/') < 0 &&
                                string.Equals(e.FullName, ComicInfo.EntryName, StringComparison.OrdinalIgnoreCase))
                                continue;   // 旧的 XML 丢掉，最后统一写新的

                            // 压缩方式跟着原条目走：图本来就压过（CompressedLength < Length）才重压，
                            // 否则不压缩搬运 —— 既不让包无谓变大，也不白烧 CPU。
                            var level = e.CompressedLength < e.Length
                                ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
                            var ne = dst.CreateEntry(e.FullName, level);
                            try { ne.LastWriteTime = e.LastWriteTime; } catch { }
                            using (var os = ne.Open())
                            using (var ins = e.Open())
                                ins.CopyTo(os);
                        }

                        var ci = dst.CreateEntry(ComicInfo.EntryName, CompressionLevel.Optimal);
                        using (var os = ci.Open())
                            os.Write(xmlBytes, 0, xmlBytes.Length);
                    }
                }

                if (opt.Backup)
                {
                    var bak = path + ".bak";
                    try
                    {
                        // 只留最近一份：首轮备份是「刮削前」的原件，正是要保的东西
                        if (File.Exists(bak)) File.Delete(bak);
                        File.Copy(path, bak, true);
                    }
                    catch (Exception ex) { App.Dump("备份失败 " + path + "：" + ex.Message); }
                }

                // 同目录内替换：先 File.Replace（会带上时间戳），失败再退到删除+改名
                try
                {
                    File.Replace(tmp, path, null, true);
                }
                catch
                {
                    File.Delete(path);
                    File.Move(tmp, path);
                }
                res.Changed = true;
            }
            catch (Exception ex)
            {
                res.Err = ex.Message;
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            return res;
        }

        /// <summary>写系列级文件（series.json、cover.jpg）。失败只记日志，不算整次刮削失败。</summary>
        public static void WriteSeriesAssets(string seriesDir, Metadata meta, int totalIssues, Options opt)
        {
            if (opt.WriteSeriesJson)
            {
                try
                {
                    var path = Path.Combine(seriesDir, SeriesJson.FileName);
                    var json = SeriesJson.Build(meta, totalIssues);
                    // 内容相同就不落盘，避免每次都刷新 mtime 触发 Komga 重扫
                    if (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != json)
                        File.WriteAllText(path, json, new UTF8Encoding(false));
                }
                catch (Exception ex) { App.Dump("series.json 写入失败 " + seriesDir + "：" + ex.Message); }
            }

            if (opt.WriteCover)
            {
                try
                {
                    var url = PickCover(meta, opt.CoverPick);
                    if (string.IsNullOrEmpty(url)) return;

                    var path = Path.Combine(seriesDir, "cover.jpg");
                    if (File.Exists(path) && !opt.OverwriteCover) return;
                    var bytes = Cover.Fetch(url, opt.CoverMaxWidth);
                    if (bytes != null && bytes.Length > 0) File.WriteAllBytes(path, bytes);
                }
                catch (Exception ex) { App.Dump("封面写入失败 " + seriesDir + "：" + ex.Message); }
            }
        }

        /// <summary>按偏好挑封面 URL；要的那版没有就退回默认，再没有就原版。</summary>
        public static string PickCover(Metadata m, string pick)
        {
            if (m == null) return "";
            if (pick == "zh" && !string.IsNullOrEmpty(m.CoverUrlZh)) return m.CoverUrlZh;
            if (pick == "original" && !string.IsNullOrEmpty(m.CoverUrlOriginal)) return m.CoverUrlOriginal;
            if (!string.IsNullOrEmpty(m.CoverUrl)) return m.CoverUrl;
            if (!string.IsNullOrEmpty(m.CoverUrlZh)) return m.CoverUrlZh;
            return m.CoverUrlOriginal;
        }
    }

    /// <summary>
    /// 封面：下载 → 统一重编码成 JPEG。
    /// 宽度上限由界面上选（原图 = 0，只重编码不缩放；默认 800）。原图动辄几 MB，当封面没必要。
    /// </summary>
    public static class Cover
    {
        public static byte[] Fetch(string url, int maxWidth)
        {
            if (string.IsNullOrEmpty(url)) return null;
            byte[] raw = Net.GetBytes(url, 20000);
            if (raw == null || raw.Length == 0) return null;
            return Shrink(raw, maxWidth);
        }

        static byte[] Shrink(byte[] raw, int maxWidth)
        {
            try
            {
                using (var ms = new MemoryStream(raw))
                using (var img = Image.FromStream(ms))
                {
                    int w = img.Width, h = img.Height;
                    if (w <= 0 || h <= 0) return raw;
                    if (maxWidth <= 0 || w <= maxWidth) return Encode(img, w, h, maxWidth <= 0 ? 92L : 88L);

                    int nw = maxWidth, nh = (int)Math.Round(h * (double)maxWidth / w);
                    using (var bmp = new Bitmap(nw, nh))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(img, 0, 0, nw, nh);
                        }
                        return Encode(bmp, nw, nh, 88L);
                    }
                }
            }
            catch
            {
                return raw;   // 解不开就原样写，至少比没有强
            }
        }

        static byte[] Encode(Image img, int w, int h, long quality)
        {
            using (var bmp = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);   // 带 alpha 的 PNG 转 JPEG 会黑底，先铺白
                    g.DrawImage(img, 0, 0, w, h);
                }
                using (var ms = new MemoryStream())
                {
                    var codec = ImageCodecInfo.GetImageEncoders();
                    ImageCodecInfo jpg = null;
                    foreach (var c in codec)
                        if (c.FormatID == ImageFormat.Jpeg.Guid) { jpg = c; break; }

                    if (jpg == null) { bmp.Save(ms, ImageFormat.Jpeg); return ms.ToArray(); }

                    using (var ps = new EncoderParameters(1))
                    {
                        // 这里的 Encoder 指 System.Drawing.Imaging.Encoder（不是 System.Text.Encoder）
                        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                        bmp.Save(ms, jpg, ps);
                    }
                    return ms.ToArray();
                }
            }
        }
    }

    /// <summary>HTTP 收口：统一 UA、超时、错误处理。所有网络源都走这里，别各写一套。</summary>
    public static class Net
    {
        public static string UserAgent
        {
            get { return Brand.Name + "/" + Brand.Version + " (+https://github.com/rexlee/" + Brand.Name + ")"; }
        }

        public static string PostJson(string url, string body, int timeoutMs)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.UserAgent = UserAgent;
                req.Accept = "application/json";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                var bytes = new UTF8Encoding(false).GetBytes(body ?? "{}");
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch (WebException ex)
            {
                App.Dump("HTTP POST " + url + "：" + ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                App.Dump("HTTP POST " + url + "：" + ex.Message);
                return null;
            }
        }

        public static string GetText(string url, int timeoutMs)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = UserAgent;
                req.Accept = "application/json";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch (Exception ex)
            {
                App.Dump("HTTP GET " + url + "：" + ex.Message);
                return null;
            }
        }

        public static byte[] GetBytes(string url, int timeoutMs)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = UserAgent;
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                App.Dump("HTTP 下载 " + url + "：" + ex.Message);
                return null;
            }
        }
    }
}
