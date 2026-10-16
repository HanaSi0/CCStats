using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PvpStats.Leaderboards;

/// <summary>Preserves the source HTML and unknown fields in addition to recognized display fields.</summary>
public static class LodestoneParser {
    public const string RankingPath = "/lodestone/ranking/crystallineconflict/";
    public static readonly string[] Hosts = ["na.finalfantasyxiv.com", "eu.finalfantasyxiv.com", "jp.finalfantasyxiv.com"];
    private static readonly Regex Space = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Character = new(@"^/lodestone/character/(\d+)/?$", RegexOptions.Compiled);
    public static string Text(HtmlNode node) => Space.Replace(HtmlEntity.DeEntitize(node.InnerText), " ").Trim();
    public static Dictionary<string, string> Query(Uri url) => url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Split('=', 2)).GroupBy(x => WebUtility.UrlDecode(x[0]), StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => WebUtility.UrlDecode(g.Last().Length == 2 ? g.Last()[1] : ""), StringComparer.Ordinal);
    public static bool IsOfficial(Uri url) => url.Scheme == "https" && url.IsDefaultPort && url.UserInfo == "" && Hosts.Contains(url.Host);
    public static Uri Root(int site) => new($"https://{Hosts[Math.Clamp(site, 0, Hosts.Length - 1)]}{RankingPath}");
    public static Uri ScopeUrl(Uri root, string scope) {
        if(!IsOfficial(root) || root.AbsolutePath != RankingPath) throw new ArgumentException("无效的官方榜单地址。");
        var query = Query(root);
        query.Remove("page");
        if(scope.Length == 0) query.Remove("dcgroup"); else query["dcgroup"] = scope;
        return Build(root, query);
    }
    public static Uri PageUrl(Uri root, int page) {
        if(page < 1 || page > 250) throw new FormatException("官方分页超出 250 页上限。");
        var query = Query(root);
        if(page == 1) query.Remove("page"); else query["page"] = page.ToString(CultureInfo.InvariantCulture);
        return Build(root, query);
    }
    private static Uri Build(Uri root, Dictionary<string, string> query) => new UriBuilder(root) {
        Query = string.Join("&", query.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))), Fragment = ""
    }.Uri;
    public static Uri? CrawlLink(Uri current, string href, bool allowScope) {
        if(!Uri.TryCreate(current, HtmlEntity.DeEntitize(href), out var link) || !IsOfficial(link)
            || link.Host != current.Host || link.AbsolutePath != current.AbsolutePath || link.AbsolutePath != RankingPath) return null;
        var before = Query(current); var after = Query(link);
        if(before.GetValueOrDefault("rank_type") == "all") before.Remove("rank_type");
        if(after.GetValueOrDefault("rank_type") == "all") after.Remove("rank_type");
        foreach(var key in before.Keys.Union(after.Keys)) {
            if(key == "page" || allowScope && key == "dcgroup") continue;
            if(before.GetValueOrDefault(key) != after.GetValueOrDefault(key)) return null;
        }
        if(!allowScope && before.GetValueOrDefault("dcgroup") != after.GetValueOrDefault("dcgroup")) return null;
        if(after.TryGetValue("page", out var page) && (!int.TryParse(page, out var n) || n < 1 || n > 250)) return null;
        if(after.GetValueOrDefault("page") == "1") after.Remove("page");
        return Build(link, after);
    }

    public static RankingPage Parse(string html, Uri source, DateTimeOffset fetchedAt) {
        if(!IsOfficial(source) || source.AbsolutePath != RankingPath) throw new ArgumentException("来源必须是官方水晶冲突榜单。");
        var document = new HtmlDocument(); document.LoadHtml(html);
        var root = document.DocumentNode;
        var isPage = int.TryParse(Query(source).GetValueOrDefault("page"), out var pageNumber) && pageNumber >= 2;
        if(isPage && string.IsNullOrWhiteSpace(html)) return new() {
            Html = html, SourceUrl = source.AbsoluteUri, FetchedAt = fetchedAt,
            Scope = Query(source).GetValueOrDefault("dcgroup", "官方默认"), IsFragment = true, ExplicitEmpty = true,
        };
        // A maintenance, login or challenge page must never become a successful empty ranking.
        var rankingContext = root.Descendants().Any(n => n.GetAttributeValue("class", "").Contains("ranking", StringComparison.OrdinalIgnoreCase));
        if(!rankingContext) throw new FormatException("页面没有排行榜结构，可能正在维护、访问受限或页面格式已变更。");
        var selectedScope = root.Descendants("select").Where(n => n.GetAttributeValue("name", "") == "dcgroup")
            .SelectMany(n => n.Descendants("option")).FirstOrDefault(n => n.Attributes.Contains("selected"))?.GetAttributeValue("value", "");
        if(string.IsNullOrWhiteSpace(selectedScope)) {
            var selected = root.Descendants("li").FirstOrDefault(n => HasClass(n, "selected")
                && n.Ancestors().Any(a => HasClass(a, "cc-ranking__select__data_center__list")));
            if(selected != null) selectedScope = Text(selected).Split('/').Last().Trim();
        }
        var scope = Query(source).GetValueOrDefault("dcgroup", string.IsNullOrWhiteSpace(selectedScope) ? "官方默认" : selectedScope);
        var actualRows = root.Descendants().Where(n => HasClass(n, "ranking_set") && n.Attributes.Contains("data-href")).ToArray();
        var fragment = isPage && actualRows.Length > 0 && !root.Descendants().Any(n => HasClass(n, "cc-ranking__heading"));
        var entries = new List<RankingEntry>();
        var ids = new HashSet<string>();
        var candidates = new List<(HtmlNode Row, Uri Profile, string Id)>();
        foreach(var row in actualRows) {
            if(!Uri.TryCreate(source, row.GetAttributeValue("data-href", ""), out var profile) || !IsOfficial(profile)) continue;
            var idMatch = Character.Match(profile.AbsolutePath);
            if(idMatch.Success) candidates.Add((row, profile, idMatch.Groups[1].Value));
        }
        // Retain support for conventional linked rows used by older markup.
        foreach(var anchor in root.Descendants("a")) {
            if(!Uri.TryCreate(source, anchor.GetAttributeValue("href", ""), out var profile) || !IsOfficial(profile)) continue;
            var idMatch = Character.Match(profile.AbsolutePath);
            if(!idMatch.Success) continue;
            HtmlNode? row = null;
            foreach(var ancestor in anchor.Ancestors().Take(12)) {
                var ancestorIds = ancestor.Descendants("a").Select(a => {
                    if(!Uri.TryCreate(source, a.GetAttributeValue("href", ""), out var u)) return "";
                    return Character.Match(u.AbsolutePath).Groups[1].Value;
                }).Where(x => x.Length > 0).Distinct().Take(2).ToArray();
                if(ancestorIds.Length > 1) break;
                if(ancestor.Name is "li" or "tr" || ancestor.GetAttributeValue("class", "").Contains("ranking", StringComparison.OrdinalIgnoreCase)) row = ancestor;
                if(ancestor.Name is "li" or "tr") break;
            }
            if(row != null) candidates.Add((row, profile, idMatch.Groups[1].Value));
        }
        foreach(var (row, profile, id) in candidates) {
            if(!ids.Add(id)) continue;
            var nodes = row.DescendantsAndSelf().ToArray();
            HtmlNode? Node(params string[] tokens) {
                foreach(var token in tokens) {
                    var node = nodes.FirstOrDefault(n => HasClass(n, token) || n.GetAttributeValue("class", "").Split(' ').Any(c => c.EndsWith("__" + token, StringComparison.OrdinalIgnoreCase)));
                    if(node != null) return node;
                }
                return null;
            }
            string Field(params string[] tokens) {
                foreach(var token in tokens) {
                    var found = nodes.FirstOrDefault(n => n.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Any(c => c.Equals(token, StringComparison.OrdinalIgnoreCase) || c.EndsWith("__" + token, StringComparison.OrdinalIgnoreCase)));
                    if(found != null) { var text = Text(found); if(text.Length > 0) return text; }
                }
                return "";
            }
            var raw = new Dictionary<string, List<string>>();
            void Add(string key, string value) {
                if(value.Length == 0) return;
                if(!raw.TryGetValue(key, out var values)) raw[key] = values = new();
                if(!values.Contains(value)) values.Add(value);
            }
            foreach(var node in nodes) {
                var classes = node.GetAttributeValue("class", "");
                if(classes.Length > 0) Add(classes, Text(node));
                foreach(var attribute in node.Attributes.Where(a => a.Name is "title" or "alt" || a.Name.StartsWith("data-", StringComparison.Ordinal)))
                    Add(classes + "@" + attribute.Name, HtmlEntity.DeEntitize(attribute.Value));
            }
            string ImageUrl(HtmlNode node) {
                var value = node.GetAttributeValue("src", node.GetAttributeValue("data-src", ""));
                return value.Length > 0 && Uri.TryCreate(source, value, out var url) && url.Scheme == "https" ? url.AbsoluteUri : "";
            }
            var images = nodes.Where(n => n.Name == "img").Select(ImageUrl).Where(x => x.Length > 0).Distinct().ToArray();
            var actualName = Node("cc-ranking__result__name")?.Descendants("h3").FirstOrDefault();
            var name = actualName != null ? Text(actualName) : Field("character__name", "name");
            if(name.Length == 0) name = row.Descendants("a").Select(Text).FirstOrDefault(x => x.Length > 0) ?? "";
            var rank = Field("order", "rank", "place");
            var previous = Node("prev_order");
            var previousRank = previous != null ? Text(previous) : "";
            var change = Field("change", "rankchange", "order__change");
            if(previous != null && int.TryParse(previousRank, out var before) && int.TryParse(rank, out var now)) {
                var delta = before - now;
                change = delta > 0 ? "+" + delta : delta.ToString(CultureInfo.InvariantCulture);
            }
            var tierNode = Node("tier", "rank__tier");
            var tier = tierNode != null ? Text(tierNode) : "";
            if(tierNode != null) {
                var imageTier = tierNode.Descendants("img").Select(n => n.GetAttributeValue("data-tooltip", n.GetAttributeValue("alt", ""))).FirstOrDefault(x => x.Length > 0);
                if(imageTier != null) tier = imageTier;
            }
            var division = tierNode?.Descendants("p").Select(Text).FirstOrDefault() ?? "";
            var points = Node("points", "rating", "credit", "point");
            var wins = Node("wins", "victory", "victories");
            string Value(HtmlNode? node) => node == null ? "" : Text(node.Descendants("p").FirstOrDefault() ?? node);
            string Delta(HtmlNode? node) => node?.Descendants("span").Where(n => HasClass(n, "up") || HasClass(n, "down")).Select(Text).FirstOrDefault(x => x.Length > 0) ?? "";
            var face = Node("face")?.Descendants("img").FirstOrDefault();
            entries.Add(new() {
                CharacterId = id, CharacterName = name, ProfileUrl = profile.AbsoluteUri, Scope = scope,
                PortraitUrl = face != null ? ImageUrl(face) : nodes.Where(n => n.Name == "img" && n.Ancestors("a").Any()).Select(ImageUrl).FirstOrDefault(x => x.Length > 0) ?? "",
                Rank = rank, RankChange = change, PreviousRank = previousRank,
                World = Field("character__world", "world"), Tier = tier, Division = division,
                Rating = Value(points), Victories = Value(wins), RatingChange = Delta(points), VictoriesChange = Delta(wins),
                RowText = Text(row), SourceUrl = source.AbsoluteUri, ImageUrls = images,
                RawFields = raw.ToDictionary(x => x.Key, x => x.Value.ToArray()),
            });
        }
        var links = root.Descendants("a").Select(a => CrawlLink(source, a.GetAttributeValue("href", ""), true)).Where(u => u != null).Cast<Uri>().ToList();
        foreach(var select in root.Descendants("select").Where(n => n.GetAttributeValue("name", "") == "dcgroup"))
            foreach(var option in select.Descendants("option")) {
                var value = option.GetAttributeValue("value", "");
                if(value.Length > 0 && !value.Contains('/')) links.Add(ScopeUrl(source, value));
            }
        if(scope != "官方默认") links.Add(ScopeUrl(source, scope));
        var sameScope = links.Where(u => Query(u).GetValueOrDefault("dcgroup", scope) == scope)
            .Select(u => {
                if(scope == "官方默认" || Query(u).ContainsKey("dcgroup")) return u;
                var q = Query(u); q["dcgroup"] = scope; return Build(u, q);
            });
        var empty = root.Descendants().Any(n => n.GetAttributeValue("class", "").Split(' ').Any(c => c.Contains("ranking", StringComparison.OrdinalIgnoreCase) && (c.Contains("empty") || c.Contains("nodata") || c.Contains("no_data"))));
        if(entries.Count == 0 && !empty) throw new FormatException("没有识别到榜单角色；已停止抓取，防止把页面变更当作空榜单。");
        var nextPages = sameScope.Select(u => u.AbsoluteUri).ToList();
        var loader = root.Descendants().FirstOrDefault(n => HasClass(n, "js--ranking-loading"));
        var pagingSource = scope == "官方默认" ? source : ScopeUrl(source, scope);
        if(loader != null && int.TryParse(loader.GetAttributeValue("data-next-page", ""), out var nextPage)) nextPages.Add(PageUrl(pagingSource, nextPage).AbsoluteUri);
        if(fragment && entries.Count > 0) nextPages.Add(PageUrl(pagingSource, pageNumber + 1).AbsoluteUri);
        bool IsRow(HtmlNode n) => n.Ancestors().Any(a => HasClass(a, "ranking_set"));
        string Heading(HtmlNode n) { var text = Text(n); return text.Length > 0 ? text : string.Join(" ", n.Descendants("img").Select(i => i.GetAttributeValue("alt", "")).Where(x => x.Length > 0)); }
        var season = root.Descendants("h2").Where(n => n.Ancestors().Any(a => HasClass(a, "cc-ranking__header-data"))).Select(Heading).FirstOrDefault() ?? "";
        return new() {
            OfficialDate = OfficialRankingDate.Read(root, source), IsFragment = fragment, Season = season,
            Scope = scope, SourceUrl = source.AbsoluteUri, Title = root.Descendants("title").Select(Text).FirstOrDefault() ?? "",
            Headings = root.Descendants().Where(n => (n.Name is "h1" or "h2" or "h3") && !IsRow(n)).Select(Heading).Where(x => x.Length > 0).Distinct().ToArray(),
            TimeLabels = root.Descendants().Where(n => n.Name == "time" || HasClass(n, "summarytime") || Regex.IsMatch(n.GetAttributeValue("class", ""), @"(?:ranking|update).*(?:date|time)", RegexOptions.IgnoreCase)).Select(Text).Where(x => x.Length > 0).Distinct().ToArray(),
            Html = html, FetchedAt = fetchedAt, Entries = entries.ToArray(), ExplicitEmpty = empty,
            NextPages = nextPages.Distinct().ToArray(),
            Scopes = links.Select(u => Query(u).GetValueOrDefault("dcgroup", "")).Where(x => x.Length > 0).Distinct().ToArray(),
        };
    }

    private static bool HasClass(HtmlNode node, string value) => node.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
    public static string CanonicalTier(string tier) => tier switch {
        "アルテマ" => "Ultima", "オメガ" => "Omega", "クリスタル" => "Crystal", "ダイヤモンド" => "Diamond",
        "プラチナ" => "Platinum", "ゴールド" => "Gold", "シルバー" => "Silver", "ブロンズ" => "Bronze", _ => tier,
    };
    public static string Csv(IEnumerable<RankingEntry> entries) {
        string Quote(string text) {
            // Avoid spreadsheet formulas while preserving the original value in JSON/HTML exports.
            if(text.Length > 0 && ("=+-@\t\r".Contains(text[0]))) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var result = new StringBuilder("榜单,名次,排名变化,角色,服务器与数据中心,段位,积分,胜场,角色ID,角色链接,头像链接,来源,原始行内容,上次名次,积分变化,胜场变化,阶位\r\n");
        foreach(var e in entries) result.AppendLine(string.Join(",", new[] {e.Scope,e.Rank,e.RankChange,e.CharacterName,e.World,e.Tier,e.Rating,e.Victories,e.CharacterId,e.ProfileUrl,e.PortraitUrl,e.SourceUrl,e.RowText,e.PreviousRank,e.RatingChange,e.VictoriesChange,e.Division}.Select(Quote)));
        return result.ToString();
    }
}
