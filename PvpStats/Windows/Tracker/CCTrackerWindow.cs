using PvpStats.Localization;
using PvpStats.Interface;
using PvpStats.Helpers;
using System;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using PvpStats.Types.Match;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using PvpStats.Windows.Filter;
using PvpStats.Windows.List;
using PvpStats.Windows.Leaderboards;
using PvpStats.Windows.Records;
using PvpStats.Windows.Summary;

namespace PvpStats.Windows.Tracker;

internal class CCTrackerWindow : TrackerWindow<CrystallineConflictMatch> {

    private List<CrystallineConflictMatch>? _overviewMatches;
    private int _overviewWins, _overviewPlayed;
    private bool _jobFiltersExpanded;
    private string _beforeSettings = "Matches";
    private CrystallineConflictMatch? _latestVisibleMatch;

    private readonly MatchSearchFilter _search;
    private readonly LeaderboardPage _leaderboard;
    private TimeSpan _averageDuration;
    private readonly CrystallineConflictMatchList _matches;
    private readonly CrystallineConflictSummary _summary;
    private readonly CrystallineConflictRecords _records;
    private readonly CrystallineConflictPlayerList _players;
    private readonly CrystallineConflictJobList _jobs;
    private readonly CrystallineConflictPvPProfile _profile;
    private readonly CrystallineConflictRankGraph _credit;

    internal CCTrackerWindow(Plugin plugin) : base(plugin, plugin.CCStatsEngine, plugin.Configuration.CCWindowConfig, ChineseText.Label("Crystalline Conflict Tracker")) {
        //SizeConstraints = new WindowSizeConstraints {
        //    MinimumSize = new Vector2(425, 400),
        //    MaximumSize = new Vector2(5000, 5000)
        //};
        //Flags = Flags | ImGuiWindowFlags.NoScrollbar;

        Size = new Vector2(1120, 800);
        SizeCondition = ImGuiCond.FirstUseEver;

        _leaderboard = new(plugin);
        var refreshAction = () => Refresh();

        var playerFilter = new OtherPlayerFilter(plugin, refreshAction);

        _search = new MatchSearchFilter(plugin, refreshAction, WindowConfig.MatchFilters.MatchSearchFilter);
        MatchFilters.Add(_search);
        MatchFilters.Add(new MatchTypeFilter(plugin, refreshAction, WindowConfig.MatchFilters.MatchTypeFilter));
        MatchFilters.Add(new TierFilter(plugin, refreshAction));
        MatchFilters.Add(new ArenaFilter(plugin, refreshAction));
        MatchFilters.Add(new TimeFilter(plugin, refreshAction, WindowConfig.MatchFilters.TimeFilter));
        MatchFilters.Add(new LocalPlayerFilter(plugin, refreshAction, WindowConfig.MatchFilters.LocalPlayerFilter));
        MatchFilters.Add(new LocalPlayerJobFilter(plugin, refreshAction, WindowConfig.MatchFilters.LocalPlayerJobFilter));
        MatchFilters.Add(playerFilter);
        MatchFilters.Add(new ResultFilter(plugin, refreshAction));
        MatchFilters.Add(new DurationFilter(plugin, refreshAction));
        MatchFilters.Add(new BookmarkFilter(plugin, refreshAction));
        MatchFilters.Add(new TagFilter(plugin, refreshAction));
        MatchFilters.Add(new MiscFilter(plugin, refreshAction, WindowConfig.MatchFilters.MiscFilter));

        _matches = new(plugin);
        Tabs.Add(_matches);
        _summary = new(plugin);
        Tabs.Add(_summary);
        _records = new(plugin);
        Tabs.Add(_records);
        _jobs = new(plugin, WindowConfig.JobStatFilters.StatSourceFilter, playerFilter);
        Tabs.Add(_jobs);
        _players = new(plugin, WindowConfig.PlayerStatFilters.StatSourceFilter, WindowConfig.PlayerStatFilters.MinMatchFilter, null, playerFilter);
        Tabs.Add(_players);
        _credit = new(plugin);
        Tabs.Add(_credit);
        _profile = new(plugin);

        JobStatFilters.Add(_jobs.StatSourceFilter);
        PlayerStatFilters.Add(_players.StatSourceFilter);
        PlayerStatFilters.Add(_players.MinMatchFilter);
        PlayerStatFilters.Add(_players.PlayerQuickSearchFilter);
    }

