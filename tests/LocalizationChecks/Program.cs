using System.Text;
using System.Numerics;
using PvpStats.Windows.Floating;
using LiteDB;
using PvpStats.Helpers;
using PvpStats.Localization;
using PvpStats.Types;
using PvpStats.Types.Match;
using PvpStats.Types.Player;

if(args.Length > 1 && args[0] == "--export-themes") {
    var palettes = Enumerable.Range(0, PvpStats.Settings.ThemeConfiguration.PresetNames.Length).Select(i => {
        var theme = PvpStats.Settings.ThemeConfiguration.Preset(i);
        return typeof(PvpStats.Settings.ThemeConfiguration).GetProperties().Where(p => p.PropertyType == typeof(Vector4))
            .ToDictionary(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p => {
                var c = (Vector4)p.GetValue(theme)!;
                return $"#{(int)MathF.Round(c.X*255):x2}{(int)MathF.Round(c.Y*255):x2}{(int)MathF.Round(c.Z*255):x2}";
            });
    }).ToArray();
    File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new { names = PvpStats.Settings.ThemeConfiguration.PresetNames, palettes }));
    return;
}

if(args.Length > 1 && args[0] == "--reparse-rankings") {
    await LiveRankingCapture.Reparse(args[1]);
    return;
}
if(args.Length > 0 && args[0] == "--capture-rankings") {
    await LiveRankingCapture.Run(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "ccstats-live-rankings"));
    return;
}

