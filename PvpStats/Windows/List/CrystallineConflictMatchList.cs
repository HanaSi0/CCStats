using PvpStats.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using PvpStats.Helpers;
using PvpStats.Types.Match;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading.Tasks;

namespace PvpStats.Windows.List;
internal class CrystallineConflictMatchList : MatchList<CrystallineConflictMatch> {

    private volatile bool _sortDirty = true;
    public override string Name => "CC Matches";
    protected override bool ShowHeader { get; set; } = true;
    protected override bool DynamicColumns { get; set; } = false;
    protected override ImGuiTableFlags TableFlags { get; set; } = ImGuiTableFlags.Hideable | ImGuiTableFlags.Sortable
        | ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV
        | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY;

    protected override List<ColumnParams> Columns { get; set; } = new() {
        new ColumnParams{Name = "Start Time", Id = 1, Flags = ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending, Width = 145f },
        new ColumnParams{Name = "Arena", Id = 2, Flags = ImGuiTableColumnFlags.WidthFixed, Width = 130f },
        new ColumnParams{Name = "Job", Id = 3, Flags = ImGuiTableColumnFlags.WidthFixed, Width = 105f, Priority = 1 },
        new ColumnParams{Name = "Queue", Id = 4, Flags = ImGuiTableColumnFlags.WidthFixed, Width = 75f },
        new ColumnParams{Name = "Duration", Id = 5, Flags = ImGuiTableColumnFlags.WidthFixed, Width = 65f, Priority = 2 },
        new ColumnParams{Name = "Result", Id = 6, Flags = ImGuiTableColumnFlags.WidthFixed, Width = 65f },
        new ColumnParams{Name = "RankAfter", Id = 7, Flags = ImGuiTableColumnFlags.WidthFixed, Width = 150f, Priority = 3 },
        new ColumnParams{Name = "Tags", Id = 8, Flags = ImGuiTableColumnFlags.WidthStretch, Width = 90f, Priority = 4 },
    };

    public CrystallineConflictMatchList(Plugin plugin) : base(plugin, plugin.CCCache, plugin.CCStatsEngine.RefreshLock) {
    }

    protected override Task RefreshInner(List<CrystallineConflictMatch> matches, List<CrystallineConflictMatch> additions, List<CrystallineConflictMatch> removals) {
        var result = base.RefreshInner(matches, additions, removals);
        _sortDirty = true;
        return result;
    }

    protected override void PostColumnSetup() {
        ImGui.TableSetupScrollFreeze(1, 1);
        var specs = ImGui.TableGetSortSpecs();
        if(specs.SpecsCount == 0 || (!specs.SpecsDirty && !_sortDirty)) return;
        Func<CrystallineConflictMatch, object> key = specs.Specs.ColumnUserID switch {
            2 => m => m.Arena.HasValue ? MatchHelper.GetArenaName(m.Arena.Value) : "",
            3 => m => ChineseText.EnumName(m.LocalPlayerTeamMember?.Job),
            4 => m => (int)m.MatchType,
            5 => m => m.MatchDuration?.Ticks ?? 0L,
            6 => m => m.IsSpectated ? 2 : m.MatchWinner is not (CrystallineConflictTeamName.Astra or CrystallineConflictTeamName.Umbra) ? 3 : m.IsWin ? 0 : 1,
            7 => m => m.PostMatch?.RankAfter?.TotalCredit ?? int.MinValue,
            8 => m => m.Tags,
            _ => m => m.DutyStartTime,
        };
        DataModel = specs.Specs.SortDirection == ImGuiSortDirection.Ascending
            ? DataModel.OrderBy(key).ToList() : DataModel.OrderByDescending(key).ToList();
        GoToPage(0);
        specs.SpecsDirty = false;
        _sortDirty = false;
    }

    public override void DrawListItem(CrystallineConflictMatch item) {
        ImGui.SameLine(0f * ImGuiHelpers.GlobalScale);
        if(item.IsBookmarked) {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(_plugin.Configuration.Colors.Favorite - new Vector4(0f, 0f, 0f, 0.7f)));
        }
        ImGui.Text($"{item.DutyStartTime.ToLocalTime():yyyy-MM-dd HH:mm}");
        ImGui.TableNextColumn();
        if(item.Arena != null) {
            ImGui.Text($"{MatchHelper.GetArenaName((CrystallineConflictMap)item.Arena)}");
        }
        ImGui.TableNextColumn();
        if(!item.IsSpectated) {
            var localPlayerJob = item.LocalPlayerTeamMember?.Job;
            ImGuiHelper.CenterAlignCursor(ChineseText.EnumName(localPlayerJob));
            ImGui.TextColored(_plugin.Configuration.GetJobColor(localPlayerJob), ChineseText.EnumName(localPlayerJob));
        }
        ImGui.TableNextColumn();
        ImGui.Text(ChineseText.EnumName(item.MatchType));
        ImGui.TableNextColumn();
        var timeSpanString = ImGuiHelper.GetTimeSpanString(item.MatchDuration ?? TimeSpan.Zero);
        ImGuiHelper.DrawNumericCell(timeSpanString, -10f);
        ImGui.TableNextColumn();
        bool noWinner = item.MatchWinner is not (CrystallineConflictTeamName.Astra or CrystallineConflictTeamName.Umbra);
        bool isWin = item.MatchWinner == item.LocalPlayerTeam?.TeamName;
        bool isSpectated = item.LocalPlayerTeam is null;
        Vector4 color;
        string resultText;
        if(isSpectated) {
            color = ImGuiColors.DalamudWhite;
            resultText = "观战";
        } else {
            color = isWin ? _plugin.Configuration.Colors.Win : _plugin.Configuration.Colors.Loss;
            color = noWinner ? _plugin.Configuration.Colors.Other : color;
            resultText = isWin ? "胜利" : "失败";
            resultText = noWinner ? ChineseText.Text("Unknown") : resultText;
        }
        ImGuiHelper.CenterAlignCursor(resultText);
        ImGui.TextColored(color, resultText);
        ImGui.TableNextColumn();
        if(item.MatchType == CrystallineConflictMatchType.Ranked) {
            ImGui.Text(item.PostMatch?.RankAfter?.ToString() ?? "");
        }
        //ImGui.TableNextColumn();
        //ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(item.Tags);
    }

    protected override string CSVRow(CrystallineConflictMatch match) {
        string csv = "";
        csv += match.DutyStartTime + ",";
        csv += (match.Arena != null ? MatchHelper.GetArenaName((CrystallineConflictMap)match.Arena) : "") + ",";
        csv += (!match.IsSpectated ? match.LocalPlayerTeamMember!.Job : "") + ",";
        csv += match.MatchType + ",";
        csv += match.MatchDuration + ",";
        csv += (match.IsWin ? "WIN" : match.MatchWinner != null ? "LOSS" : "???") + ",";
        csv += (match.MatchType == CrystallineConflictMatchType.Ranked && match.PostMatch != null ? match.PostMatch.RankAfter?.ToString() : "") + ",";
        csv += "\n";
        return csv;
    }

    //public override void OpenItemDetail(CrystallineConflictMatch item) {
    //    _plugin.DataQueue.QueueDataOperation(() => {
    //        _plugin.WindowManager.OpenMatchDetailsWindow(item);
    //    });
    //}
}
