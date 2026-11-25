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
using System.Xml;
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

        // 同一个作品的三种写法各留一个：有的库只收拉丁字母、有的只收日文，
        // 丢哪一种都没法再还原（拿中文名去 AniList 搜，命中率明显不如英文名）。
        public string SeriesChinese = "";     // 中文译名
        public string SeriesOriginal = "";    // 原名（日文 / 韩文）
        public string SeriesEnglish = "";     // 英文名

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

        // 外部数据库 id：Kavita 的「外部元数据」面板就是这几项。
        // 它们不是 ComicInfo 标准字段，写成独立标签（服务器会忽略未知标签）；
        // 同时 Notes 里再写一份 URL —— Kavita 是从备注里的链接解析这些 id 的。
        public string AniListId = "";
        public string MalId = "";
        public string MangaBakaId = "";
        public string HardcoverId = "";
        public string ComicVineId = "";
        public string MetronId = "";
        public string ComicBookRoundupId = "";

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
            Fill(ref SeriesChinese, other.SeriesChinese);
            Fill(ref SeriesOriginal, other.SeriesOriginal);
            Fill(ref SeriesEnglish, other.SeriesEnglish);
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
            Fill(ref AniListId, other.AniListId); Fill(ref MalId, other.MalId);
            Fill(ref MangaBakaId, other.MangaBakaId); Fill(ref HardcoverId, other.HardcoverId);
            Fill(ref ComicVineId, other.ComicVineId); Fill(ref MetronId, other.MetronId);
            Fill(ref ComicBookRoundupId, other.ComicBookRoundupId);
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
        /// <summary>
        /// 刮削页：kavitacustom = Kavita Custom / kavita = Kavita 原版 / komga；
        /// 转换页仍保留 both。Custom 与原版的 ComicInfo 写法一致，差别在 series.json 格式。
        /// </summary>
        public string Target = "both";

        /// <summary>
        /// Kavita 系档位的「不写 Number」规则（界面开关，切到 Kavita 档默认开）：
        /// 正传有卷 → 只写 Volume，Number 清空（Kavita 排卷只看 Volume，双写没有意义）；
        /// 话（没有卷）→ 只写 Number，不再把话抄成卷号。
        /// 番外的号位归 <see cref="SpecialVolumeZero"/> 管。
        /// </summary>
        public bool SkipNumber;

        /// <summary>
        /// 「番外卷号占 0」（界面开关，Kavita Custom 档默认开）：番外的 Volume 写 0 而不是清空，
        /// Number 按原有顺序照写 —— Specials 区里靠它排序，也不会跟正传挤进同一张卷卡片。
        /// 关掉则回到第 5 步的行为：番外卷号清空。
        /// </summary>
        public bool SpecialVolumeZero;

        /// <summary>Kavita 系（原版 / Custom）档位判定。</summary>
        public static bool IsKavitaFamily(string target)
        {
            return target == "kavita" || target == "kavitacustom";
        }

        /// <summary>系列名末尾的「(3)」拆成 Volume=3。Kavita 按 Volume 归类，必须拆。</summary>
        public bool SplitSeriesVolume;

        /// <summary>
        /// 番外（Format=Special）在 Komga 档里写成 x.5，插进正传中间。
        /// 只在 Komga 档用：Kavita 会把 x.5 当成「第 x.5 话」照排，它本来就靠 Format=Special 判特刊。
        /// </summary>
        public bool SpecialAsNumber;

        /// <summary>
        /// 写 SeriesGroup。Kavita 只认这一个字段来建「收藏」（Collections）：
        /// 关掉就等于告诉 Kavita 别给这个系列自动归组，包内也不留这个标签。
        /// </summary>
        public bool WriteSeriesGroup = true;

        /// <summary>
        /// 缺号互补：只有 Volume 没 Number（或反过来）时，把有的那个抄给空的。
        ///
        /// 刮削填表时这么做是对的（两个服务器各认一个字段，缺一个就排不对）。
        /// 但**写入用户手动改过的文件时必须关掉** —— 用户在界面上把「话」清空、
        /// 留着「卷」，互补又会把话抄回来，渲染结果和磁盘上的一模一样，
        /// 于是判定「元数据未变化」直接跳过：界面看着改了，文件一个字节没动。
        /// </summary>
        public bool FillMissingNumbers = true;

        /// <summary>
        /// 用户在界面上手动编辑过的写入（前端发满 7 段协议）。
        /// 打开后 Normalize 只做格式收拾，不再替用户改任何值：
        /// 缺号互补、番外小数号、番外清卷号全都跳过 —— 用户留着的卷号不能被规则吃掉，
        /// 否则改完永远和盘上一致，第二次写入就是「跳过：元数据没有变化」。
        /// </summary>
        public bool ManualEdit;

        public MetaOptions Clone()
        {
            return (MetaOptions)MemberwiseClone();
        }

        /// <summary>
        /// &lt;Web&gt; 里多个链接用什么拼 —— 两家服务器的切法**相反**，只能按档位分：
        ///
        /// Kavita 的 ExternalIdParser.ExtractId 只用 `Split(',')` 切，再拿
        /// `StartsWith("https://anilist.co/manga/")` 逐个比对。空格分隔时整串是一个 token，
        /// 一个 id 都认不出来（章节页那一排「外部元数据」就全是 Not set）。
        /// 它自己的 ComicInfo 模型注释也写着 “This can be comma-separated”，
        /// wiki 的转换表里 `Web → Web Links`。
        ///
        /// Komga 反过来只按空格切（ComicInfoProvider：`comicInfo.web?.split(" ")`），
        /// 逗号会让它把整串当成一条链接。所以：Kavita 写逗号，Komga 写空格。
        /// （Kavita Custom 补丁版逗号、空格都认 —— 读侧兼容 —— 逗号对官方版也有效，仍写逗号最稳。）
        /// </summary>
        public string WebSep = ",";

        public static MetaOptions ForTarget(string target)
        {
            return Preset(target);
        }

        /// <summary>
        /// 预设值：这几个开关不再让用户选，按目标服务器定 ——
        /// Kavita 认 Volume，系列名里的「(3)」要拆出来；Komga 认 Number，番外写 x.5 才能插队。
        /// 链接分隔符同理：Kavita 只认逗号，Komga 只认空格。
        /// </summary>
        public static MetaOptions Preset(string target)
        {
            if (string.IsNullOrEmpty(target)) target = "both";
            return new MetaOptions
            {
                Target = target,
                SplitSeriesVolume = IsKavitaFamily(target),
                SpecialAsNumber = target == "komga",
                WebSep = target == "komga" ? " " : ","
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

            // 0. LocalizedSeries 是 Kavita 的「本地化名」：显示名不是中文时，把中文名放这儿。
            //    上一版把「原名」塞在这个字段里，Kavita 的别名就会指成日文原名；
            //    现在原名有独立的 SeriesOriginal，这个字段只放中文名。
            if (!string.IsNullOrEmpty(m.SeriesChinese))
                m.LocalizedSeries = (m.SeriesChinese != m.Series) ? m.SeriesChinese : "";

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

            // 2. 两边排序依据不同，缺哪个补哪个。
            //    仅限「程序自己凑数据」的场合（刮削填表、预览）；用户手动逐册写入时
            //    由 WriteOne 把 FillMissingNumbers 关掉，否则清空一个号会被另一个抄回来。
            bool hasVol = !string.IsNullOrEmpty(m.Volume);
            bool hasNum = !string.IsNullOrEmpty(m.Number);

            if (MetaOptions.IsKavitaFamily(opt.Target))
            {
                // 「不写 Number」开着时不抄：话就该留在 Number（进 Kavita 的章节区按号排），
                // 抄成卷号反而把话伪装成了卷。
                if (!opt.SkipNumber && !hasVol && hasNum && opt.FillMissingNumbers)
                {
                    int v;
                    if (TryFirstInt(m.Number, out v)) m.Volume = v.ToString(CultureInfo.InvariantCulture);
                }
                // Kavita 认 Format=Special 判特刊，不靠小数号
                if (string.IsNullOrEmpty(m.Format) && IsSpecial(m)) m.Format = "Special";
            }
            else if (opt.Target == "komga")
            {
                if (!hasNum && hasVol && opt.FillMissingNumbers) m.Number = m.Volume;
            }
            else
            {
                if (!hasNum && hasVol && opt.FillMissingNumbers) m.Number = m.Volume;
                if (!hasVol && hasNum && opt.FillMissingNumbers)
                {
                    int v;
                    if (TryFirstInt(m.Number, out v)) m.Volume = v.ToString(CultureInfo.InvariantCulture);
                }
            }

            // 3. 番外的小数号只在 Komga 档用：Kavita 会把 x.5 当成「第 x.5 话」照排，
            //    但它本来就有 Format=Special，不需要靠号位插队。
            //    （手动编辑时跳过：用户填的号就是最终值，不能被程序改成 x.5）
            if (!opt.ManualEdit && opt.SpecialAsNumber && IsSpecial(m))
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
            else if (!opt.ManualEdit && IsSpecial(m))
            {
                decimal n, v;
                if (decimal.TryParse(m.Number, NumberStyles.Float, CultureInfo.InvariantCulture, out n)
                    && decimal.TryParse(m.Volume, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                    && v > 0 && Math.Abs(n - (v - 0.5m)) < 0.0001m)
                    m.Number = m.Volume;
            }

            // 5. 番外不挂卷号。番外本来就不属于任何一卷，写着 Volume=1 会跟正传第 1 卷
            //    挤进同一张卷卡片（Kavita 里点进卷 1 会看到「正传第 1 话 + 番外」），
            //    连带把系列级的 ISBN / Web 继承成番外那一本（番外没有书号 → 显示 Not set）。
            //    Kavita 认 Format=Special 就把它放进 Specials 区；Komga 档保留卷号（它只按 Number 排）。
            //    手动编辑时跳过：用户特意给番外留的卷号（比如只清了话）不能被这条吃掉，
            //    否则改完永远和盘上一致 → 第二次写入「跳过」，看着就像怎么改都没用。
            if (!opt.ManualEdit && opt.Target != "komga" && IsSpecial(m))
            {
                m.Volume = "";
                if (string.IsNullOrEmpty(m.Format)) m.Format = "Special";
            }

            // 5b. Kavita 系 + 「不写 Number」：卷 / 话各写各的，不双写（只管正传）。
            //     正传有卷 → Number 清空（排卷只看 Volume）；正传无卷有话 → Number 原样写。
            //     番外的号位归 5c 的「番外卷号占 0」开关管。
            //     同样按 !ManualEdit 走，两个原因：
            //     ① 手动编辑的册以用户填的值为准（和第 3/4/5 步同一原则）；
            //     ② WriteCbz 的「跳过判定」会用 ManualEdit=true 严格渲染磁盘旧值 ——
            //        若这里对旧值也清 Number，磁盘残留的 Number=5 会被渲染成空，
            //        新旧渲染一样 → 永远「跳过：元数据未变化」，开关开了也修不掉残留。
            if (!opt.ManualEdit && opt.SkipNumber && MetaOptions.IsKavitaFamily(opt.Target)
                && !IsSpecial(m) && !string.IsNullOrEmpty(m.Volume))
            {
                m.Number = "";
            }

            // 5c. 「番外卷号占 0」（独立开关，Custom 档默认开）：覆盖第 5 步的清空 ——
            //     番外 Volume 写 0，Number 按原有顺序照写（Specials 区里靠 Number 排序）。
            //     跟 SkipNumber 相互独立：只开这一个也生效。!ManualEdit 的理由同 5b。
            if (!opt.ManualEdit && opt.SpecialVolumeZero && MetaOptions.IsKavitaFamily(opt.Target)
                && IsSpecial(m))
            {
                m.Volume = "0";
                if (string.IsNullOrEmpty(m.Format)) m.Format = "Special";
            }

            // 6. 关掉「生成收藏」时不写 SeriesGroup。
            //    Kavita 的 ProcessSeries 只凭这个字段建 Collections/收藏，
            //    清空以后重新 Scan 一次，那个只有一本书的收藏就不会再被补回去
            //    （已存在的收藏要自己去 Kavita 里删，扫描不会删用户建的收藏）。
            if (!opt.WriteSeriesGroup) m.SeriesGroup = "";
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
            Tag(sb, "SeriesChinese", data.SeriesChinese);
            Tag(sb, "SeriesOriginal", data.SeriesOriginal);
            Tag(sb, "SeriesEnglish", data.SeriesEnglish);
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
            Tag(sb, "Web", BuildWeb(data, opt.WebSep));
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
            Tag(sb, "AniListId", data.AniListId);
            Tag(sb, "MalId", data.MalId);
            Tag(sb, "MangaBakaId", data.MangaBakaId);
            Tag(sb, "HardcoverId", data.HardcoverId);
            Tag(sb, "ComicVineId", data.ComicVineId);
            Tag(sb, "MetronId", data.MetronId);
            Tag(sb, "ComicBookRoundupId", data.ComicBookRoundupId);
            Tag(sb, "Notes", BuildNotes(data));

            sb.Append("</ComicInfo>");
            return sb.ToString();
        }

        /// <summary>
        /// Web 字段：主链接 + 各个外部库链接，用 opt.WebSep 拼（Kavita 要逗号、Komga 要空格，
        /// 见 MetaOptions.WebSep）。
        ///
        /// Kavita 的「外部元数据」不是从 Notes 读的 —— 它把 ComicInfo 的 &lt;Web&gt; 拆成
        /// WebLinks（有逗号按逗号拆、否则按空格拆），再用 ExternalIdParser 按
        /// `https://anilist.co/manga/` 这类前缀逐个提取 id。**但提 id 那一步只按逗号切**，
        /// 所以空格分隔时链接能显示、id 全空。写在备注里的链接它完全看不见。
        /// 系列级那几个 id 还要求库设置里开了「Inherit Web Links from first chapter」。
        /// </summary>
        static string BuildWeb(Metadata m, string sep)
        {
            var parts = new List<string>();
            // m.Web 里可能本来是好几条（读回来时用空格拼的），先摊平再按目标分隔符重拼，
            // 否则「a b,id1」这种混合格式在 Kavita 里会把 "a b" 当成一条链接
            foreach (var one in SplitWebLinks(m.Web))
                if (!parts.Contains(one)) parts.Add(one);
            foreach (var kv in ExternalLinks(m))
                if (!parts.Contains(kv.Value)) parts.Add(kv.Value);
            return string.Join(string.IsNullOrEmpty(sep) ? " " : sep, parts.ToArray());
        }

        /// <summary>
        /// 拆 &lt;Web&gt; 里的链接：逗号和空格都认（Kavita 写逗号、Komga 写空格，读的时候不挑）。
        /// </summary>
        public static List<string> SplitWebLinks(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var one in text.Split(new[] { ' ', ',', '\t', '\r', '\n' },
                                           StringSplitOptions.RemoveEmptyEntries))
            {
                var link = one.Trim();
                if (link.Length > 0 && !list.Contains(link)) list.Add(link);
            }
            return list;
        }

        /// <summary>
        /// 磁盘上原文的 &lt;Web&gt; 和这次要写的 &lt;Web&gt; 是不是一模一样。
        ///
        /// 为什么单看「渲染结果」不够：链接分隔符改成逗号这类**格式**变化，
        /// 用同一个渲染器比新旧元数据是看不出来的（两边都渲染成逗号），
        /// 文件会被判成「没变化」直接跳过 —— 于是 Kavita 永远读不到外部 id。
        /// </summary>
        public static bool SameWebText(string rawXml, string newXml)
        {
            return string.Equals(WebValue(rawXml), WebValue(newXml), StringComparison.Ordinal);
        }

        static string WebValue(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return "";
            var m = Regex.Match(xml, "<Web>(.*?)</Web>", RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        /// <summary>
        /// Notes 里带上刮削来源与外部库链接：
        /// 一来日后对不上时能一眼看出数据是哪来的，二来别的工具（以及我们自己回读）能认。
        /// 注意：Kavita 不看这里，它只看 &lt;Web&gt;（见 BuildWeb）。
        /// </summary>
        static string BuildNotes(Metadata m)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(m.Notes)) parts.Add(m.Notes.Trim());
            if (!string.IsNullOrEmpty(m.SourceName) && !string.IsNullOrEmpty(m.SourceId))
                parts.Add("Source: " + m.SourceName + " #" + m.SourceId);
            else if (!string.IsNullOrEmpty(m.SourceName))
                parts.Add("Source: " + m.SourceName);
            if (!string.IsNullOrEmpty(m.SourceUrl)) parts.Add(m.SourceUrl.Trim());

            foreach (var kv in ExternalLinks(m))
            {
                var line = kv.Key + ": " + kv.Value;
                if (!parts.Contains(line)) parts.Add(line);
            }
            return string.Join(" | ", parts.ToArray());
        }

        /// <summary>
        /// 外部数据库 id → 链接。Notes 的尾巴、以及「补 id」都靠这一份映射，
        /// 免得链接格式在两处各写一遍、以后改了一处漏一处。
        /// </summary>
        public static List<KeyValuePair<string, string>> ExternalLinks(Metadata m)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (m == null) return list;
            AddLink(list, "AniList", m.AniListId, "https://anilist.co/manga/");
            AddLink(list, "MAL", m.MalId, "https://myanimelist.net/manga/");
            AddLink(list, "MangaBaka", m.MangaBakaId, "https://mangabaka.org/manga/");
            AddLink(list, "Hardcover", m.HardcoverId, "https://hardcover.app/books/");
            // ComicVine 的链接格式是 .../volume/4050-<id>，有人会把 4050- 一起填进来，去掉再说
            AddLink(list, "ComicVine", DigitsOf(m.ComicVineId), "https://comicvine.gamespot.com/volume/4050-");
            AddLink(list, "Metron", m.MetronId, "https://metron.cloud/series/");
            AddLink(list, "Comic Book Roundup", m.ComicBookRoundupId, "https://comicbookroundup.com/");
            return list;
        }

        /// <summary>取 id 里最后一段数字（"4050-123" → "123"）。</summary>
        static string DigitsOf(string raw)
        {
            var s = (raw ?? "").Trim();
            if (s.Length == 0) return "";
            int dash = s.LastIndexOf('-');
            if (dash >= 0) s = s.Substring(dash + 1);
            return Regex.Replace(s, "[^0-9]", "");
        }

        static void AddLink(List<KeyValuePair<string, string>> list, string name, string id, string prefix)
        {
            if (string.IsNullOrEmpty(id)) return;
            list.Add(new KeyValuePair<string, string>(name, prefix + id));
        }

        /// <summary>这个 URL 是不是我们写进 Web 的外部库链接（读回时要挑出来，别当主链接）。</summary>
        static bool IsExternalLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            foreach (var p in new[]
            {
                "https://anilist.co/manga/", "https://myanimelist.net/manga/",
                "https://mangabaka.org/", "https://hardcover.app/",
                "https://comicvine.gamespot.com/", "https://metron.cloud/",
                "https://comicbookroundup.com/"
            })
                if (url.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>外部 id 的字段名 → 取值器：解析、补写、回读三处共用。</summary>
        public static readonly string[] ExternalIdFields =
        {
            "AniListId", "MalId", "MangaBakaId", "HardcoverId",
            "ComicVineId", "MetronId", "ComicBookRoundupId"
        };

        /// <summary>从一段自由文本（旧版只写在 Notes 里的链接）里认出外部 id。</summary>
        public static void ScanExternalLinks(string text, Metadata m)
        {
            if (string.IsNullOrEmpty(text) || m == null) return;
            Grab(text, @"anilist\.co/manga/(\d+)", ref m.AniListId);
            Grab(text, @"myanimelist\.net/manga/(\d+)", ref m.MalId);
            Grab(text, @"mangabaka\.(?:org|dev)/manga/(\d+)", ref m.MangaBakaId);
            Grab(text, @"hardcover\.app/books/([A-Za-z0-9\-_]+)", ref m.HardcoverId);
            Grab(text, @"comicvine\.gamespot\.com/(?:volume|series)/4050-(\d+)", ref m.ComicVineId);
            Grab(text, @"metron\.cloud/series/(\d+)", ref m.MetronId);
            Grab(text, @"comicbookroundup\.com/([A-Za-z0-9\-_/]+)", ref m.ComicBookRoundupId);
        }

        static void Grab(string text, string pattern, ref string field)
        {
            if (!string.IsNullOrEmpty(field)) return;
            var hit = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (hit.Success) field = hit.Groups[1].Value.Trim('/');
        }

        /// <summary>
        /// 把「备注」拆成「用户写的部分」和「程序追加的来源尾巴」。
        ///
        /// 为什么必须有这一步：Notes 是个纯文本字段，写出去是
        /// `原作：xxx | Source: Bangumi #6151 | https://bgm.tv/subject/6151`，
        /// 读回来如果整串当成 Notes，下次再写就会在后面再接一遍 Source —— 于是
        /// 「重新刮削同一个条目，备注却变了」。拆开以后，重复写入是幂等的。
        /// </summary>
        public static void SplitNotes(string notes, out string body, out string sourceName,
                                      out string sourceId, out string sourceUrl)
        {
            body = (notes ?? "").Trim();
            sourceName = ""; sourceId = ""; sourceUrl = "";

            var parts = (notes ?? "").Split(new[] { " | " }, StringSplitOptions.None);
            int cut = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].TrimStart().StartsWith("Source:", StringComparison.OrdinalIgnoreCase))
                { cut = i; break; }
            }
            if (cut < 0) return;   // 没有程序写的尾巴，原样当用户内容

            var rest = parts[cut].Trim().Substring("Source:".Length).Trim();
            int hash = rest.LastIndexOf('#');
            if (hash > 0)
            {
                sourceName = rest.Substring(0, hash).Trim();
                sourceId = rest.Substring(hash + 1).Trim();
            }
            else sourceName = rest;

            var keep = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i == cut) continue;
                if (i > cut)
                {
                    var seg = parts[i].Trim();
                    if (sourceUrl.Length == 0 &&
                        seg.StartsWith("http", StringComparison.OrdinalIgnoreCase)) sourceUrl = seg;
                    continue;      // 尾巴里除了 URL 都不留
                }
                keep.Add(parts[i]);
            }
            body = string.Join(" | ", keep.ToArray()).Trim();
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
            // XML 解析器读回来时会把 CRLF 折成 LF：写的时候不折，同一段文本
            // 写进去再读出来就少一个字符，「对比」会把它当成改过（简介最常撞上）。
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
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

        /// <summary>
        /// 只补外部数据库 id（以及 Notes 里的链接），其余字段一个都不动。
        /// 「获取额外数据库 id」走这里：整份重写会把用户手改过的字段一起盖掉。
        /// </summary>
        public static bool PatchExternalIds(string path, Metadata ids, string webSep,
                                            out bool changed, out string err)
        {
            changed = false; err = "";
            try
            {
                string raw;
                using (var fs = File.OpenRead(path))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                    raw = ReadRawFromArchive(zip);
                if (string.IsNullOrEmpty(raw)) { err = "包内没有 ComicInfo.xml"; return false; }

                XDocument doc;
                // PreserveWhitespace：只补几个标签，不想把整份 XML 重排一遍，
                // 那样 diff 起来满屏都是白的
                try { doc = XDocument.Parse(raw, LoadOptions.PreserveWhitespace); }
                catch (Exception ex) { err = "现有 XML 解析不了：" + ex.Message; return false; }
                var root = doc.Root;
                if (root == null) { err = "现有 XML 没有根元素"; return false; }

                var pairs = new[]
                {
                    new KeyValuePair<string, string>("AniListId", ids.AniListId),
                    new KeyValuePair<string, string>("MalId", ids.MalId),
                    new KeyValuePair<string, string>("MangaBakaId", ids.MangaBakaId),
                    new KeyValuePair<string, string>("HardcoverId", ids.HardcoverId),
                    new KeyValuePair<string, string>("ComicVineId", ids.ComicVineId),
                    new KeyValuePair<string, string>("MetronId", ids.MetronId),
                    new KeyValuePair<string, string>("ComicBookRoundupId", ids.ComicBookRoundupId)
                };
                foreach (var kv in pairs)
                {
                    if (string.IsNullOrEmpty(kv.Value)) continue;
                    var e = root.Element(kv.Key);
                    if (e == null) { root.Add(new XElement(kv.Key, kv.Value)); changed = true; }
                    else if ((e.Value ?? "").Trim() != kv.Value) { e.Value = kv.Value; changed = true; }
                }

                // 链接也补进备注：Kavita 的「外部元数据」是从链接里认 id 的
                var links = ExternalLinks(ids);
                if (links.Count > 0)
                {
                    // 1) Web —— Kavita 真正读的地方。整条按目标服务器的分隔符重排：
                    //    旧文件可能是空格分隔的（那种 Kavita 的 ExtractId 认不出 id），
                    //    这次修补顺手规范成逗号，不用等下一次整组重写。
                    var web = root.Element("Web");
                    var webText = web == null ? "" : (web.Value ?? "");
                    var have = SplitWebLinks(webText);
                    foreach (var kv in links)
                        if (!have.Exists(x => string.Equals(x, kv.Value, StringComparison.OrdinalIgnoreCase)))
                            have.Add(kv.Value);
                    var mergedWeb = string.Join(string.IsNullOrEmpty(webSep) ? " " : webSep, have.ToArray());
                    if (mergedWeb.Length > 0 && !string.Equals(mergedWeb, webText.Trim(), StringComparison.Ordinal))
                    {
                        if (web == null) root.Add(new XElement("Web", mergedWeb));
                        else web.Value = mergedWeb;
                        changed = true;
                    }

                    // 2) Notes —— 给人看、给别的工具看的留痕
                    var notes = root.Element("Notes");
                    var text = notes == null ? "" : (notes.Value ?? "");
                    var add = new List<string>();
                    foreach (var kv in links)
                        if (text.IndexOf(kv.Value, StringComparison.OrdinalIgnoreCase) < 0)
                            add.Add(kv.Key + ": " + kv.Value);
                    if (add.Count > 0)
                    {
                        var merged = (text.Trim().Length == 0 ? "" : text.TrimEnd() + " | ")
                                   + string.Join(" | ", add.ToArray());
                        if (notes == null) root.Add(new XElement("Notes", merged));
                        else notes.Value = merged;
                        changed = true;
                    }
                }

                if (!changed) return true;          // 已经是这个值，别去动文件的 mtime
                ReplaceXml(path, Serialize(doc));
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        static byte[] Serialize(XDocument doc)
        {
            var ms = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false,
                Indent = false
            };
            using (var w = XmlWriter.Create(ms, settings)) doc.Save(w);
            return ms.ToArray();
        }

        /// <summary>换掉包内的 ComicInfo.xml，其余条目按原压缩方式原样搬运。</summary>
        static void ReplaceXml(string path, byte[] xmlBytes)
        {
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            try
            {
                using (var srcFs = File.OpenRead(path))
                using (var src = new ZipArchive(srcFs, ZipArchiveMode.Read))
                using (var dstFs = File.Create(tmp))
                using (var dst = new ZipArchive(dstFs, ZipArchiveMode.Create))
                {
                    foreach (var e in src.Entries)
                    {
                        if (e.FullName.IndexOf('/') < 0 &&
                            string.Equals(e.FullName, EntryName, StringComparison.OrdinalIgnoreCase)) continue;
                        var level = e.CompressedLength < e.Length
                            ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
                        var ne = dst.CreateEntry(e.FullName, level);
                        try { ne.LastWriteTime = e.LastWriteTime; } catch { }
                        using (var os = ne.Open())
                        using (var ins = e.Open())
                            ins.CopyTo(os);
                    }
                    var ci = dst.CreateEntry(EntryName, CompressionLevel.Optimal);
                    using (var os = ci.Open()) os.Write(xmlBytes, 0, xmlBytes.Length);
                }
                try { File.Replace(tmp, path, null, true); }
                catch
                {
                    File.Delete(path);
                    File.Move(tmp, path);
                }
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
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
                    case "SeriesChinese": case "ChineseSeries": m.SeriesChinese = v; break;
                    case "SeriesOriginal": case "OriginalSeries": m.SeriesOriginal = v; break;
                    case "SeriesEnglish": case "EnglishSeries": m.SeriesEnglish = v; break;
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
                    case "Web":
                        {
                            // Web 里混着主链接和外部库链接（空格分隔）。外部链接还原成 id 字段，
                            // m.Web 只留主链接 —— 否则下次渲染会把外部链接再拼一遍，越接越长。
                            var keep = new List<string>();
                            foreach (var one in v.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                var link = one.Trim();
                                if (link.Length == 0) continue;
                                ScanExternalLinks(link, m);
                                if (!IsExternalLink(link)) keep.Add(link);
                            }
                            m.Web = string.Join(" ", keep.ToArray());
                            break;
                        }
                    case "LanguageISO": m.LanguageISO = v; break;
                    case "Characters": m.Characters = v; break;
                    case "Teams": m.Teams = v; break;
                    case "Locations": m.Locations = v; break;
                    case "ScanInformation": m.ScanInformation = v; break;
                    case "StoryArc": m.StoryArc = v; break;
                    case "StoryArcNumber": m.StoryArcNumber = v; break;
                    case "SeriesGroup": m.SeriesGroup = v; break;
                    // 标准字段名是 AlternateSeries / AlternateNumber（Anansi v2.1）。
                    // Kavita 的文档表格里写成了 AlternativeSeries / AlternativeCount，
                    // 有些工具照着那个表写，所以两种拼写都认。
                    case "AlternateSeries": case "AlternativeSeries": m.AlternateSeries = v; break;
                    case "AlternateNumber": case "AlternativeNumber": case "AlternativeCount":
                        m.AlternateNumber = v; break;
                    case "Manga": m.Manga = v; break;
                    case "AgeRating": m.AgeRating = v; break;
                    case "Format": m.Format = v; break;
                    case "GTIN": m.GTIN = v; break;
                    case "AniListId": m.AniListId = v; break;
                    case "MalId": case "MALId": m.MalId = v; break;
                    case "MangaBakaId": m.MangaBakaId = v; break;
                    case "HardcoverId": m.HardcoverId = v; break;
                    case "ComicVineId": m.ComicVineId = v; break;
                    case "MetronId": m.MetronId = v; break;
                    case "ComicBookRoundupId": m.ComicBookRoundupId = v; break;
                    case "Notes":
                        {
                            // 备注里混着程序追加的「Source: 来源 #id | URL」与外部库链接，
                            // 拆出来还原成结构化字段，这样再写一遍时不会把尾巴越堆越长
                            string body, sn, sid, url;
                            SplitNotes(v, out body, out sn, out sid, out url);
                            m.Notes = body;
                            if (sn.Length > 0) m.SourceName = sn;
                            if (sid.Length > 0) m.SourceId = sid;
                            if (url.Length > 0) m.SourceUrl = url;
                            // 旧版只把 AniList / MAL 链接写在备注里，没有独立标签：
                            // 从原文里再认一遍，才不会因为「拆尾巴」把它们丢掉
                            ScanExternalLinks(v, m);
                            break;
                        }
                }
            }

            // 老文件只有 LocalizedSeries（当时存的是原名），没有三个新标签：
            // 带假名的当原名、不带的当中文名，免得重写一次就把另一个名字弄丢。
            if (!string.IsNullOrEmpty(m.LocalizedSeries) && m.LocalizedSeries != m.Series)
            {
                if (HasKana(m.LocalizedSeries))
                {
                    if (m.SeriesOriginal.Length == 0) m.SeriesOriginal = m.LocalizedSeries;
                }
                else if (m.SeriesChinese.Length == 0) m.SeriesChinese = m.LocalizedSeries;
            }
            return m;
        }

        static bool HasKana(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s)
                if ((c >= '\u3041' && c <= '\u309f') || (c >= '\u30a1' && c <= '\u30fa')) return true;
            return false;
        }
    }

    /// <summary>
    /// 系列级侧车文件，放系列目录根部。
    /// Kavita Custom（kavita 档）写 Custom 侧车格式：简介 / 状态 / 年份 / 语言 / 分级 /
    /// 出版社 / 链接 / 总卷数 / 类型 / 标签 / 人物 12 类字段，Kavita 侧优先级高于 ComicInfo；
    /// 其余档位（Komga 等）写 Mylar 的 series.json 老格式。
    /// </summary>
    public static class SeriesJson
    {
        public const string FileName = "series.json";

        public static string Build(Metadata m, int totalIssues)
        {
            return Build(m, totalIssues, "both");
        }

        /// <param name="target">kavitacustom = Kavita Custom（Custom 侧车格式）；其余写 Komga 的 Mylar 格式（原版 Kavita 不读它，无害）。</param>
        public static string Build(Metadata m, int totalIssues, string target)
        {
            if (target == "kavitacustom") return BuildCustom(m, totalIssues);

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

        // ------------------------------------------------------ Kavita Custom 侧车格式
        //
        // 键名与 Kavita.Services.Scanner.SeriesJsonSidecar 一一对应（大小写不敏感、
        // 允许注释和尾逗号）。空字段直接不写：侧车的优先级高于 ComicInfo，
        // 写一堆空串反而会把 Kavita 自己从文件名 / ComicInfo 推出来的值顶掉。

        static string BuildCustom(Metadata m, int totalIssues)
        {
            var o = new Dictionary<string, object>();

            // name 故意不写：改名会让 Kavita 在重扫时把同一系列拆成两个（见侧车解析端注释）
            Put(o, "sortName", m.SeriesSort);
            // 本地化名 = 中文译名；显示名本来就是中文时写同值也无害
            var localized = FirstNonEmpty(m.SeriesChinese, m.LocalizedSeries);
            Put(o, "localizedName", localized);
            Put(o, "summary", StripHtml(m.Summary));
            Put(o, "status", StatusCustom(m, totalIssues));
            var year = ParseYear(m.Year);
            if (year > 0) o["releaseYear"] = year;
            Put(o, "language", m.LanguageISO);
            Put(o, "ageRating", m.AgeRating);
            Put(o, "publisher", m.Publisher);

            var links = WebLinks(m);
            if (links.Count > 0) o["webLinks"] = links;

            var total = TotalBooks(m, totalIssues);
            if (total > 0) o["totalBookCount"] = total;

            var genres = SplitList(m.Genre);
            if (genres.Count > 0) o["genres"] = genres;
            var tags = SplitList(m.Tags);
            if (tags.Count > 0) o["tags"] = tags;

            var people = People(m);
            if (people.Count > 0) o["people"] = people;

            return MiniJson.Serialize(o);
        }

        /// <summary>非空才写：Custom 侧车优先级高于 ComicInfo，空值不写免得顶掉 Kavita 自己推的值。</summary>
        static void Put(Dictionary<string, object> o, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) o[key] = value.Trim();
        }

        static string FirstNonEmpty(string a, string b)
        {
            if (!string.IsNullOrWhiteSpace(a)) return a;
            return b ?? "";
        }

        /// <summary>
        /// Kavita Custom 认 Ongoing / Hiatus / Completed / Cancelled / Ended（Continuing 它不认）。
        /// 来源站不给出版状态，只能按现有信息推：番外单卷当已完结；
        /// 报了总卷数且盘上已收齐 → Completed；其余 → Ongoing。
        /// </summary>
        static string StatusCustom(Metadata m, int totalIssues)
        {
            if (!string.IsNullOrEmpty(m.Format) &&
                m.Format.IndexOf("special", StringComparison.OrdinalIgnoreCase) >= 0) return "Ended";
            int count;
            if (int.TryParse(m.Count, out count) && count > 0 && totalIssues >= count) return "Completed";
            return "Ongoing";
        }

        /// <summary>总卷数：报了 Count 用 Count（是「应出总卷数」，Kavita 拿它算缺卷），否则退回盘上本数。</summary>
        static int TotalBooks(Metadata m, int totalIssues)
        {
            int count;
            if (int.TryParse(m.Count, out count) && count > 0) return count;
            return totalIssues;
        }

        /// <summary>主链接 + 外部库链接 + 刮削来源页：webLinks 进 Kavita 的 WebLinks，id 解析也认这些前缀。</summary>
        static List<string> WebLinks(Metadata m)
        {
            var list = new List<string>();
            foreach (var one in ComicInfo.SplitWebLinks(m.Web))
                if (!list.Contains(one)) list.Add(one);
            foreach (var kv in ComicInfo.ExternalLinks(m))
                if (!list.Contains(kv.Value)) list.Add(kv.Value);
            var src = (m.SourceUrl ?? "").Trim();
            if (src.Length > 0 && !list.Contains(src)) list.Add(src);
            return list;
        }

        /// <summary>逗号 / 顿号分隔的一组值：去空白、去重、保序。</summary>
        static List<string> SplitList(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            foreach (var one in csv.Split(new[] { ',', '，', '、', '\t', '\r', '\n' },
                                          StringSplitOptions.RemoveEmptyEntries))
            {
                var v = one.Trim();
                if (v.Length > 0 && !list.Contains(v)) list.Add(v);
            }
            return list;
        }

        /// <summary>人物：键名按侧车约定的角色（artist 别名解析端自动归到 penciller）。</summary>
        static Dictionary<string, object> People(Metadata m)
        {
            var p = new Dictionary<string, object>();
            AddPeople(p, "writer", m.Writer);
            AddPeople(p, "artist", m.Penciller);
            AddPeople(p, "inker", m.Inker);
            AddPeople(p, "colorist", m.Colorist);
            AddPeople(p, "letterer", m.Letterer);
            AddPeople(p, "coverArtist", m.CoverArtist);
            AddPeople(p, "editor", m.Editor);
            AddPeople(p, "translator", m.Translator);
            AddPeople(p, "character", m.Characters);
            AddPeople(p, "team", m.Teams);
            AddPeople(p, "location", m.Locations);
            return p;
        }

        static void AddPeople(Dictionary<string, object> p, string role, string csv)
        {
            var names = SplitList(csv);
            if (names.Count > 0) p[role] = names;
        }

        static string Status(Metadata m)
        {
            if (!string.IsNullOrEmpty(m.Format) &&
                m.Format.IndexOf("special", StringComparison.OrdinalIgnoreCase) >= 0) return "Ended";
            return "Continuing";
        }

        /// <summary>
        /// 把「获取额外数据库 id」新拿到的链接并进已有侧车的 webLinks：只动 webLinks 一个键，
        /// 其余字段原样保留（简介 / 人物 / 标签都是界面刮出来的，不能在这里丢）。
        /// 返回新增链接数；文件不存在 / 解析失败 / 没有新链接返回 -1 / 0，调用方只记日志。
        /// </summary>
        public static int MergeWebLinks(string path, Metadata ids)
        {
            try
            {
                if (ids == null || !File.Exists(path)) return -1;
                var urls = WebLinks(ids);        // ids 里只有 id → 全是外部库 URL
                if (urls.Count == 0) return -1;

                var json = File.ReadAllText(path, Encoding.UTF8);
                var root = MiniJson.Parse(json) as Dictionary<string, object>;
                if (root == null) return -1;

                var merged = new List<string>();
                var cur = root.ContainsKey("webLinks") ? root["webLinks"] as object[] : null;
                if (cur != null)
                    foreach (var x in cur)
                    {
                        var s = (x == null ? "" : x.ToString()).Trim();
                        if (s.Length > 0 && !merged.Contains(s)) merged.Add(s);
                    }

                int added = 0;
                foreach (var u in urls)
                    if (!merged.Contains(u)) { merged.Add(u); added++; }

                if (added > 0)
                {
                    root["webLinks"] = merged;
                    File.WriteAllText(path, MiniJson.Serialize(root), new UTF8Encoding(false));
                }
                return added;
            }
            catch (Exception ex)
            {
                App.Dump("series.json 链接更新失败 " + path + "：" + ex.Message);
                return -1;
            }
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
            /// 固定开启、不由界面控制：一旦关过一轮，那个文件里「最初的样子」就永久丢了。
            /// 字段留着只是为了老配置还能反序列化。
            /// </summary>
            public bool KeepOriginal = true;

        /// <summary>封面挑哪一版：auto（中文优先，没有就原版）/ zh / original。</summary>
        public string CoverPick = "auto";

        /// <summary>封面宽度上限，0 = 原图尺寸（只重编码成 JPEG）。</summary>
        public int CoverMaxWidth = 800;
            public MetaOptions Meta = new MetaOptions();

            /// <summary>按册写入时要单独改 Meta 里的开关（不能动共享那份，多线程会串）。</summary>
            public Options Clone()
            {
                var c = (Options)MemberwiseClone();
                c.Meta = Meta == null ? new MetaOptions() : Meta.Clone();
                return c;
            }
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
                    // 旧 XML 原文先读出来：判断要不要跳过、以及留档，都要用
                    var oldRaw = ComicInfo.ReadRawFromArchive(src);

                    // 内容没变就不重打包：大文件重打包一次几十秒，白跑。
                    // 但「渲染结果一样」还不够：磁盘上的 <Web> 可能还是空格分隔的老格式，
                    // Kavita 的 id 解析只按逗号切 —— 这种文件必须重写一次，否则永远修不好。
                    //
                    // 旧文件用「严格渲染」对比（关掉缺号互补 / 番外清卷号这些改值规则）：
                    // 若按归一化渲染旧值，规则对旧值和定值各做一遍会互相抵消 ——
                    // 磁盘上番外带着 Volume=1，刮削规则要把它清掉，两边的渲染结果却一样，
                    // 于是永远「跳过：元数据没有变化」，规则永远落不了盘。
                    // 严格渲染暴露的是「磁盘原值 vs 这次要写的值」的真实差异。
                    var old = ComicInfo.ReadFromArchive(src);
                    if (old != null)
                    {
                        var strict = opt.Meta.Clone();
                        strict.FillMissingNumbers = false;
                        strict.ManualEdit = true;
                        var oldXml = ComicInfo.Render(old, strict);
                        if (oldXml == xml && ComicInfo.SameWebText(oldRaw, xml))
                        {
                            res.Skipped = true; res.Note = "元数据未变化"; return res;
                        }
                    }

                    // 无条件保留「最初那一版」：旧文件里已有留档块就原样搬回来，
                    // 没有才把这次覆盖前的内容记成原始。开关关过也不丢（见 KeepOriginal）。
                    var finalXml = ComicInfo.KeepOriginal(xml, oldRaw);
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
                        // 只留最先那一份：.bak 是「刮削前的原件」，不能被后续每一轮覆盖成
                        // 「上一轮刮削的结果」—— 那样想退回最原始的样子就没得退了。
                        if (!File.Exists(bak)) File.Copy(path, bak, true);
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
                    var json = SeriesJson.Build(meta, totalIssues,
                        opt.Meta == null ? "both" : opt.Meta.Target);
                    // 内容相同就不落盘，避免每次都刷新 mtime 触发服务器重扫
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
