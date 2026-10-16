using System.Net;
using System.Net.Http;
using PvpStats.Leaderboards;

// Offline fixtures. Live official date markup and selectors still require the configured network access.
static class DailyRankingChecks {
    static readonly string[] Scopes = ["Elemental", "Mana", "Aether", "Primal", "Chaos", "Materia"];
    static string Fixture(Uri url, string day, int offset = 0, bool omitDate = false) {
        var query = LodestoneParser.Query(url);
        var scope = query.GetValueOrDefault("dcgroup", url.Host.StartsWith("jp.") ? "Elemental" : url.Host.StartsWith("eu.") ? "Chaos" : "Aether");
        var index = Array.IndexOf(Scopes, scope);
        var number = query.ContainsKey("page") ? 2 : 1;
        var id = offset + (index + 1) * 100 + number;
        return $"""
            <html><title>Current standings</title><h2>Season 2026/01/01 - 2026/12/31</h2>
            {(omitDate ? "" : $"<div class='ranking__update'><time datetime='{day}'>Updated {day}</time></div>")}
            <select name="dcgroup">{string.Join("", Scopes.Select(s => $"<option value='{s}' {(s == scope ? "selected" : "")}>{s}</option>"))}</select>
            <ol class="ranking"><li class="ranking__item"><span class="ranking__order">{number}</span>
            <a href="/lodestone/character/{id}/"><p class="ranking__character__name">First Middle Last {id}</p><p class="ranking__character__world">World [{scope}]</p></a>
            <span class="ranking__rating">1234</span></li></ol>
            {(number == 1 ? $"<a href='?dcgroup={scope}&amp;page=2'>Next page</a>" : "")}</html>
            """;
    }
    public static async Task Run(Action<bool, string> check) {
        var jp = RankingRegion.All[0]; var na = RankingRegion.All[1]; var eu = RankingRegion.All[2]; var oc = RankingRegion.All[3];
        check(RankingRegion.All.Select(r => r.Key).Distinct().Count() == 4 && jp.Name == "日服", "region menu has separate Japanese, North American, European and Oceanian caches");
        check(jp.Includes("Japan") && jp.Includes("Mana") && !jp.Includes("Aether") && na.Includes("North America") && oc.Includes("Materia"), "region aliases map official scopes without mixing language and region");
        check(OfficialRankingDate.ParseLabel("2026-10-16T00:01:00+14:00", LodestoneParser.Hosts[0]) == new DateOnly(2026,10,16), "date comparison keeps the official calendar day without converting time zones");
        check(OfficialRankingDate.ParseLabel("更新日 2026年10月16日 15:00", LodestoneParser.Hosts[2]) == new DateOnly(2026,10,16), "ranking accepts Japanese date labels");
        check(OfficialRankingDate.ParseLabel("Updated 10/11/2026", LodestoneParser.Hosts[0]) == new DateOnly(2026,10,11)
            && OfficialRankingDate.ParseLabel("Updated 10/11/2026", LodestoneParser.Hosts[1]) == new DateOnly(2026,11,10), "day parsing respects North American and European numeric formats");
        check(OfficialRankingDate.ParseLabel("2026/01/01 - 2026/12/31", LodestoneParser.Hosts[0]) == null
            && OfficialRankingDate.ParseLabel("2026/02/30", LodestoneParser.Hosts[0]) == null, "season ranges and impossible dates cannot mark a cache fresh");
        var dateDocument = new HtmlAgilityPack.HtmlDocument();
        dateDocument.LoadHtml("<div class='ranking__update'>Updated <time datetime='2026-10-17T01:00:00Z'>10/16/2026 18:00 PDT</time></div>");
        check(OfficialRankingDate.Read(dateDocument.DocumentNode, LodestoneParser.Root(0)) == new DateOnly(2026,10,16), "visible official date takes precedence over an ISO attribute in another time zone");
        dateDocument.LoadHtml("<div class='ranking__update'>Updated <time datetime='2026-10-16T06:00:00Z'></time></div>");
        check(OfficialRankingDate.Read(dateDocument.DocumentNode, LodestoneParser.Root(0)) == new DateOnly(2026,10,16), "date checks read nested machine-readable date when display text has no date");
        var dir = Path.Combine(Path.GetTempPath(), "ccstats-daily-test-" + Guid.NewGuid().ToString("N"));
        try {
            var firstHandler = new Handler(url => Fixture(url, "2026-10-16T06:00:00Z"));
            using(var first = new LeaderboardService(Path.Combine(dir, jp.Key), firstHandler, TimeSpan.Zero)) {
                await first.CheckDateAsync(jp);
                var snapshot = first.State.Snapshot;
                check(snapshot?.OfficialDate == new DateOnly(2026,10,16) && snapshot.RegionKey == jp.Key, "first launch saves the official day and region identity");
                check(snapshot?.Entries.Length == 2 && snapshot.Entries.All(e => jp.Includes(e.Scope)), "first launch fetches one physical-region board and all its pages without scanning logical servers");
                check(snapshot!.Entries.All(e => e.CharacterName.StartsWith("First Middle Last ")), "cached player names retain all words without abbreviation");
                check(firstHandler.Requests == 2, "first date-check response is reused rather than downloaded again for the crawl");
            }
            var path = Path.Combine(dir, jp.Key, "latest.json"); var originalBytes = await File.ReadAllBytesAsync(path);
            using(var same = new LeaderboardService(Path.Combine(dir, jp.Key), new Handler(url => Fixture(url, "2026-10-16T23:59:00-07:00")), TimeSpan.Zero)) {
                await same.Ready; var before = same.State.Snapshot;
                await same.CheckDateAsync(jp);
                check(ReferenceEquals(before, same.State.Snapshot) && same.State.Message.Contains("缓存一致"), "same official day skips the full update even when the hour and zone changed");
                check((await File.ReadAllBytesAsync(path)).SequenceEqual(originalBytes), "same-day check does not rewrite the cached full ranking");
            }
            var sameHandler = new Handler(url => Fixture(url, "2026-10-16"));
            using(var same = new LeaderboardService(Path.Combine(dir, jp.Key), sameHandler, TimeSpan.Zero)) {
                await same.CheckDateAsync(jp);
                check(sameHandler.Requests == 1, "same-day restart requests only the ranking date page and no other boards or pagination");
            }
            using(var next = new LeaderboardService(Path.Combine(dir, jp.Key), new Handler(url => Fixture(url, "2026-10-17", 10000)), TimeSpan.Zero)) {
                await next.CheckDateAsync(jp);
                check(next.State.Snapshot?.OfficialDate == new DateOnly(2026,10,17) && next.State.Snapshot.Entries.All(e => long.Parse(e.CharacterId) > 10000), "changed official day replaces the complete regional ranking");
            }
            var latestBytes = await File.ReadAllBytesAsync(path);
            using(var missing = new LeaderboardService(Path.Combine(dir, jp.Key), new Handler(url => Fixture(url, "", omitDate:true)), TimeSpan.Zero)) {
                await missing.Ready; var before = missing.State.Snapshot;
                await missing.CheckDateAsync(jp);
                check(ReferenceEquals(before, missing.State.Snapshot) && missing.State.Message.Contains("日期"), "missing official update day retains cache rather than substituting the computer date or season dates");
            }
            using(var rollover = new LeaderboardService(Path.Combine(dir, jp.Key), new Handler(url => Fixture(url, url.Query.Contains("page=2") ? "2026-10-19" : "2026-10-18")), TimeSpan.Zero)) {
                await rollover.Ready; var before = rollover.State.Snapshot;
                await rollover.CheckDateAsync(jp);
                check(ReferenceEquals(before, rollover.State.Snapshot) && rollover.State.Message.Contains("日期发生变化"), "a date rollover during pagination cannot produce a mixed-day snapshot");
            }
            check((await File.ReadAllBytesAsync(path)).SequenceEqual(latestBytes), "failed date checks and incomplete updates preserve the last complete file");
            using(var ocean = new LeaderboardService(Path.Combine(dir, oc.Key), new Handler(url => Fixture(url, "2026-10-17")), TimeSpan.Zero)) {
                await ocean.CheckDateAsync(oc);
                check(ocean.State.Snapshot?.Entries.Length == 2 && ocean.State.Snapshot.Entries.All(e => e.Scope == "Materia"), "Oceanian selector discovers Materia from the official site without caching North American players");
            }
            var requests = new Dictionary<string, Handler>();
            using(var regional = new RegionalLeaderboardService(Path.Combine(dir, "startup"), 0, region => {
                var handler = new Handler(url => Fixture(url, "2026-10-18")); requests[region.Key] = handler;
                return new LeaderboardService(Path.Combine(dir, "startup", region.Key), handler, TimeSpan.Zero);
            })) {
                var startupA = regional.CheckAtStartupAsync(); var startupB = regional.CheckAtStartupAsync();
                check(ReferenceEquals(startupA, startupB), "repeated startup and login callbacks share one check task");
                await startupA;
                check(requests.Count == 4 && requests.All(p => p.Value.Requests > 0), "startup checks all four regions once");
                var total = requests.Values.Sum(h => h.Requests);
                foreach(var i in Enumerable.Range(0,4)) {
                    regional.SelectRegion(i);
                    check(regional.State.Snapshot?.RegionKey == RankingRegion.All[i].Key, "switching region " + i + " shows its independent cached snapshot");
                }
                await regional.CheckAtStartupAsync();
                check(requests.Values.Sum(h => h.Requests) == total, "browsing regions and repeated startup calls do not cause polling or extra network requests");
            }
        } finally { if(Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    sealed class Handler(Func<Uri,string> html) : HttpMessageHandler {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html(request.RequestUri!), System.Text.Encoding.UTF8, "text/html") });
        }
    }
}
