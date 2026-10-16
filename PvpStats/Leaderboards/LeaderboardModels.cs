using System;
using System.Collections.Generic;

namespace PvpStats.Leaderboards;

public sealed record RankingEntry {
    public string CharacterId { get; init; } = "";
    public string CharacterName { get; init; } = "";
    public string ProfileUrl { get; init; } = "";
    public string PortraitUrl { get; init; } = "";
    public string Scope { get; init; } = "";
    public string Rank { get; init; } = "";
    public string RankChange { get; init; } = "";
    public string PreviousRank { get; init; } = "";
    public string RatingChange { get; init; } = "";
    public string VictoriesChange { get; init; } = "";
    public string World { get; init; } = "";
    public string Tier { get; init; } = "";
    public string Division { get; init; } = "";
    public string Rating { get; init; } = "";
    public string Victories { get; init; } = "";
    public string RowText { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public Dictionary<string, string[]> RawFields { get; init; } = new();
    public string[] ImageUrls { get; init; } = [];
}

public sealed record RankingPage {
    public DateOnly? OfficialDate { get; init; }
    public bool IsFragment { get; init; }
    public string Season { get; init; } = "";
    public string Scope { get; init; } = "官方默认";
    public string SourceUrl { get; init; } = "";
    public string Title { get; init; } = "";
    public string[] Headings { get; init; } = [];
    public string[] TimeLabels { get; init; } = [];
    public DateTimeOffset FetchedAt { get; init; }
    public string Html { get; init; } = "";
    public RankingEntry[] Entries { get; init; } = [];
    public string[] NextPages { get; init; } = [];
    public string[] Scopes { get; init; } = [];
    public bool ExplicitEmpty { get; init; }
}

public sealed record RankingSnapshot {
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; }
    public DateOnly? OfficialDate { get; init; }
    public string RegionKey { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public string RootUrl { get; init; } = "";
    public bool AllScopes { get; init; }
    public RankingPage[] Pages { get; init; } = [];
    public RankingEntry[] Entries { get; init; } = [];
    public string[] Scopes { get; init; } = [];
}
