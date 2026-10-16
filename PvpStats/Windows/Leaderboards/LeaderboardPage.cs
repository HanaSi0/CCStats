using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using PvpStats.Helpers;
using PvpStats.Interface;
using PvpStats.Localization;
using PvpStats.Leaderboards;
using System;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PvpStats.Windows.Leaderboards;

internal sealed class LeaderboardPage {
    private readonly RegionalLeaderboardService _service;
    private readonly Plugin _plugin;
    private int _sort, _page;
    private string _search = "", _viewSearch = "";
    private bool _pendingJump;
    private string? _scrollTo;
    private RankingSnapshot? _viewSnapshot;
    private RankingEntry[] _view = [];
    private int _viewSort = -1;
    internal LeaderboardPage(Plugin plugin) { _plugin = plugin; _service = plugin.Leaderboards; }
    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static long Number(string value, long fallback) {
        var m = Regex.Match(value, @"^\s*(\d[\d,]*)");
        return m.Success && long.TryParse(m.Groups[1].Value.Replace(",", ""), out var number) ? number : fallback;
    }
    internal void Draw() {
        var state = _service.State;
        var snapshot = state.Snapshot;
        ImGui.TextColored(ModernTheme.Accent, "水晶冲突 · 榜单");
        ImGui.Spacing();
        var selectedRegion = _service.SelectedRegion;
        var labels = RankingRegion.All.Select(r => r.Name).ToArray();
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if(ImGui.Combo("大区###RankingRegion", ref selectedRegion, labels, labels.Length)) {
            _service.SelectRegion(selectedRegion);
            _plugin.Configuration.LeaderboardRegion = selectedRegion;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
            _page = 0;
            _pendingJump = false;
            _scrollTo = null;
            state = _service.State; snapshot = state.Snapshot;
        }
        var region = RankingRegion.All[selectedRegion];
        ImGui.SameLine();
        using(var disabled = ImRaii.Disabled(state.Busy)) {
            if(ModernTheme.Button("手动抓取###CheckRankingDate")) _ = Task.Run(() => _service.CheckDateAsync(selectedRegion,force:true));
        }
        if(ImGui.IsItemHovered()) ImGui.SetTooltip("重新抓取当前大区完整榜单（间隔至少 60 秒）。\n"+state.Message);
        if(state.Busy && _service.CanCancel) {
            ImGui.SameLine();
            if(ModernTheme.Button("取消抓取###CancelRanking")) _service.Cancel();
        }
        var date = snapshot?.OfficialDate?.ToString("yyyy-MM-dd") ?? "—";
        ImGui.TextUnformatted($"{region.Name}  ·  {date}  ·  {snapshot?.Entries.Length ?? 0} 名玩家");
        var failed = state.Message.Contains("失败") || state.Message.Contains("超时") || state.Message.Contains("取消");
        var status = state.Busy ? "更新中…" : failed ? "检查未完成" : state.Message.Contains("一致") ? "已是最新" : snapshot == null ? "等待检查" : "已缓存";
        if(ImGui.GetContentRegionAvail().X > 180 * ImGuiHelpers.GlobalScale) ImGui.SameLine();
        ImGui.TextDisabled("来源");
        if(ImGui.IsItemHovered()) ImGui.SetTooltip(status + "\n" + state.Message + (snapshot != null ? "\n" + snapshot.RootUrl + "\n" + snapshot.Pages.FirstOrDefault()?.Season : ""));
        if(snapshot == null) {
            ImGui.Separator();
            ImGui.TextWrapped($"暂无{region.Name}榜单缓存。检查完成后将在此显示。");
            return;
        }
        ImGui.Separator();
        ImGui.SetNextItemWidth(340 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("###RankingSearch", "搜索玩家、所属世界…", ref _search, 256);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        var sorts = new[] { "官方名次", "积分从高到低", "胜场从高到低" };
        ImGui.Combo("###RankingSort", ref _sort, sorts, sorts.Length);
        var local=_plugin.ObjectTable.LocalPlayer;
        var localName=local?.Name.ToString()??"";
        var localWorld=local?.HomeWorld.Value.Name.ToString()??"";
        bool IsSelf(RankingEntry entry)=>RankingIdentity.Matches(entry,localName,localWorld);
        var self=snapshot.Entries.FirstOrDefault(IsSelf);
        using(var disabled=ImRaii.Disabled(self==null)) {
            if(ModernTheme.Button("跳转到我###JumpToMyRanking")) { _search=""; _viewSearch="\0"; _pendingJump=true; }
        }
        ImGui.SameLine();
        ImGui.TextDisabled(local==null?"登录角色后可定位自己的排名":self==null?"当前角色未出现在此大区已缓存榜单":$"我的排名：{self.Rank} · {self.CharacterName} · {localWorld}");
        if(!ReferenceEquals(snapshot, _viewSnapshot) || _viewSearch != _search || _viewSort != _sort) {
            var terms = MatchQuery.Parse(_search);
            var rows = snapshot.Entries.Where(e => MatchQuery.Matches(e.CharacterName + " " + e.World, terms));
            _view = (_sort switch {
                1 => rows.OrderByDescending(e => Number(e.Rating, long.MinValue)),
                2 => rows.OrderByDescending(e => Number(e.Victories, long.MinValue)),
                _ => rows.OrderBy(e => e.Scope).ThenBy(e => Number(e.Rank, long.MaxValue)),
            }).ToArray();
            _viewSnapshot = snapshot; _viewSearch = _search; _viewSort = _sort; _page = 0;
        }
        if(_pendingJump) {
            var index=Array.FindIndex(_view,IsSelf);
            if(index>=0) { _page=index/100; _scrollTo=_view[index].CharacterId; }
            _pendingJump=false;
        }
        var pageCount = Math.Max(1, (_view.Length + 99) / 100);
        _page = Math.Clamp(_page, 0, pageCount - 1);
        using(var table = ImRaii.Table("OfficialRankingTable", 8, ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable,
            new Vector2(0, Math.Max(180, ImGui.GetContentRegionAvail().Y - 65 * ImGuiHelpers.GlobalScale)))) {
            if(table) {
                foreach(var name in new[] { "名次", "玩家完整姓名", "服务器 / 数据中心", "段位", "积分 / 胜星", "胜场", "排名变化", "地区" })
                    ImGui.TableSetupColumn(name, ImGuiTableColumnFlags.WidthFixed, (name == "玩家完整姓名" ? 240 : name.StartsWith("服务器") ? 190 : 90) * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupScrollFreeze(2, 1); ImGui.TableHeadersRow();
                foreach(var entry in _view.Skip(_page * 100).Take(100)) {
                    ImGui.TableNextRow(); ImGui.TableNextColumn();
                    var isSelf=IsSelf(entry);
                    if(isSelf) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1,ImGui.GetColorU32(ModernTheme.Selection));
                    ImGui.TextUnformatted(Display(entry.Rank)+(isSelf?" · 我":""));
                    if(_scrollTo==entry.CharacterId) { ImGui.SetScrollHereY(.5f); _scrollTo=null; }
                    foreach(var value in new[] { entry.CharacterName, entry.World, ChineseText.Text(LodestoneParser.CanonicalTier(entry.Tier)) + (entry.Division.Length > 0 ? " " + entry.Division : ""), entry.Rating, entry.Victories, entry.RankChange, region.Name }) {
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(Display(value));
                        if(ImGui.IsItemHovered()) ImGui.SetTooltip(Display(value));
                    }
                }
            }
        }
        ImGui.TextDisabled($"筛选后 {_view.Length} 条  ·  第 {_page + 1}/{pageCount} 页"); ImGui.SameLine();
        using(var disabled = ImRaii.Disabled(_page == 0)) if(ModernTheme.Button("上一页###RankPrevious")) _page--;
        ImGui.SameLine();
        using(var disabled = ImRaii.Disabled(_page + 1 >= pageCount)) if(ModernTheme.Button("下一页###RankNext")) _page++;
    }
}