    private void UpdateOverview() {
        var matches = Plugin.CCStatsEngine.Matches;
        if(ReferenceEquals(_overviewMatches, matches)) return;
        _overviewMatches = matches;
        _overviewWins = _overviewPlayed = 0;
        _latestVisibleMatch = matches.Where(x => x.IsCompleted && !x.IsDeleted && !x.IsQuarantined).MaxBy(x=>x.DutyStartTime);
        long ticks = 0;
        var timed = 0;
        foreach(var match in matches) {
            if(match.MatchDuration is { } duration && duration > TimeSpan.Zero) { ticks += duration.Ticks; timed++; }
            if(match.IsSpectated || match.MatchWinner is not (CrystallineConflictTeamName.Astra or CrystallineConflictTeamName.Umbra)) continue;
            _overviewPlayed++;
            if(match.IsWin) _overviewWins++;
        }
        _averageDuration = timed == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(ticks / timed);
    }

    internal void ShowSettings() {
        if(CurrentTab == "Settings") return;
        _beforeSettings = CurrentTab.Length == 0 ? "Matches" : CurrentTab;
        Plugin.WindowManager.ConfigWindow.OnOpen();
        ChangeTab("Settings");
    }

    internal void ReturnFromSettings() {
        Plugin.WindowManager.ConfigWindow.OnClose();
        ChangeTab(_beforeSettings);
    }

    public override void OnOpen() {
        base.OnOpen();
        // This queue is shared with initialization/import. Reopening must also repopulate views without changing saved filters.
        Plugin.DataQueue.QueueDataOperation(()=>Refresh(true));
    }

    public override void OnClose() {
        if(CurrentTab == "Settings") ReturnFromSettings();
        base.OnClose();
    }

    public override void PreDraw() {
        var screen = ImGui.GetMainViewport().WorkSize / ImGuiHelpers.GlobalScale;
        WindowChrome.Constrain(this);
        // Repair sizes saved by old child-window navigation, once per installation.
        if(Plugin.Configuration.WindowLayoutVersion < 3) {
            var available = ImGui.GetMainViewport().WorkSize / ImGuiHelpers.GlobalScale;
            Size = new Vector2(Math.Min(1120, Math.Max(300, available.X - 40)),
                Math.Min(800, Math.Max(250, available.Y - 60)));
            SizeCondition = ImGuiCond.Always;
            Position = ImGui.GetMainViewport().WorkPos + new Vector2(20, 20) * ImGuiHelpers.GlobalScale;
            PositionCondition = ImGuiCond.Always;
        }
        base.PreDraw();
    }

