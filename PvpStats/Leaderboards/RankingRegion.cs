using System;
using System.Linq;

namespace PvpStats.Leaderboards;

public sealed record RankingRegion(string Key, string Name, int Site, string[] ScopeAliases) {
    // Region names are UI categories; actual query values are always discovered on the official page.
    public static readonly RankingRegion[] All = [
        new("japan", "日服", 2, ["Japan", "JP", "日本", "Elemental", "Gaia", "Mana", "Meteor"]),
        new("north-america", "北美服", 0, ["NorthAmerica", "NA", "北米", "北美", "Aether", "Crystal", "Dynamis", "Primal"]),
        new("europe", "欧服", 1, ["Europe", "EU", "欧州", "欧洲", "Chaos", "Light"]),
        new("oceania", "大洋洲服", 0, ["Oceania", "OC", "オセアニア", "大洋洲", "Materia"]),
    ];
    private static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    public bool Includes(string scope) => ScopeAliases.Any(alias => Normalize(alias) == Normalize(scope));
}
