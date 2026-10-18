namespace CCStats.Core.Import;

public sealed record ReplaySummary(string MatchId, uint TerritoryId, string MapName,
    DateTimeOffset StartUtc, DateTimeOffset? EndUtc, int Frames, int Effects, int Skills,
    int MapEvents, IReadOnlyList<string> Players, string FilePath, bool IsObservation = false) {
    public TimeSpan? Duration => EndUtc.HasValue ? EndUtc.Value - StartUtc : null;
    public string DurationLabel => Duration is {} duration ? $"{(int)duration.TotalMinutes}:{duration.Seconds:00}" : "未记录";
}