    public override void DrawInternal() {
        ImGui.SetWindowFontScale(1f);
        if(Plugin.Configuration.WindowLayoutVersion < 3) {
            SizeCondition = ImGuiCond.FirstUseEver;
            PositionCondition = ImGuiCond.FirstUseEver;
            Plugin.Configuration.WindowLayoutVersion = 3;
            Plugin.DataQueue.QueueDataOperation(Plugin.Configuration.Save);
        }
        UpdateOverview();
        if(CurrentTab == "") ChangeTab("Matches");
        ModernTheme.Brand(Plugin, "CCStats", "水晶冲突 · 战绩与数据中心");
        var sidebarWidth = ImGuiHelpers.GlobalScale * (ImGui.GetWindowWidth()/ImGuiHelpers.GlobalScale<900?124:152);
        using(var sidebar = ImRaii.Child("CCNavigation", new Vector2(sidebarWidth, 0), true)) {
            if(sidebar) {
                Navigation("Matches", "战绩查询");
                Navigation("Summary", "数据概览");
                Navigation("Credit", "段位趋势");
                Navigation("Leaderboard", "榜单");
                Navigation("Jobs", "职业分析");
                Navigation("Players", "玩家分析");
                Navigation("Records", "个人纪录");
                Navigation("Profile", "个人资料");
                ImGui.Separator();
                using(var iconFont = ImRaii.PushFont(UiBuilder.IconFont)) {
                    if(ModernTheme.Button(FontAwesomeIcon.Cog.ToIconString() + "###CCSettingsNav")) Plugin.WindowManager.OpenConfigWindow();
                }
                if(ImGui.IsItemHovered()) ImGui.SetTooltip("设置");
            }
        }
        ImGui.SameLine();
        using var workspace = ImRaii.Child("CCWorkspace", Vector2.Zero, false);
        if(!workspace) return;
        if(Plugin.LegacyImportPending && CurrentTab != "Settings") {
            ImGui.TextColored(ModernTheme.Accent, "发现 PvP Tracker 历史战绩");
            ImGui.TextWrapped("是否导入旧版的水晶冲突对局、时间线和玩家关联？仅复制缺少的记录，不改写原文件或覆盖已有战绩。");
            ImGui.Spacing();
            ImGui.TextWrapped("选择后将不再提示。无论现在是否导入，以后都可以在设置 → 界面与悬浮图标 → 旧版数据导入中操作。");
            ImGui.Spacing();
            if(ModernTheme.Button("导入旧战绩###AcceptLegacyImport")) Plugin.ChooseLegacyImport(true);
            ImGui.SameLine();
            if(ModernTheme.Button("暂不导入###DeclineLegacyImport")) Plugin.ChooseLegacyImport(false);
            return;
        }
        if(CurrentTab == "Settings") { Plugin.WindowManager.ConfigWindow.DrawEmbedded(ReturnFromSettings); return; }
        if(CurrentTab == "Leaderboard") { _leaderboard.Draw(); return; }
        if(CurrentTab == "Jobs") { DrawJobsPage(); return; }
        DrawOverviewCards();
        ImGui.Spacing();
        ImGui.SetNextItemWidth(Math.Max(120, ImGui.GetContentRegionAvail().X - 92 * ImGuiHelpers.GlobalScale));
        _search.Draw();
        ImGui.SameLine();
        if(ModernTheme.Button("清空###ClearMatchSearch")) _search.Clear();
        if(ImGui.IsItemHovered()) ImGui.SetTooltip("搜索支持多个关键词、双引号短语和 -排除词。");
        DrawQuickFilters();
        DrawFilters();
        ImGui.Spacing();
        switch(CurrentTab) {
            case "Summary": DrawPanel("SummaryContent", _summary.Draw, _summary.RefreshActive, _summary.RefreshProgress,_summary.RefreshFailures); break;
            case "Records": DrawPanel("RecordsContent", _records.Draw, _records.RefreshActive, _records.RefreshProgress,_records.RefreshFailures); break;
            case "Jobs": DrawPanel("JobsContent", _jobs.Draw, _jobs.RefreshActive, _jobs.RefreshProgress,_jobs.RefreshFailures); break;
            case "Players": DrawPanel("PlayersContent", _players.Draw, _players.RefreshActive, _players.RefreshProgress,_players.RefreshFailures); break;
            case "Credit": DrawPanel("CreditContent", _credit.Draw, _credit.RefreshActive, _credit.RefreshProgress,_credit.RefreshFailures); break;
            case "Profile": DrawPanel("ProfileContent", _profile.Draw, false, 0); break;
            default: DrawPanel("MatchesContent", _matches.Draw, _matches.RefreshActive, _matches.RefreshProgress,_matches.RefreshFailures); break;
        }
    }

    internal void PersistFilters() => SaveFilters();

    private void DrawJobsPage() {
        var scale=ImGuiHelpers.GlobalScale;
        // Keep the controls bounded; the grid owns the remaining height at every window size.
        using(var header=ImRaii.Table("JobPageHeader",2,ImGuiTableFlags.SizingStretchSame)) {
            if(header) {
                ImGui.TableNextColumn();
                if(ModernTheme.Button(_jobFiltersExpanded?"收起对局筛选###JobMatchFilters":"对局筛选###JobMatchFilters",new(-1,28*scale))) _jobFiltersExpanded=!_jobFiltersExpanded;
                ImGui.TableNextColumn();
                if(ModernTheme.Button(_jobs.ShowDetailedTable?"职业图标视图###JobView":"完整数据表###JobView",new(-1,28*scale))) _jobs.ShowDetailedTable=!_jobs.ShowDetailedTable;
            }
        }
        if(_jobFiltersExpanded) {
            using var filters=ImRaii.Child("JobMatchFiltersPanel",new(0,Math.Min(240*scale,ImGui.GetContentRegionAvail().Y*.35f)),true);
            if(filters) {
                ImGui.SetNextItemWidth(-1); _search.Draw();
                DrawQuickFilters(); ImGui.Separator(); DrawFilterFields();
            }
        }
        ImGui.Spacing();
        DrawPanel("JobCardsContent",_jobs.DrawCards,_jobs.RefreshActive,_jobs.RefreshProgress,_jobs.RefreshFailures);
    }

    private void Navigation(string key, string caption) {
        if(ModernTheme.NavigationItem(caption + "###CCNav" + key, CurrentTab == key,
            new Vector2(0, 31f * ImGuiHelpers.GlobalScale))) {
            if(CurrentTab == "Settings") Plugin.WindowManager.ConfigWindow.OnClose();
            ChangeTab(key);
        }
    }

