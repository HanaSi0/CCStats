using PvpStats.Leaderboards;
using System.Text.Json;

static class LiveRankingCapture {
    public static async Task Reparse(string directory) {
        foreach(var region in RankingRegion.All) {
            var path = Path.Combine(directory, region.Key, "latest.json");
            var old = JsonSerializer.Deserialize<RankingSnapshot>(await File.ReadAllTextAsync(path))!;
            var pages = old.Pages.Select(p => LodestoneParser.Parse(p.Html, new Uri(p.SourceUrl), p.FetchedAt)).ToArray();
            var snapshot = old with { Pages = pages, Entries = pages.SelectMany(p => p.Entries).DistinctBy(e => e.CharacterId).ToArray(), SchemaVersion = RankingSnapshot.CurrentSchemaVersion };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine("Reparsed captured source pages using the current parser; no network requests.");
    }

    public static async Task Run(string directory) {
        using var service = new RegionalLeaderboardService(directory);
        await service.CheckAtStartupAsync();
        var report = new List<object>();
        var failed = new List<string>();
        foreach(var (region, index) in RankingRegion.All.Select((r, i) => (r, i))) {
            service.SelectRegion(index);
            var state = service.State;
            var s = state.Snapshot;
            Console.WriteLine($"{region.Name}: {state.Message}");
            if(s == null) failed.Add(region.Key);
            report.Add(new { region.Key, Message = state.Message, Success = s != null, s?.OfficialDate, s?.RootUrl,
                Count = s?.Entries.Length ?? 0, Pages = s?.Pages.Length ?? 0,
                First = s?.Entries.FirstOrDefault(), Last = s?.Entries.LastOrDefault() });
        }
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if(failed.Count > 0) throw new Exception("Live ranking capture failed for: " + string.Join(", ",failed));
    }
}