int passed = 0;
void Check(bool condition, string description) {
    if(!condition) throw new Exception("FAILED: " + description);
    passed++;
}
// Dear ImGui's CRC32 string hash, including its ### reset rule.
uint HashLabel(string text) {
    var bytes = Encoding.UTF8.GetBytes(text);
    uint crc = uint.MaxValue;
    for(int i = 0; i < bytes.Length; i++) {
        if(bytes[i] == '#' && i + 2 < bytes.Length && bytes[i+1] == '#' && bytes[i+2] == '#') crc = uint.MaxValue;
        crc ^= bytes[i];
        for(int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
    }
    return ~crc;
}
Check(ChineseText.Text("Paladin") == "骑士", "confirmed full job name");
Check(ChineseText.EnumName(Job.VPR) == "蝰蛇剑士", "job enum display alias");
Check(ChineseText.EnumName(JobSubRole.RANGED) == "远程物理输出职业", "role display alias");
Check(ChineseText.EnumName(CrystallineConflictMatchType.Casual) == "练习赛", "confirmed match type");
Check(ChineseText.ActionName(29428, "Fan Dance") == "扇舞·序", "action ID uses action translation");
Check(ChineseText.StatusName(2052, "Fan Dance") == "扇舞", "same English text uses distinct status translation");
Check(ChineseText.StatusName(3054, "Guard") == "防御" && ChineseText.StatusName(3673, "Guard") == "防御", "duplicate English states retain separate IDs");
Check(ChineseText.ActionName(3054, "unmapped action") == "unmapped action", "action lookup cannot consume a status ID");
Check(ChineseText.StatusName(uint.MaxValue, "new status") == "new status", "unknown status retains game fallback");
Check(ChineseText.ActionName(uint.MaxValue, "new action") == "new action", "unknown action retains game fallback");
Check(ChineseText.Text("Example Player@Example World") == "Example Player@Example World", "unmapped identity is unchanged");
Check(ChineseText.Text("Fourth Generation Combo") == "侧击擦齿连击" && ChineseText.ActionName(39172, "Fourth Generation") == "祖灵之牙四式", "combo and individual action retain independently confirmed names");
Check(ChineseText.EnumName((Job?)null) == "", "missing historical job displays safely");
Check(ChineseText.Label("Save###persisted-control") == "保存###persisted-control", "explicit ID suffix survives translation");
Check(HashLabel(ChineseText.Label("Save###persisted-control")) == HashLabel("Save###persisted-control"), "explicit ImGui control hash remains identical");
Check(ChineseText.Label("Save##first") != ChineseText.Label("Save##second"), "same visible text does not collide across original IDs");
Check(HashLabel(ChineseText.EnumLabel(CrystallineConflictMatchType.Casual,"Casual###queue")) == HashLabel("Casual###queue"), "enum label preserves explicit ImGui ID");
Check(ChineseText.Label("###hidden") == "###hidden", "hidden-only controls remain untouched");
Check(ChineseText.Label(ChineseText.Label("Save")) == ChineseText.Label("Save"), "translation wrappers are idempotent");
Check(MatchHelper.GetTier("crystal") == ArenaTier.Crystal && MatchHelper.ArenaRankLookup[ArenaTier.Crystal] == "crystal", "rank recognition keys remain English");
Check(MatchHelper.GetTeamName("Astra") == CrystallineConflictTeamName.Astra, "team recognition remains compatible");
Check(MatchHelper.GetTeamName(CrystallineConflictTeamName.Astra) == "星极队", "team display is Chinese");
Check(MatchHelper.GetArena(1032) == CrystallineConflictMap.Palaistra && MatchHelper.GetArenaName(CrystallineConflictMap.Palaistra) == "角力学校", "map ID and translated display agree");
Check(Enum.GetName(Job.PLD) == "PLD" && (int)ArenaTier.Crystal == 6, "persisted enum names and numbers are unchanged");
var rank = new PlayerRank { Tier = ArenaTier.Crystal, Credit = 1234 };
var before = BsonMapper.Global.ToDocument(rank).ToString();
Check(rank.ToString().Contains("水晶") && rank.ToString().Contains("1234"), "rank display retains numeric credit");
var after = BsonMapper.Global.ToDocument(rank).ToString();
Check(before == after && !after.Contains("水晶"), "display translation does not alter persisted rank document");
Check(((PlayerRank)"bronze 2").Tier == ArenaTier.Bronze, "original rank string parser still works");
Check(GamePeriod.Expansion[0].Name == "Endwalker" && ChineseText.Text(GamePeriod.Expansion[0].Name) == "晓月之终途", "expansion display does not mutate source names");
var launcher = new LauncherInteraction();
var start = new Vector2(40, 200);
var bounds = new Vector2(1920, 1080);
var left = launcher.Update(start, new(50, 210), bounds, 48, true, false, false);
Check(left.OpenWindow && left.Position == start, "launcher left click opens without moving");
var outside = launcher.Update(start, new(500, 500), bounds, 48, false, false, true);
Check(!launcher.IsDragging && outside.Position == start, "right hold outside the icon does not start dragging");
var right = launcher.Update(start, new(50, 210), bounds, 48, false, true, true);
Check(launcher.IsDragging && !right.OpenWindow && !right.SavePosition, "right press begins a drag without opening or writing configuration");
var moved = launcher.Update(start, new(150, 260), bounds, 48, false, false, true);
Check(moved.Position == new Vector2(140, 250) && !moved.SavePosition, "drag keeps pointer offset and does not save every frame");
var outsideMoved = launcher.Update(moved.Position, new(500, 600), bounds, 48, false, false, true);
Check(launcher.IsDragging && outsideMoved.Position == new Vector2(490, 590), "right drag continues after pointer leaves the original icon bounds");
var released = launcher.Update(outsideMoved.Position, new(500, 600), bounds, 48, true, false, false);
Check(!launcher.IsDragging && released.SavePosition && !released.OpenWindow, "right release saves once and does not also open the window");
var idle = launcher.Update(released.Position, new(500, 600), bounds, 48, false, false, false);
Check(!idle.SavePosition, "idle frames do not repeatedly persist the position");
launcher.Update(start, new(50, 210), bounds, 48, false, true, true);
var clickOnly = launcher.Update(start, new(50, 210), bounds, 48, false, false, false);
Check(!clickOnly.SavePosition && !clickOnly.OpenWindow, "a stationary right click does not open or write configuration");
launcher.Update(start, new(50, 210), bounds, 48, false, true, true);
var clipped = launcher.Update(start, new(5000, 5000), bounds, 48, false, false, true);
Check(clipped.Position == new Vector2(1872, 1032), "icon is clamped entirely inside the viewport");
launcher.CancelDrag();
Check(!launcher.IsDragging, "hiding or logging out cancels pointer capture");
Check(LauncherInteraction.Clamp(new(-500, -50), bounds, 48) == Vector2.Zero, "negative saved positions are recovered on screen");
Check(LauncherInteraction.Clamp(new(1000, 1000), new(800, 600), 48) == new Vector2(752, 552), "viewport resize recovers offscreen saved position");
Check(LauncherInteraction.Clamp(new(float.NaN, float.PositiveInfinity), bounds, 48) == start, "invalid saved coordinates fall back to the initial location");
Check(LauncherInteraction.Clamp(start, new(16, 16), 48) == Vector2.Zero, "very small viewports do not produce negative positions");
Check(MatchQuery.Matches("角力学校 骑士 段位赛 冲分", MatchQuery.Parse("角力 骑士")), "search requires all Chinese terms");
Check(!MatchQuery.Matches("角力学校 骑士", MatchQuery.Parse("角力 战士")), "search rejects missing terms");
Check(MatchQuery.Matches("Alice Example@World PLD", MatchQuery.Parse("\"Alice Example\" pld")), "search supports quoted player names and case-insensitive English");
Check(!MatchQuery.Matches("Alice Other Example", MatchQuery.Parse("\"Alice Example\"")), "quoted search requires the exact phrase");
Check(!MatchQuery.Matches("九霄云上 练习赛", MatchQuery.Parse("九霄 -练习赛")), "search excludes forbidden terms");
Check(MatchQuery.Matches("九霄云上 段位赛", MatchQuery.Parse("九霄 -练习赛")), "search combines positive and excluded terms");
Check(!MatchQuery.Matches("Alice Example World", MatchQuery.Parse("-\"Alice Example\"")), "search supports excluded quoted phrases");
Check(MatchQuery.Matches("any record", MatchQuery.Parse("   ")), "blank search does not hide records");
Check(MatchQuery.Parse("-").Count == 0, "unfinished exclusion token does not hide every record");
Check(MatchQuery.Matches("2026-10-17 abc123 收藏", MatchQuery.Parse("2026-10 abc123")), "search accepts dates and record IDs as ordinary text");
Check(MatchQuery.Matches("A.*B", MatchQuery.Parse("A.*B")), "search treats regex characters as literal text");
var theme = new PvpStats.Settings.ThemeConfiguration();
var savedTheme = theme.Copy();
theme.Accent = new Vector4(.3f, .6f, .9f, 1);
Check(savedTheme.Accent != theme.Accent, "saved palette is independent from live edits");
var invalidTheme = new PvpStats.Settings.ThemeConfiguration { Background = new(float.NaN, -2, 4, 0), Text = new(float.PositiveInfinity, .5f, .7f, 0) };
invalidTheme.Normalize();
Check(float.IsFinite(invalidTheme.Background.X) && invalidTheme.Background.Y == 0 && invalidTheme.Background.Z == 1, "invalid saved channels recover to valid colors");
Check(invalidTheme.Background.W == .35f && invalidTheme.Text.W == 1, "window opacity remains visible and foreground colors remain opaque");
var partialTheme = System.Text.Json.JsonSerializer.Deserialize<PvpStats.Settings.ThemeConfiguration>("{}");
Check(partialTheme != null && partialTheme.Accent == savedTheme.Accent, "older configuration missing palette fields receives defaults");
for(var i = 0; i < PvpStats.Settings.ThemeConfiguration.PresetNames.Length; i++) {
    var original = PvpStats.Settings.ThemeConfiguration.Preset(i);
    var roundtrip = System.Text.Json.JsonSerializer.Deserialize<PvpStats.Settings.ThemeConfiguration>(
        System.Text.Json.JsonSerializer.Serialize(original, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }),
        new System.Text.Json.JsonSerializerOptions { IncludeFields = true })!;
    Check(roundtrip.Accent == original.Accent && roundtrip.Background == original.Background && roundtrip.Selection == original.Selection,
        "preset " + i + " survives saving and reloading");
}
Check(WinRateLabel.Rate(12,8) == .6 && WinRateLabel.Format(12,8).Contains("12/20"), "win-rate labels expose the actual numerator and valid-match denominator");
Check(WinRateLabel.Rate(0,0) == 0 && WinRateLabel.Format(0,0).StartsWith("—"), "empty win-rate samples show unavailable instead of NaN or a misleading rate");
Check(WinRateLabel.Rate(0,5) == 0 && WinRateLabel.Format(0,5).Contains("0/5"), "zero wins with valid matches remains a real zero-rate sample");
Check(WinRateLabel.Rate(5,0) == 1 && WinRateLabel.Format(5,0).Contains("5/5"), "all-win sample preserves its actual match count");
Check(ChineseText.Text("Current Ranked Season:") == "当前段位赛赛季：" && ChineseText.Text("Current Rank: ") == "当前段位：" && ChineseText.Text("Highest Rank: ") == "最高段位：", "profile season and rank labels are translated");
Check(StatDisplay.Number(double.NaN)=="—" && StatDisplay.Number(double.PositiveInfinity)=="—" && StatDisplay.Number(0,false)=="—", "job metrics distinguish unavailable data from valid zero");
Check(StatDisplay.Number(0)=="0" && StatDisplay.Duration(0)=="0:00" && StatDisplay.Duration(3661)=="61:01", "job totals preserve real zero and durations longer than an hour");
Check(StatDisplay.Duration(double.NaN)=="—" && StatDisplay.Duration(double.PositiveInfinity)=="—" && StatDisplay.Duration(-1)=="—", "invalid job durations never throw or show NaN");
Check(StatDisplay.CardColumns(610,1)==1 && StatDisplay.CardColumns(640,1)==2 && StatDisplay.CardColumns(1000,1)==3 && StatDisplay.CardColumns(1280,2)==2, "job card grid keeps readable minimum widths across scaling");
MovementReplayChecks.Run(Check);
await StabilityChecks.Run(Check);
StatisticsChecks.Run(Check);
DetailedTelemetryChecks.Run(Check);
UiInteractionChecks.Run(Check);
LegacyImportChecks.Run(Check);
await LeaderboardChecks.Run(Check);
await DailyRankingChecks.Run(Check);
OfficialMarkupChecks.Run(Check);
await ScrollCacheChecks.Run(Check);
Console.WriteLine($"Passed {passed} localization, launcher, query, theme and leaderboard checks.");
