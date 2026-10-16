using System.IO.Compression;
using System.Net;
using System.Net.Http;
using PvpStats.Leaderboards;

// Synthetic fixtures: these tests do not assert the current live Lodestone markup is compatible.
static class LeaderboardChecks {
    static string Fixture(int id, string scope, bool extraPage = false) => $$"""
        <html><head><title>Crystalline Conflict Standings</title></head><body>
        <h1>Crystalline Conflict · Test Season</h1><time>2026-10-16</time>
        <select name="dcgroup"><option value="Aether" {{(scope == "Aether" ? "selected" : "")}}>Aether</option><option value="Chaos" {{(scope == "Chaos" ? "selected" : "")}}>Chaos</option></select>
        <ol class="ranking"><li class="ranking__item">
        <div class="ranking__order">{{id}}</div><span class="ranking__change">+2</span>
        <a href="/lodestone/character/{{id}}/"><img src="https://img.finalfantasyxiv.com/test.png" alt="Portrait"><p class="ranking__character__name">测试角色 &amp; Alice {{id}}</p><p class="ranking__character__world">Cactuar [Aether]</p></a>
        <p class="ranking__tier">Crystal</p><p class="ranking__rating">1,234</p><p class="ranking__victories">42</p>
        <p class="ranking__future_metric" data-score="7">unknown metric</p>
        </li></ol>
        {{(extraPage ? "<a href='?dcgroup=Aether&amp;page=2'>Next</a>" : "")}}
        <a href="?dcgroup=Aether&amp;season=old&amp;page=2">Old season</a>
        <a href="https://example.com/lodestone/ranking/crystallineconflict/?page=3">External</a>
        </body></html>
        """;
    public static async Task Run(Action<bool, string> check) {
        var root = LodestoneParser.Root(0);
        var page = LodestoneParser.Parse(Fixture(1, "Aether", true), root, DateTimeOffset.UtcNow);
        check(page.Entries.Length == 1 && page.Scope == "Aether", "ranking reads selected scope and one character row");
        var row = page.Entries[0];
        check(row.CharacterName == "测试角色 & Alice 1" && row.CharacterId == "1", "ranking decodes Unicode names and stable character IDs");
        check(row.Rank == "1" && row.Rating == "1,234" && row.Victories == "42" && row.Tier == "Crystal", "ranking extracts explicit public fields");
        check(row.World == "Cactuar [Aether]" && row.RankChange == "+2", "ranking preserves original server, data center and movement text");
        check(row.PortraitUrl.EndsWith("test.png") && row.ImageUrls.Length == 1, "ranking retains portrait and all image links");
        check(row.RawFields["ranking__future_metric"].Single() == "unknown metric" && row.RawFields["ranking__future_metric@data-score"].Single() == "7", "ranking keeps unknown future fields and attributes");
        check(page.Html == Fixture(1, "Aether", true) && page.Headings.Length == 1 && page.TimeLabels.Single() == "2026-10-16", "ranking retains raw HTML and official page metadata");
        check(page.NextPages.Any(x => x.Contains("page=2")) && page.NextPages.All(x => !x.Contains("season=")), "ranking follows current pagination and excludes historical seasons");
        check(page.Scopes.Contains("Aether") && page.Scopes.Contains("Chaos"), "ranking discovers scopes from official controls");
        check(LodestoneParser.CrawlLink(root, "https://example.com/lodestone/ranking/crystallineconflict/?page=2", true) == null, "ranking rejects external pagination");
        check(LodestoneParser.CrawlLink(root, "https://na.finalfantasyxiv.com.evil.test/lodestone/ranking/crystallineconflict/", true) == null, "ranking rejects lookalike hosts");
        check(LodestoneParser.CrawlLink(root, "http://na.finalfantasyxiv.com/lodestone/ranking/crystallineconflict/", true) == null, "ranking rejects insecure source URLs");
        check(LodestoneParser.CrawlLink(root, "?page=999999", false) == null && LodestoneParser.CrawlLink(root, "?season=10&page=2", false) == null, "ranking bounds pagination and does not cross season queries");
        check(LodestoneParser.CrawlLink(LodestoneParser.ScopeUrl(root, "Aether"), "?dcgroup=Chaos&page=2", false) == null, "ranking does not mix scopes when fetching a selected board");
        check(LodestoneParser.CrawlLink(root, "?page=1", false)!.AbsoluteUri == root.AbsoluteUri, "ranking canonicalizes the first page");
        bool maintenanceRejected = false, changedRejected = false;
        try { LodestoneParser.Parse("<html><h1>Maintenance</h1></html>", root, DateTimeOffset.UtcNow); } catch(FormatException) { maintenanceRejected = true; }
        try { LodestoneParser.Parse("<div class='ranking'>Changed layout</div>", root, DateTimeOffset.UtcNow); } catch(FormatException) { changedRejected = true; }
        check(maintenanceRejected && changedRejected, "maintenance and unrecognized layout never become successful empty rankings");
        var missing = LodestoneParser.Parse(Fixture(1, "Aether").Replace("<p class=\"ranking__rating\">1,234</p>", ""), root, DateTimeOffset.UtcNow);
        check(missing.Entries[0].Rating == "", "ranking never invents absent public fields");
        var csv = LodestoneParser.Csv([row with { CharacterName = "=HYPERLINK(\"test\")", RowText = "A, B\nC" }]);
        check(csv.Contains("'=" ) && csv.Contains("\"A, B\nC\""), "CSV escapes values and prevents spreadsheet formulas");
        var dir = Path.Combine(Path.GetTempPath(), "ccstats-ranking-test-" + Guid.NewGuid().ToString("N"));
        try {
            var handler = new FakeHandler((url, token) => {
                var q = LodestoneParser.Query(url);
                var scope = q.GetValueOrDefault("dcgroup", "Aether");
                var id = scope == "Chaos" ? 3 : q.ContainsKey("page") ? 2 : 1;
                return Task.FromResult(Html(Fixture(id, scope, id == 1)));
            });
            using(var service = new LeaderboardService(dir, handler, TimeSpan.Zero)) {
                await service.FetchAsync(0, "", true);
                var snapshot = service.State.Snapshot;
                check(snapshot != null && snapshot.Pages.Length == 3 && snapshot.Entries.Length == 3, "crawl reads all discovered scopes and pagination without duplicate default board");
                check(!service.State.Busy && File.Exists(Path.Combine(dir, "latest.json")), "completed ranking is saved atomically");
                await service.FetchAsync(0, "", true);
                check(handler.Requests == 3 && service.State.Message.Contains("60"), "repeated clicks respect refresh cooldown");
                await service.ExportAsync();
                var archivePath = Directory.GetFiles(Path.Combine(dir, "exports"), "*.zip").Single();
                using var archive = ZipFile.OpenRead(archivePath);
                check(archive.GetEntry("ranking.csv") != null && archive.GetEntry("ranking.json") != null && archive.Entries.Count(e => e.FullName.EndsWith(".html")) == 3, "export contains CSV, JSON and every raw source page");
            }
            using(var redirect = new LeaderboardService(Path.Combine(dir, "redirect"), new FakeHandler((url, _) => {
                if(url.Query.Length == 0) {
                    var response = new HttpResponseMessage(HttpStatusCode.Found);
                    response.Headers.Location = new Uri("?dcgroup=Aether", UriKind.Relative);
                    return Task.FromResult(response);
                }
                return Task.FromResult(Html(Fixture(1, "Aether")));
            }), TimeSpan.Zero)) {
                await redirect.FetchAsync(0, "", false);
                check(redirect.State.Snapshot?.RootUrl.Contains("dcgroup=Aether") == true, "official current-board redirect preserves canonical source");
            }
            using(var externalRedirect = new LeaderboardService(dir, new FakeHandler((_, _) => {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://example.com/redirect");
                return Task.FromResult(response);
            }), TimeSpan.Zero)) {
                await externalRedirect.Ready; var previous = externalRedirect.State.Snapshot;
                await externalRedirect.FetchAsync(0, "", false);
                check(ReferenceEquals(previous, externalRedirect.State.Snapshot) && externalRedirect.State.Message.Contains("重定向"), "redirects cannot send ranking fetches to external hosts");
            }
            using(var failed = new LeaderboardService(dir, new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))), TimeSpan.Zero)) {
                await failed.Ready; var previous = failed.State.Snapshot;
                await failed.FetchAsync(0, "", false);
                check(previous != null && ReferenceEquals(previous, failed.State.Snapshot) && failed.State.Message.Contains("403"), "HTTP failure preserves loaded snapshot and shows status");
            }
            using(var timeout = new LeaderboardService(dir, new FakeHandler((_, _) => throw new TaskCanceledException("HTTP timeout")), TimeSpan.Zero)) {
                await timeout.Ready; var previous = timeout.State.Snapshot;
                await timeout.FetchAsync(0, "", false);
                check(ReferenceEquals(previous, timeout.State.Snapshot) && timeout.State.Message.Contains("超时"), "network timeout is distinct from user cancellation and retains cache");
            }
            using(var partial = new LeaderboardService(dir, new FakeHandler((url, _) => Task.FromResult(url.Query.Contains("page=2") ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Html(Fixture(1, "Aether", true)))), TimeSpan.Zero)) {
                await partial.Ready; var previous = partial.State.Snapshot;
                await partial.FetchAsync(0, "", false);
                check(ReferenceEquals(previous, partial.State.Snapshot) && partial.State.Message.Contains("500"), "failed later pages do not replace a complete cached ranking");
            }
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using(var canceled = new LeaderboardService(dir, new FakeHandler(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Html(""); }), TimeSpan.Zero)) {
                await canceled.Ready; var previous = canceled.State.Snapshot;
                var request = canceled.FetchAsync(0, "", false);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); canceled.Cancel();
                await request.WaitAsync(TimeSpan.FromSeconds(5));
                check(!canceled.State.Busy && ReferenceEquals(previous, canceled.State.Snapshot) && canceled.State.Message.Contains("取消"), "cancel interrupts network work and retains the old snapshot");
            }
            using(var duplicate = new LeaderboardService(dir, new FakeHandler((_, _) => Task.FromResult(Html(Fixture(1, "Aether", true)))), TimeSpan.Zero)) {
                await duplicate.Ready; var previous = duplicate.State.Snapshot;
                await duplicate.FetchAsync(0, "", false);
                check(ReferenceEquals(previous, duplicate.State.Snapshot) && duplicate.State.Message.Contains("相同角色"), "ignored pagination cannot silently be reported as a complete scrape");
            }
        } finally { if(Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") };
    sealed class FakeHandler(Func<Uri, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Requests++; return respond(request.RequestUri!, token); }
    }
}
