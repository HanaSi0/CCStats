using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PvpStats.Helpers;

/// <summary>Space-separated AND queries, quoted phrases, and optional excluded terms.</summary>
public static class MatchQuery {
    private static readonly Regex Tokens = new("(-?)\"([^\"]+)\"|([^\\s]+)", RegexOptions.Compiled);
    public readonly record struct Term(string Value, bool Excluded);
    public static IReadOnlyList<Term> Parse(string query) {
        var result = new List<Term>();
        foreach(Match token in Tokens.Matches(query ?? "")) {
            var quoted = token.Groups[2].Success;
            var value = quoted ? token.Groups[2].Value : token.Groups[3].Value;
            var excluded = quoted ? token.Groups[1].Value == "-" : value.StartsWith('-');
            if(!quoted && excluded) value = value[1..];
            if(!string.IsNullOrWhiteSpace(value)) result.Add(new(value, excluded));
        }
        return result;
    }
    public static bool Matches(string document, IReadOnlyList<Term> terms) {
        foreach(var term in terms) {
            var found = document.Contains(term.Value, StringComparison.OrdinalIgnoreCase);
            if(found == term.Excluded) return false;
        }
        return true;
    }
}
