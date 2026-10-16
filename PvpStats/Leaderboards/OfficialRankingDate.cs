using HtmlAgilityPack;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PvpStats.Leaderboards;

/// <summary>The calendar day printed by the ranking page, never the PC clock or HTTP cache date.</summary>
public static class OfficialRankingDate {
    private static readonly Regex YearFirst = new(@"(?<!\d)(\d{4})[-/年.](\d{1,2})[-/月.](\d{1,2})(?:日)?(?!\d)", RegexOptions.Compiled);
    private static readonly Regex YearLast = new(@"(?<!\d)(\d{1,2})[/.](\d{1,2})[/.](\d{4})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex UpdateClass = new(@"(?:\bsummarytime\b|ranking.*(?:date|time|update)|(?:date|time|update).*ranking|last[_-]?updated?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static DateOnly? ParseLabel(string text, string host) {
        var first = YearFirst.Matches(text).Cast<Match>().Select(m => Make(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)).ToArray();
        if(first.Length > 0) return Unique(first);
        var last = YearLast.Matches(text).Cast<Match>().Select(m => host == "eu.finalfantasyxiv.com"
            ? Make(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value)
            : Make(m.Groups[3].Value, m.Groups[1].Value, m.Groups[2].Value)).ToArray();
        if(last.Length > 0) return Unique(last);
        // English month names are unambiguous and may be used by either English site.
        var english = Regex.Match(text, @"\b(?:\d{1,2}\s+[A-Za-z]+\s+\d{4}|[A-Za-z]+\s+\d{1,2},?\s+\d{4})\b");
        return english.Success && DateOnly.TryParse(english.Value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date) ? date : null;
    }
    private static DateOnly? Make(string year, string month, string day) {
        if(!int.TryParse(year, out var y) || !int.TryParse(month, out var m) || !int.TryParse(day, out var d)) return null;
        try { return new DateOnly(y, m, d); } catch(ArgumentOutOfRangeException) { return null; }
    }
    private static DateOnly? Unique(DateOnly?[] dates) => dates.Length > 0 && dates.All(d => d.HasValue) && dates.Distinct().Count() == 1 ? dates[0] : null;
    public static DateOnly? Read(HtmlNode root, Uri source) {
        var updateNodes = root.Descendants().Where(n => UpdateClass.IsMatch(n.GetAttributeValue("class", ""))).ToArray();
        // Prefer explicitly labeled ranking update nodes over unrelated time elements (e.g. season dates).
        var candidates = updateNodes.Length > 0 ? updateNodes : root.Descendants("time").ToArray();
        var dates = candidates.Select(n => {
            if(n.GetAttributeValue("class", "").Split(' ').Contains("summarytime")) {
                var spans = n.Descendants("span").Select(s => ParseLabel(LodestoneParser.Text(s), source.Host)).ToArray();
                if(spans.Length > 0) return Unique(spans);
            }
            // Use the day actually shown on the site before an ISO attribute in a different zone.
            var displayed = ParseLabel(LodestoneParser.Text(n), source.Host);
            if(displayed.HasValue) return displayed;
            var attributeDates = n.DescendantsAndSelf().Where(t => t.GetAttributeValue("datetime", "").Length > 0)
                .Select(t => ParseLabel(t.GetAttributeValue("datetime", ""), source.Host)).ToArray();
            return Unique(attributeDates);
        }).ToArray();
        // An unparseable update label must not be replaced with the computer's current day.
        return Unique(dates);
    }
}
