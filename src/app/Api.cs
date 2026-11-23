using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ComicManager
{
    /// <summary>本地 HTTP 服务（TcpListener 实现，避免 HttpListener 的 URL ACL 限制）。</summary>
    public static class Api
    {
        public static int Port = 8765;
        public static string ConfigPath;

        static TcpListener _tcp;
        static Thread _accept;
        static Thread _worker;
        static volatile bool _stop;
        static readonly object Sync = new object();

        static readonly List<Dictionary<string, object>> Logs = new List<Dictionary<string, object>>();
        static bool _running;
        static string _phase = "待命";
        static int _total, _done, _ok, _err, _skip;
        static string _current = "";

        public static Dictionary<string, object> DefaultConfig()
        {
            return new Dictionary<string, object>
            {
                {"sources", new object[0]},
                {"out", ""},
                {"target", "both"},
                {"reading", "auto"},
                {"t2s", "tw2s"},
                {"compress", "store"},
                {"special", "series"},
                {"special_suffix", "番外"},
                {"name_template", "{series} v{vol:02d}"},
                {"name_template_komga", "{series} #{vol3}"},
                {"jobs", 6},
                {"overwrite", false},
                {"drop_junk", true},
                {"keep_label", true},
                {"t2s_label", true},
                {"limit", 0},
                {"mode", "convert"},

                // 刮削页的设置与转换页分开存：两页各自的参数互不干扰
                {"page", "home"},
                {"scrape_sources", new object[] { MetaSources.Bangumi, MetaSources.MangaUpdates }},
                {"scrape_dirs", new object[0]},
                {"scrape_target", "both"},
                {"scrape_backup", true},
                {"scrape_series_json", true},
                {"scrape_cover", true},
                {"scrape_cover_overwrite", false},
                {"scrape_threads", 3},
                {"scrape_cover_pick", "auto"},
                {"scrape_cover_width", 800},
                {"scrape_fill_missing", true},
                {"scrape_special_number", false},
                {"scrape_keep_raw", true},
                {"scrape_rename", false},
                {"rename_template", "{series}/{id}"},
                {"last_dir", ""},
                {"scrape_split_suffix", true}
            };
        }

        public static Dictionary<string, object> LoadConfig()
        {
            var cfg = DefaultConfig();
            try
            {
                if (ConfigPath != null && File.Exists(ConfigPath))
                {
                    var d = MiniJson.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8)) as Dictionary<string, object>;
                    if (d != null) foreach (var kv in d) cfg[kv.Key] = kv.Value;
                }
            }
            catch { }
            return cfg;
        }

        public static bool SaveConfig(Dictionary<string, object> cfg)
        {
            try
            {
                if (ConfigPath == null) return false;
                File.WriteAllText(ConfigPath, MiniJson.Serialize(cfg), new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        static void Log(string msg, string level)
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

        static void ResetState()
        {
            lock (Sync)
            {
                _running = true; _phase = "准备中";
                _total = 0; _done = 0; _ok = 0; _err = 0; _skip = 0;
                _current = ""; Logs.Clear();
            }
        }

        static void Bump(string field)
        {
            lock (Sync)
            {
                if (field == "ok") _ok++;
                else if (field == "err") _err++;
                else if (field == "skip") _skip++;
                _done++;
            }
        }

        static void Finish(string phase)
        {
            lock (Sync) { _running = false; _phase = phase; }
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

        // ---------------------------------------------------------------- 任务
        static void RunConvert(Dictionary<string, object> cfg)
        {
            try
            {
                ResetState();
                Log("扫描源目录…", "i");
                var errors = new List<string>();
                var items = Core.ScanSources(cfg, errors, delegate(string m) { Log(m, "i"); });
                lock (Sync) { _total = items.Count; _phase = "转换中"; }
                foreach (var e in errors.Take(80)) Log("跳过 " + e, "w");
                if (items.Count == 0) { Finish("没有可转换的文件"); return; }

                int jobs = Math.Max(1, Core.GetInt(cfg, "jobs", 6));
                Log("共 " + items.Count + " 个文件，" + jobs + " 并发", "i");

                int idx = -1;
                var threads = new List<Thread>();
                for (int t = 0; t < jobs; t++)
                {
                    var th = new Thread(delegate()
                    {
                        while (true)
                        {
                            int i = Interlocked.Increment(ref idx);
                            if (i >= items.Count || _stop) break;
                            var it = items[i];
                            try
                            {
                                var r = Core.ConvertOne(it, cfg, null);
                                if (r.Skip) Bump("skip");
                                else if (r.Err != null) { Bump("err"); Log("失败 " + Path.GetFileName(it.Src) + "：" + r.Err, "e"); }
                                else
                                {
                                    Bump("ok");
                                    lock (Sync) { _current = r.Series + " v" + r.Vol; }
                                    Log("成功 " + Path.GetFileName(r.Dst) + "  " + r.Mb + " MB", "i");
                                }
                            }
                            catch (Exception ex)
                            {
                                Bump("err");
                                Log("失败 " + Path.GetFileName(it.Src) + "：" + ex.Message, "e");
                            }
                        }
                    });
                    th.IsBackground = true;
                    threads.Add(th);
                    th.Start();
                }
                foreach (var th in threads) th.Join();

                int ok, er, sk;
                lock (Sync) { ok = _ok; er = _err; sk = _skip; }
                Log("完成：成功 " + ok + "，跳过 " + sk + "，失败 " + er, er == 0 ? "s" : "w");
                Finish(_stop ? "已停止" : "完成");
            }
            catch (Exception ex)
            {
                Log("致命错误：" + ex.Message, "e");
                Finish("出错");
            }
        }

        static void RunRepack(Dictionary<string, object> cfg)
        {
            try
            {
                ResetState();
                var files = new List<string>();
                foreach (var s in Core.GetList(cfg, "sources"))
                    if (Directory.Exists(s))
                        files.AddRange(Directory.GetFiles(s, "*.cbz", SearchOption.AllDirectories).OrderBy(x => x));

                lock (Sync) { _total = files.Count; _phase = "互转中"; }
                Log("找到 " + files.Count + " 个 CBZ → 目标 " + Core.GetStr(cfg, "target", "both"), "i");
                if (files.Count == 0) { Finish("没有 CBZ"); return; }

                foreach (var f in files)
                {
                    if (_stop) break;
                    try
                    {
                        var r = Repack.RepackOne(f, cfg);
                        if (r == "skip" || r == "same") { Bump("skip"); continue; }
                        if (r != null) { Bump("err"); Log("失败 " + Path.GetFileName(f) + "：" + r, "e"); continue; }
                        Bump("ok");
                        Log("成功 " + Path.GetFileName(f), "i");
                    }
                    catch (Exception ex)
                    {
                        Bump("err");
                        Log("失败 " + Path.GetFileName(f) + "：" + ex.Message, "e");
                    }
                }
                int ok, er, sk;
                lock (Sync) { ok = _ok; er = _err; sk = _skip; }
                Log("完成：成功 " + ok + "，跳过 " + sk + "，失败 " + er, er == 0 ? "s" : "w");
                Finish("完成");
            }
            catch (Exception ex)
            {
                Log("致命错误：" + ex.Message, "e");
                Finish("出错");
            }
        }

        static Dictionary<string, object> RunPreview(Dictionary<string, object> cfg)
        {
            var errors = new List<string>();
            var items = Core.ScanSources(cfg, errors, delegate(string m) { Log(m, "i"); });
            var rows = new List<object>();
            foreach (var it in items)
            {
                rows.Add(new Dictionary<string, object>
                {
                    {"src", Path.GetFileName(it.Src)},
                    {"folder", Core.SeriesFolder(it, cfg)},
                    {"file", Core.BuildFileName(it, cfg)},
                    {"pages", it.Pages},
                    {"vol", it.Vol ?? 0},
                    {"special", it.IsSpecial},
                    {"dir", it.Reading},
                    {"country", it.Country},
                    {"origin", it.CountrySource}
                });
            }
            return new Dictionary<string, object> { { "rows", rows }, { "errors", errors } };
        }

        // ---------------------------------------------------------------- TCP HTTP
        class Req
        {
            public string Method = "";
            public string Path = "";
            public string Body = "";
        }

        public static bool Start()
        {
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    _tcp = new TcpListener(IPAddress.Loopback, Port);
                    _tcp.Start();
                    _accept = new Thread(AcceptLoop) { IsBackground = true };
                    _accept.Start();
                    return true;
                }
                catch { Port++; }
            }
            return false;
        }

        public static void Stop()
        {
            _stop = true;
            try { if (_tcp != null) _tcp.Stop(); } catch { }
        }

        static void AcceptLoop()
        {
            while (true)
            {
                TcpClient c;
                try { c = _tcp.AcceptTcpClient(); }
                catch { return; }
                var t = new Thread(delegate() { Serve(c); }) { IsBackground = true };
                t.Start();
            }
        }

        static void Serve(TcpClient client)
        {
            try
            {
                using (client)
                using (var ns = client.GetStream())
                {
                    ns.ReadTimeout = 120000;
                    ns.WriteTimeout = 120000;
                    var req = ReadRequest(ns);
                    if (req == null) return;

                    int code; string body, type;
                    Route(req, out code, out body, out type);
                    WriteResponse(ns, code, body, type);
                }
            }
            catch (Exception ex)
            {
                // 单个请求出错不能连累整个服务，但要把原因留在日志里
                Log("请求处理失败：" + ex.Message, "e");
            }
        }

        static Req ReadRequest(NetworkStream ns)
        {
            var buf = new List<byte>(4096);
            var one = new byte[1];
            while (true)
            {
                int n;
                try { n = ns.Read(one, 0, 1); }
                catch { return null; }
                if (n <= 0) return null;
                buf.Add(one[0]);
                int c = buf.Count;
                if (c >= 4 && buf[c - 4] == 13 && buf[c - 3] == 10 && buf[c - 2] == 13 && buf[c - 1] == 10) break;
                if (c > 32768) return null;
            }

            var head = Encoding.UTF8.GetString(buf.ToArray());
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var req = new Req();
            var first = lines[0].Split(' ');
            if (first.Length >= 2) { req.Method = first[0].ToUpperInvariant(); req.Path = first[1]; }

            int len = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var idx = lines[i].IndexOf(':');
                if (idx <= 0) continue;
                if (lines[i].Substring(0, idx).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(lines[i].Substring(idx + 1).Trim(), out len);
            }

            if (len > 0)
            {
                var body = new byte[len];
                int read = 0;
                while (read < len)
                {
                    int n;
                    try { n = ns.Read(body, read, len - read); }
                    catch { break; }
                    if (n <= 0) break;
                    read += n;
                }
                req.Body = Encoding.UTF8.GetString(body, 0, read);
            }

            var q = req.Path.IndexOf('?');
            if (q >= 0) req.Path = req.Path.Substring(0, q);
            return req;
        }

        static void WriteResponse(NetworkStream ns, int code, string body, string type)
        {
            var bytes = Encoding.UTF8.GetBytes(body ?? "");
            var head = "HTTP/1.1 " + code + " " +
                       (code == 200 ? "OK" : code == 404 ? "Not Found" : code == 409 ? "Conflict" : "Error") + "\r\n" +
                       "Content-Type: " + type + "\r\n" +
                       "Content-Length: " + bytes.Length + "\r\n" +
                       "Cache-Control: no-store\r\n" +
                       "Connection: close\r\n\r\n";
            var hb = Encoding.ASCII.GetBytes(head);
            ns.Write(hb, 0, hb.Length);
            ns.Write(bytes, 0, bytes.Length);
            ns.Flush();
        }

        static void Route(Req req, out int code, out string body, out string type)
        {
            code = 200; type = "application/json; charset=utf-8"; body = "{}";

            if (req.Method == "GET" && (req.Path == "/" || req.Path == "/index.html"))
            {
                type = "text/html; charset=utf-8";
                body = Page.Html;
                return;
            }
            if (req.Method == "GET" && req.Path == "/api/status")
            {
                body = MiniJson.Serialize(Status());
                return;
            }
            if (req.Method == "GET" && req.Path == "/api/version")
            {
                body = MiniJson.Serialize(new Dictionary<string, object>
                {
                    {"name", Brand.Name},
                    {"version", Brand.Version},
                    {"tagline", Brand.Tagline},
                    {"author", Brand.Author},
                    {"email", Brand.Email}
                });
                return;
            }
            if (req.Method == "GET" && req.Path == "/api/config")
            {
                body = MiniJson.Serialize(new Dictionary<string, object> { { "cfg", LoadConfig() } });
                return;
            }

            if (req.Method == "GET" && req.Path == "/api/scrape/status")
            {
                body = MiniJson.Serialize(Scrape.Status());
                return;
            }
            if (req.Method == "POST")
            {
                var data = MiniJson.Parse(req.Body) as Dictionary<string, object>;
                if (data == null) data = new Dictionary<string, object>();

                if (req.Path == "/api/config")
                {
                    Dictionary<string, object> cfg;
                    if (data.ContainsKey("cfg") && data["cfg"] is Dictionary<string, object>)
                        cfg = (Dictionary<string, object>)data["cfg"];
                    else cfg = data;
                    body = MiniJson.Serialize(new Dictionary<string, object> { { "ok", SaveConfig(cfg) } });
                    return;
                }
                if (req.Path == "/api/pick")
                {
                    body = MiniJson.Serialize(new Dictionary<string, object> { { "path", PickFolder() } });
                    return;
                }
                if (req.Path == "/api/copy")
                {
                    body = MiniJson.Serialize(new Dictionary<string, object>
                    {
                        {"ok", CopyText(Core.GetStr(data, "text", ""))}
                    });
                    return;
                }
                if (req.Path == "/api/preview")
                {
                    body = MiniJson.Serialize(RunPreview(data));
                    return;
                }
                if (req.Path == "/api/start")
                {
                    if (_running) { code = 409; body = "{\"err\":\"已有任务在跑\"}"; return; }
                    _stop = false;
                    SaveConfig(data);
                    var cfgCopy = data;
                    var mode = Core.GetStr(data, "mode", "convert");
                    _worker = new Thread(delegate() { if (mode == "repack") RunRepack(cfgCopy); else RunConvert(cfgCopy); });
                    _worker.IsBackground = true;
                    _worker.Start();
                    body = "{\"ok\":true}";
                    return;
                }
                if (req.Path == "/api/stop")
                {
                    _stop = true;
                    Log("收到停止指令…", "w");
                    body = "{\"ok\":true}";
                    return;
                }

                // ------------------------------------------------ 刮削
                if (req.Path == "/api/scrape/scan")
                {
                    body = MiniJson.Serialize(Guard(delegate
                    {
                        var series = Scrape.Scan(Core.GetList(data, "sources"), null);
                        var rows = new List<object>();
                        foreach (var s in series) rows.Add(s.ToJson());
                        return new Dictionary<string, object> { { "series", rows.ToArray() } };
                    }));
                    return;
                }
                if (req.Path == "/api/scrape/search")
                {
                    body = MiniJson.Serialize(Guard(delegate
                    {
                        var term = Core.GetStr(data, "term", "");
                        var srcs = Core.GetList(data, "sources");
                        if (srcs.Count == 0) srcs = MetaSources.All.ToList();
                        var list = MetaSources.Search(term, srcs, delegate(string m) { Scrape.Log(m, "w"); });
                        var rows = new List<object>();
                        foreach (var c in list) rows.Add(Scrape.CandidateToJson(c));
                        return new Dictionary<string, object> { { "candidates", rows.ToArray() } };
                    }));
                    return;
                }
                if (req.Path == "/api/scrape/fetch")
                {
                    body = MiniJson.Serialize(Guard(delegate
                    {
                        var src = Core.GetStr(data, "source", "");
                        var id = Core.GetStr(data, "id", "");
                        var term = Core.GetStr(data, "term", "");
                        var srcs = Core.GetList(data, "sources");
                        var log = new Action<string>(delegate(string m) { Scrape.Log(m, "i"); });

                        Metadata meta;
                        if (Core.GetBool(data, "fillMissing", false))
                            meta = MetaSources.FetchMerged(src, id, term, srcs, log);
                        else
                            meta = MetaSources.Fetch(src, id, log);

                        if (meta == null) return new Dictionary<string, object> { { "err", "这个来源没有返回详情" } };
                        return new Dictionary<string, object> { { "meta", Scrape.MetaToJson(meta) } };
                    }));
                    return;
                }

                if (req.Path == "/api/reveal")
                {
                    var path = Core.GetStr(data, "path", "");
                    body = MiniJson.Serialize(new Dictionary<string, object> { { "ok", Reveal(path) } });
                    return;
                }
                if (req.Path == "/api/scrape/preview")
                {
                    body = MiniJson.Serialize(Guard(delegate
                    {
                        var meta = Scrape.MetaFromJson(data.ContainsKey("meta")
                            ? data["meta"] as Dictionary<string, object> : null);
                        var opt = new MetaOptions
                        {
                            Target = Core.GetStr(data, "target", "both"),
                            SplitSeriesVolume = Core.GetBool(data, "splitSuffix", true),
                            SpecialAsNumber = Core.GetBool(data, "specialNumber", false)
                        };
                        int pages = Core.GetInt(data, "pageCount", 0);
                        int total = Core.GetInt(data, "totalIssues", 0);
                        return new Dictionary<string, object>
                        {
                            {"xml", Scrape.PreviewXml(meta, opt, pages)},
                            {"seriesJson", Scrape.PreviewSeriesJson(meta, total)},
                            {"number", meta.Number}, {"volume", meta.Volume}, {"series", meta.Series}
                        };
                    }));
                    return;
                }
                if (req.Path == "/api/scrape/write")
                {
                    if (Scrape.Running) { code = 409; body = "{\"err\":\"刮削任务还在跑\"}"; return; }
                    if (_running) { code = 409; body = "{\"err\":\"转换任务还在跑\"}"; return; }

                    body = MiniJson.Serialize(Guard(delegate
                    {
                        var opt = new MetaWriter.Options
                        {
                            Backup = Core.GetBool(data, "backup", true),
                            WriteSeriesJson = Core.GetBool(data, "seriesJson", true),
                            WriteCover = Core.GetBool(data, "cover", true),
                            OverwriteCover = Core.GetBool(data, "coverOverwrite", false),
                            CoverPick = Core.GetStr(data, "coverPick", "auto"),
                            CoverMaxWidth = Core.GetInt(data, "coverWidth", 800),
                            KeepOriginal = Core.GetBool(data, "keepRaw", true),
                            Meta = new MetaOptions
                            {
                                Target = Core.GetStr(data, "target", "both"),
                                SplitSeriesVolume = Core.GetBool(data, "splitSuffix", true),
                                SpecialAsNumber = Core.GetBool(data, "specialNumber", false)
                            }
                        };

                        var jobs = new List<ScrapeJob>();
                        var arr = data.ContainsKey("jobs") ? data["jobs"] as object[] : null;
                        if (arr != null)
                        {
                            foreach (var o in arr)
                            {
                                var d = o as Dictionary<string, object>;
                                if (d == null) continue;
                                var job = new ScrapeJob
                                {
                                    Dir = Core.GetStr(d, "dir", ""),
                                    Series = Core.GetStr(d, "series", ""),
                                    TotalIssues = Core.GetInt(d, "totalIssues", 0),
                                    Meta = Scrape.MetaFromJson(d.ContainsKey("meta")
                                        ? d["meta"] as Dictionary<string, object> : null)
                                };
                                var files = d.ContainsKey("files") ? d["files"] as object[] : null;
                                if (files != null)
                                {
                                    foreach (var fo in files)
                                    {
                                        var fd = fo as Dictionary<string, object>;
                                        if (fd == null) continue;
                                        var path = Core.GetStr(fd, "path", "");
                                        if (path.Length == 0) continue;
                                        // 每本自己的号、标题、特殊类型、ISBN：同一系列里番外和正传能分别标
                                        var pair = Core.GetStr(fd, "volume", "") + "|"
                                                 + Core.GetStr(fd, "number", "") + "|"
                                                 + Core.GetStr(fd, "title", "") + "|"
                                                 + Core.GetStr(fd, "format", "") + "|"
                                                 + Core.GetStr(fd, "gtin", "");
                                        job.Files.Add(new KeyValuePair<string, string>(path, pair));
                                    }
                                }
                                if (job.Files.Count > 0) jobs.Add(job);
                            }
                        }

                        if (jobs.Count == 0) return new Dictionary<string, object> { { "err", "没有可写入的系列" } };
                        int threads = Math.Max(1, Math.Min(8, Core.GetInt(data, "threads", 3)));
                        Scrape.StartWrite(jobs, opt, threads);
                        return new Dictionary<string, object> { { "ok", true }, { "count", jobs.Count } };
                    }));
                    return;
                }
                if (req.Path == "/api/scrape/original")
                {
                    body = MiniJson.Serialize(Guard(delegate
                    {
                        var dir = Core.GetStr(data, "dir", "");
                        string from;
                        List<Dictionary<string, object>> perFile;
                        var m = Scrape.ReadOriginal(dir, out from, out perFile);
                        if (m == null)
                            return new Dictionary<string, object> { { "err", "这个系列没有可还原的留档（写入时没开「留档原 XML」）" } };
                        return new Dictionary<string, object>
                        {
                            {"meta", Scrape.MetaToJson(m)},
                            {"file", from},
                            {"files", perFile.ToArray()}
                        };
                    }));
                    return;
                }

                if (req.Path == "/api/scrape/rename" || req.Path == "/api/scrape/rename/preview")
                {
                    var preview = req.Path.EndsWith("/preview");
                    body = MiniJson.Serialize(Guard(delegate
                    {
                        var template = Core.GetStr(data, "template", Renamer.DefaultTemplate);
                        var all = new List<Renamer.Item>();

                        var arr = data.ContainsKey("jobs") ? data["jobs"] as object[] : null;
                        if (arr != null)
                        {
                            foreach (var o in arr)
                            {
                                var d = o as Dictionary<string, object>;
                                if (d == null) continue;
                                var dir = Core.GetStr(d, "dir", "");
                                var series = Core.GetStr(d, "series", "");
                                var year = Core.GetStr(d, "year", "");

                                var pairs = new List<KeyValuePair<string, string>>();
                                var files = d.ContainsKey("files") ? d["files"] as object[] : null;
                                if (files != null)
                                {
                                    foreach (var fo in files)
                                    {
                                        var fd = fo as Dictionary<string, object>;
                                        if (fd == null) continue;
                                        var path = Core.GetStr(fd, "path", "");
                                        if (path.Length == 0) continue;
                                        // 顺序：册号 | 卷 | 话 | 单册标题
                                        pairs.Add(new KeyValuePair<string, string>(path,
                                            Core.GetStr(fd, "label", "") + "|" +
                                            Core.GetStr(fd, "volume", "") + "|" +
                                            Core.GetStr(fd, "number", "") + "|" +
                                            Core.GetStr(fd, "title", "")));
                                    }
                                }
                                all.AddRange(Renamer.Plan(template, dir, series, year, pairs));
                            }
                        }

                        if (all.Count == 0)
                            return new Dictionary<string, object> { { "err", "没有可重命名的文件" } };

                        if (preview)
                        {
                            var rows = new List<object>();
                            foreach (var it in all)
                                rows.Add(new Dictionary<string, object>
                                {
                                    {"src", it.Src}, {"dst", it.Dst},
                                    {"same", it.Same},
                                    {"exists", !it.Same && File.Exists(it.Dst)}
                                });
                            return new Dictionary<string, object> { { "rows", rows.ToArray() } };
                        }

                        var r = Renamer.Apply(all, delegate(string msg, string lv) { Scrape.Log(msg, lv); });
                        r["err"] = "";
                        return r;
                    }));
                    return;
                }

                if (req.Path == "/api/scrape/stop")
                {
                    Scrape.Stop();
                    body = "{\"ok\":true}";
                    return;
                }
            }

            code = 404;
            body = "{\"err\":\"not found\"}";
        }

        /// <summary>
        /// 接口兜底：任何异常都变成 {"err": "..."} 交给页面显示。
        /// 不兜的话请求会静默断开 —— 页面上只剩一个转不动的「查询中…」。
        /// </summary>
        static Dictionary<string, object> Guard(Func<Dictionary<string, object>> f)
        {
            try { return f(); }
            catch (Exception ex)
            {
                Log("接口出错：" + ex.Message, "e");
                return new Dictionary<string, object> { { "err", ex.Message } };
            }
        }

        /// <summary>写系统剪贴板。剪贴板需要 STA 线程，这里单开一个再等它结束。</summary>
        public static bool CopyToClipboard(string text)
        {
            return CopyText(text);
        }

        static bool CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Length > 4096) text = text.Substring(0, 4096);

            bool ok = false;
            var th = new Thread(delegate()
            {
                for (int i = 0; i < 4 && !ok; i++)
                {
                    try { System.Windows.Forms.Clipboard.SetText(text); ok = true; }
                    catch { Thread.Sleep(40); }   // 剪贴板被别的程序占用时重试
                }
            });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
            th.Join(2000);
            return ok;
        }

        /// <summary>
        /// 选目录。这件事**必须回到 WPF 的 UI 线程**做，而且要给 Owner：
        /// 请求是在后台线程上处理的，WinForms 的文件夹框要求 STA，从后台线程直接 ShowDialog 会抛异常；
        /// 就算侥幸弹出来，没有 Owner 也会掉到主窗口后面去 —— 用户看到的就是「点浏览没反应」。
        /// </summary>
        static string PickFolder()
        {
            var app = System.Windows.Application.Current;
            if (app == null) return "ERR:程序界面还没准备好";

            string result = "";
            try
            {
                app.Dispatcher.Invoke(new Action(delegate
                {
                    try
                    {
                        using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                        {
                            dlg.Description = "选择目录";
                            dlg.ShowNewFolderButton = true;

                            // 根节点定在「此电脑」：默认的桌面/文档树里大半是用不上的，
                            // 漫画库多半在别的盘，多一层层点进去很烦。
                            try { dlg.RootFolder = Environment.SpecialFolder.MyComputer; } catch { }

                            // 停在上次选过的目录；目录不在了就当没记忆
                            var cfg = LoadConfig();
                            var last = cfg.ContainsKey("last_dir") ? Convert.ToString(cfg["last_dir"]) : "";
                            if (!string.IsNullOrEmpty(last) && Directory.Exists(last)) dlg.SelectedPath = last;

                            var owner = app.MainWindow == null ? null : new OwnerHandle(app.MainWindow);
                            var r = owner == null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
                            if (r == System.Windows.Forms.DialogResult.OK)
                            {
                                result = dlg.SelectedPath;
                                cfg["last_dir"] = result;
                                SaveConfig(cfg);
                            }
                            else result = "";
                        }
                    }
                    catch (Exception ex) { result = "ERR:" + ex.Message; }
                }));
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
            return result;
        }

        /// <summary>把 WPF 窗口包成 WinForms 认的 Owner，保证对话框压在主窗口上、模态跟着主窗口。</summary>
        sealed class OwnerHandle : System.Windows.Forms.IWin32Window
        {
            readonly IntPtr _h;
            public OwnerHandle(System.Windows.Window w)
            {
                _h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            }
            public IntPtr Handle { get { return _h; } }
        }

        /// <summary>在资源管理器里定位一个文件/目录（选中漫画路径用）。</summary>
        public static bool Reveal(string path)
        {
            try
            {
                if (File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
                else return false;
                return true;
            }
            catch (Exception ex)
            {
                App.Dump("打开路径失败 " + path + "：" + ex.Message);
                return false;
            }
        }
    }
}
