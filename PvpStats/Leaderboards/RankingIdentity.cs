using System;
using System.Text;
using System.Text.RegularExpressions;

namespace PvpStats.Leaderboards;
public static class RankingIdentity {
    private static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ");
    public static bool Matches(RankingEntry entry, string name, string homeWorld) {
        if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(homeWorld)) return false;
        var world=Normalize(entry.World);
        var bracket=world.IndexOf('[');
        if(bracket>=0)world=world[..bracket].Trim();
        return string.Equals(Normalize(entry.CharacterName),Normalize(name),StringComparison.OrdinalIgnoreCase)
            && string.Equals(world,Normalize(homeWorld),StringComparison.OrdinalIgnoreCase);
    }
}
