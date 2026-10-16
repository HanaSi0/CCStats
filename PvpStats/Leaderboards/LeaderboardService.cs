using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PvpStats.Leaderboards;

public sealed record RankingRunState(bool Busy, string Message, RankingSnapshot? Snapshot, int PagesFetched = 0);

/// <summary>Daily date checks and atomic regional snapshots. No polling timer or profile crawling.</summary>
public sealed class LeaderboardService : IDisposable {
    private readonly HttpClient _http;
    private readonly string _directory;
    private readonly TimeSpan _delay;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _load;
    private CancellationTokenSource? _operation;
    private int _busy;
    private int _disposed;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public volatile RankingRunState State = new(false, "尚未抓取官方榜单。", null);

    public LeaderboardService(string directory, HttpMessageHandler? handler = null, TimeSpan? delay = null) {
        _directory = directory;
        _delay = delay ?? TimeSpan.FromSeconds(1);
        _http = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CCStats/2.7 (+public-Lodestone-ranking-reader)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        _load = LoadAsync();
    }
    public Task Ready => _load;
    public bool CanCancel => _operation != null;
    private async Task LoadAsync() {
        try {
            var path = Path.Combine(_directory, "latest.json");
            if(!File.Exists(path)) return;
            var snapshot = JsonSerializer.Deserialize<RankingSnapshot>(await File.ReadAllTextAsync(path, _lifetime.Token), Json);
            if(snapshot == null || snapshot.Pages.Length == 0 || !Uri.TryCreate(snapshot.RootUrl, UriKind.Absolute, out var uri) || !LodestoneParser.IsOfficial(uri))
                throw new FormatException("缓存格式不完整。");
            State = new(false, "已加载本地快照；点击抓取以更新。", snapshot);
        } catch(OperationCanceledException) { }
        catch(Exception e) { State = new(false, "缓存无法加载，可重新抓取：" + e.Message, null); }
    }
    public void Cancel() { try { _operation?.Cancel(); } catch(ObjectDisposedException) { } }
    public Task CheckDateAsync(RankingRegion region) => FetchAsync(region.Site, "", true, region, true);
    public async Task FetchAsync(int site, string scope, bool allScopes, RankingRegion? region = null, bool checkDate = false) {
        if(Volatile.Read(ref _disposed) != 0 || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        try {
            await _load;
            if(DateTimeOffset.UtcNow - _lastAttempt < TimeSpan.FromMinutes(1)) {
                State = State with { Message = "请间隔至少 60 秒再发起抓取。" }; return;
            }
            _lastAttempt = DateTimeOffset.UtcNow;
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _operation = operation;
            var token = operation.Token;
            var previous = State.Snapshot;
            State = new(true, "正在连接官方榜单…", previous);
            var root = allScopes ? LodestoneParser.Root(site) : LodestoneParser.ScopeUrl(LodestoneParser.Root(site), scope);
            var started = DateTimeOffset.UtcNow;
            RankingPage? seed = null;
            var regionScopes = new HashSet<string>(StringComparer.Ordinal);
            if(region != null) {
                var index = await ReadPageAsync(root, token);
                seed = LodestoneParser.Parse(index.Html, index.Url, DateTimeOffset.UtcNow);
                root = index.Url;
                foreach(var candidate in seed.Scopes.Where(region.Includes)) regionScopes.Add(candidate);
                if(!region.Includes(seed.Scope)) {
                    var target = regionScopes.Order(StringComparer.Ordinal).FirstOrDefault();
                    if(target == null) throw new FormatException($"官网未发现{region.Name}的榜单入口；继续显示该地区缓存。");
                    var selected = await ReadPageAsync(LodestoneParser.ScopeUrl(root, target), token);
                    seed = LodestoneParser.Parse(selected.Html, selected.Url, DateTimeOffset.UtcNow);
                    root = selected.Url;
                }
                if(!region.Includes(seed.Scope)) throw new FormatException("官网返回了其他地区榜单，已保留原缓存。");
                // The current site publishes one physical-region board, even if its query uses a representative logical DC.
                regionScopes.Clear(); regionScopes.Add(seed.Scope);
            }
            var officialDate = seed?.OfficialDate;
            if(checkDate) {
                if(officialDate == null) throw new FormatException("未识别到官网榜单更新日期，无法按天比较；保留原缓存。");
                if(previous?.OfficialDate == officialDate && previous.RegionKey == region?.Key && previous.AllScopes && previous.SchemaVersion == RankingSnapshot.CurrentSchemaVersion) {
                    State = new(false, $"{region!.Name}官网日期 {officialDate:yyyy-MM-dd} 与缓存一致，直接使用缓存。", previous);
                    return;
                }
                State = new(true, $"{region!.Name}官网榜单日期为 {officialDate:yyyy-MM-dd}，正在更新完整榜单…", previous);
            }
            var pages = new List<RankingPage>();
            var pending = new Queue<Uri>(); pending.Enqueue(root);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var discoveredScopes = new HashSet<string>(regionScopes, StringComparer.Ordinal);
            foreach(var candidate in regionScopes) pending.Enqueue(LodestoneParser.ScopeUrl(root, candidate));
            var fingerprints = new Dictionary<string, string>();
            while(pending.Count > 0) {
                token.ThrowIfCancellationRequested();
                var url = pending.Dequeue();
                if(!visited.Add(url.AbsoluteUri)) continue;
                if(pages.Count >= 250) throw new FormatException("已达到 250 页保护上限；本次未完成，不会覆盖旧快照。");
                if(pages.Count > 0) await Task.Delay(_delay, token);
                State = new(true, $"正在读取第 {pages.Count + 1} 页 · {LodestoneParser.Query(url).GetValueOrDefault("dcgroup", "官方默认")}", previous, pages.Count);
                RankingPage page;
                if(seed != null) { page = seed; seed = null; url = new Uri(page.SourceUrl); }
                else {
                    var responsePage = await ReadPageAsync(url, token);
                    url = responsePage.Url;
                    page = LodestoneParser.Parse(responsePage.Html, url, DateTimeOffset.UtcNow);
                }
                visited.Add(url.AbsoluteUri);
                if(pages.Count == 0) { root = url; officialDate ??= page.OfficialDate; }
                if(region != null && !region.Includes(page.Scope)) throw new FormatException("分页返回其他地区的榜单，已停止更新。");
                if(officialDate.HasValue && page.OfficialDate.HasValue && officialDate != page.OfficialDate)
                    throw new FormatException("抓取期间官网榜单日期发生变化，保留旧缓存，下次启动时重试。");
                // A site ignoring page=N must not be reported as a complete multipage ranking.
                var pageScope = page.Scope;
                var crawlSource = url;
                if(!LodestoneParser.Query(url).ContainsKey("dcgroup") && pageScope != "官方默认") {
                    crawlSource = LodestoneParser.ScopeUrl(url, pageScope);
                    visited.Add(crawlSource.AbsoluteUri);
                }
                var signature = pageScope + ":" + string.Join(",", page.Entries.Select(e => e.CharacterId));
                if(page.Entries.Length > 0 && fingerprints.TryGetValue(signature, out var other) && other != url.AbsoluteUri)
                    throw new FormatException("不同分页返回了相同角色列表，无法确认榜单是否完整；保留旧快照。");
                fingerprints[signature] = url.AbsoluteUri;
                pages.Add(page);
                foreach(var next in page.NextPages) {
                    var link = LodestoneParser.CrawlLink(crawlSource, next, false);
                    if(link != null && !visited.Contains(link.AbsoluteUri)) pending.Enqueue(link);
                }
                foreach(var discovered in page.Scopes) {
                    if(region != null && !regionScopes.Contains(discovered)) continue;
                    if(!discoveredScopes.Add(discovered) || !allScopes) continue;
                    var next = LodestoneParser.ScopeUrl(root, discovered);
                    if(!visited.Contains(next.AbsoluteUri)) pending.Enqueue(next);
                }
            }
            if(region != null && regionScopes.Count == 0 && pages.Count > 0) discoveredScopes.Add(pages[0].Scope);
            if(allScopes && discoveredScopes.Count == 0)
                throw new FormatException("未发现数据中心切换入口，无法确认已抓取全部当前榜单；请先抓取默认榜单或核对页面格式。");
            if(checkDate && pages.Any(p => p.IsFragment)) {
                // Scroll fragments omit their date: confirm the dated index stayed unchanged before committing.
                await Task.Delay(_delay, token);
                var finalIndex = await ReadPageAsync(root, token);
                var finalPage = LodestoneParser.Parse(finalIndex.Html, finalIndex.Url, DateTimeOffset.UtcNow);
                if(finalPage.OfficialDate != officialDate || finalPage.Season != pages[0].Season)
                    throw new FormatException("抓取期间官网榜单日期或赛季发生变化，保留旧缓存。");
            }
            var snapshot = new RankingSnapshot {
                RootUrl = root.AbsoluteUri, StartedAt = started, CompletedAt = DateTimeOffset.UtcNow, AllScopes = allScopes,
                OfficialDate = officialDate, RegionKey = region?.Key ?? "", SchemaVersion = RankingSnapshot.CurrentSchemaVersion,
                Pages = pages.ToArray(), Scopes = discoveredScopes.Order().ToArray(),
                Entries = pages.SelectMany(p => p.Entries).DistinctBy(e => region == null ? e.Scope + ":" + e.CharacterId : e.CharacterId).ToArray(),
            };
            Directory.CreateDirectory(_directory);
            var temporary = Path.Combine(_directory, $"latest-{Guid.NewGuid():N}.json.tmp");
            try {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, Json), Encoding.UTF8, token);
                File.Move(temporary, Path.Combine(_directory, "latest.json"), true);
            } finally { if(File.Exists(temporary)) File.Delete(temporary); }
            State = new(false, $"已更新{region?.Name}榜单：{snapshot.OfficialDate:yyyy-MM-dd} · {snapshot.Entries.Length} 名玩家，{pages.Count} 页。", snapshot, pages.Count);
        } catch(OperationCanceledException) when(_operation?.IsCancellationRequested == true || _lifetime.IsCancellationRequested) {
            State = State with { Busy = false, Message = "已取消；保留上一次完整快照。" };
        } catch(OperationCanceledException) {
            State = State with { Busy = false, Message = "请求超时；保留上一次完整快照，请稍后重试。" };
        } catch(Exception e) {
            State = State with { Busy = false, Message = "抓取失败，保留原快照：" + e.Message };
        } finally { _operation = null; Interlocked.Exchange(ref _busy, 0); }
    }
    private async Task<(string Html, Uri Url)> ReadPageAsync(Uri initial, CancellationToken token) {
        var url = initial;
        for(var redirects = 0; redirects <= 5; redirects++) {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if((int)response.StatusCode is >= 300 and < 400) {
                var location = response.Headers.Location;
                if(redirects == 5 || location == null || !Uri.TryCreate(url, location, out var next) || !LodestoneParser.IsOfficial(next)
                    || next.Host != initial.Host || next.AbsolutePath != LodestoneParser.RankingPath)
                    throw new HttpRequestException("官方重定向超出当前榜单范围或次数上限。", null, response.StatusCode);
                url = next; continue;
            }
            if(!response.IsSuccessStatusCode) throw new HttpRequestException($"官方返回 HTTP {(int)response.StatusCode}；请稍后再试。", null, response.StatusCode);
            if(response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml")) throw new FormatException("官方返回的内容不是 HTML 榜单。");
            if(response.Content.Headers.ContentLength > 4 * 1024 * 1024) throw new FormatException("单页超过 4 MB，已停止读取。");
            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int read;
            while((read = await stream.ReadAsync(chunk, token)) > 0) {
                if(buffer.Length + read > 4 * 1024 * 1024) throw new FormatException("单页超过 4 MB，已停止读取。");
                await buffer.WriteAsync(chunk.AsMemory(0, read), token);
            }
            return (Encoding.UTF8.GetString(buffer.ToArray()), url);
        }
        throw new HttpRequestException("官方页面重定向次数过多。");
    }
    public async Task ExportAsync() {
        if(Volatile.Read(ref _disposed) != 0 || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        string? temporary = null;
        try {
            await _load;
            var snapshot = State.Snapshot;
            if(snapshot == null) return;
            State = State with { Busy = true, Message = "正在导出 CSV、JSON 和原始网页…" };
            var directory = Path.Combine(_directory, "exports"); Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, $"CC-ranking-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.zip");
            temporary = destination + ".tmp";
            await Task.Run(() => {
                using var archive = ZipFile.Open(temporary, ZipArchiveMode.Create);
                void Write(string name, string content) {
                    using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(true)); writer.Write(content);
                }
                Write("ranking.csv", LodestoneParser.Csv(snapshot.Entries));
                Write("ranking.json", JsonSerializer.Serialize(snapshot, Json));
                for(var i = 0; i < snapshot.Pages.Length; i++) Write($"pages/{i + 1:D3}.html", snapshot.Pages[i].Html);
                Write("README.txt", "来源与抓取时间见 ranking.json。CSV 包含识别的显示字段；完整字段、图片链接和原始 HTML 见 JSON 与 pages。官方未公开的字段不会补造。\n");
            });
            File.Move(temporary, destination);
            State = State with { Busy = false, Message = "已导出：" + destination };
        } catch(Exception e) { State = State with { Busy = false, Message = "导出失败：" + e.Message }; }
        finally { if(temporary != null && File.Exists(temporary)) File.Delete(temporary); Interlocked.Exchange(ref _busy, 0); }
    }
    public void Dispose() {
        if(Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel(); _http.Dispose();
        // Keep the cancellation source alive until an in-flight request has observed cancellation.
    }
}