    private void DrawOverviewCards() {
        if(ImGui.GetContentRegionAvail().X<640*ImGuiHelpers.GlobalScale) {
            ImGui.TextWrapped($"筛选比赛 {_overviewMatches?.Count??0:N0}  ·  有效胜率 {(_overviewPlayed>0?((double)_overviewWins/_overviewPlayed).ToString("P1"):"—")}");
            ImGui.TextDisabled($"{_overviewWins:N0} 胜 / {_overviewPlayed-_overviewWins:N0} 负  ·  平均时长 {StatDisplay.Duration(_averageDuration.TotalSeconds,_averageDuration>TimeSpan.Zero)}");
            return;
        }
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var width = Math.Max(40, (ImGui.GetContentRegionAvail().X - 3 * gap) / 4);
        ModernTheme.Card("MetricMatches", "筛选比赛", (_overviewMatches?.Count ?? 0).ToString("N0"), width, ModernTheme.Accent);
        ImGui.SameLine();
        ModernTheme.Card("MetricWinRate", "有效胜率", _overviewPlayed == 0 ? "—" : ((double)_overviewWins / _overviewPlayed).ToString("P1"), width, ModernTheme.Gold);
        ImGui.SameLine();
        ModernTheme.Card("MetricWins", "胜 / 负", $"{_overviewWins} / {_overviewPlayed - _overviewWins}", width, ModernTheme.Accent);
        ImGui.SameLine();
        ModernTheme.Card("MetricDuration", "平均时长", _averageDuration == TimeSpan.Zero ? "—" : _averageDuration.ToString(@"m\:ss"), width, ModernTheme.Accent);
    }

    private void DrawQuickFilters() {
        var scale=ImGuiHelpers.GlobalScale;
        var mode=MatchFilters.OfType<MatchTypeFilter>().First();
        var modes=new[] { ("全部",(CrystallineConflictMatchType?)null),("练习赛",(CrystallineConflictMatchType?)CrystallineConflictMatchType.Casual),
            ("段位赛",(CrystallineConflictMatchType?)CrystallineConflictMatchType.Ranked),("自定赛",(CrystallineConflictMatchType?)CrystallineConflictMatchType.Custom) };
        var width=Math.Max(1,(ImGui.GetContentRegionAvail().X-3*ImGui.GetStyle().ItemSpacing.X)/4);
        for(var i=0;i<modes.Length;i++) {
            if(i>0)ImGui.SameLine();
            var item=modes[i];
            var selected=item.Item2==null?mode.FilterState.Values.All(x=>x):mode.FilterState.All(x=>x.Value==(x.Key==item.Item2));
            using var tint=ImRaii.PushColor(ImGuiCol.Button,selected?ModernTheme.Selection:ModernTheme.ButtonBackground);
            if(ModernTheme.Button(item.Item1+"###CCQuick"+item.Item1,new(width,28*scale))) {
                foreach(var key in mode.FilterState.Keys.ToArray()) mode.FilterState[key]=item.Item2==null || key==item.Item2;
                mode.AllSelected=item.Item2==null;
                _=Task.Run(()=>Refresh());
            }
        }
        var bookmark=MatchFilters.OfType<BookmarkFilter>().First();
        var onlyFavorites=bookmark.BookmarkedOnly;
        ImGui.AlignTextToFramePadding();
        if(ImGui.Checkbox("仅收藏###CCQuickFavorites",ref onlyFavorites)) { bookmark.BookmarkedOnly=onlyFavorites; _=Task.Run(()=>Refresh()); }
        ImGui.SameLine();
        using(var disabled=ImRaii.Disabled(_latestVisibleMatch==null)) {
            if(ModernTheme.Button("最近一场###CCLatestMatch",new(120*scale,32*scale))&&_latestVisibleMatch!=null) Plugin.WindowManager.OpenMatchDetailsWindow(_latestVisibleMatch);
        }
    }

    private void DrawPanel(string id, Action draw, bool refreshing, double progress, int failures=0) {
        using var panel = ImRaii.Child(id, Vector2.Zero, false);
        if(!panel) return;
        if(!refreshing&&failures>0) ImGui.TextWrapped($"有 {failures} 条记录未能完成统计，当前结果可能不完整；详情见日志。");
        try { draw(); }
        catch(Exception e) {
            if(!refreshing) {
                Plugin.Log.Error(e, "Modern content panel draw failed.");
                ImGui.TextDisabled("内容暂时无法显示，请查看插件日志。");
            }
        }
        finally { if(refreshing) ImGui.TextDisabled($"正在刷新… {progress:P0}"); }
    }
}
