using PvpStats.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using PvpStats.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PvpStats.Windows.Filter;

public enum StatSource {
    LocalPlayer,
    Teammate,
    Opponent,
    Spectated
}

public class StatSourceFilter : DataFilter, IEquatable<StatSourceFilter> {

    public override string Name => "Stat Source";
    internal bool AllSelected { get; set; }
    internal bool InheritFromPlayerFilter { get; set; }
    public Dictionary<StatSource, bool> FilterState { get; set; } = new();

    public static Dictionary<StatSource, string> FilterNames => new() {
        { StatSource.LocalPlayer, "Local Player" },
        { StatSource.Teammate, "Teammates" },
        { StatSource.Opponent, "Opponents" },
        { StatSource.Spectated, "Spectated Matches" }
    };

    public StatSourceFilter() {
        Initialize();
    }

    public StatSourceFilter(StatSourceFilter filter) {
        Initialize(filter);
    }

    internal StatSourceFilter(Plugin plugin, Func<Task> action, StatSourceFilter? filter = null) : base(plugin, action) {
        Initialize(filter);
    }

    private void Initialize(StatSourceFilter? filter = null) {
        FilterState = new() {
                {StatSource.LocalPlayer, true },
                {StatSource.Teammate, true },
                {StatSource.Opponent, true },
                {StatSource.Spectated, true },
            };

        if(filter is not null) {
            foreach(var category in filter.FilterState) {
                FilterState[category.Key] = category.Value;
            }
            InheritFromPlayerFilter = filter.InheritFromPlayerFilter;
        }
        UpdateAllSelected();
    }

    protected void UpdateAllSelected() {
        AllSelected = true;
        foreach(var category in FilterState) {
            AllSelected = AllSelected && category.Value;
        }
    }

    internal override void Draw() {
        using var table = ImRaii.Table("statSourceTable", 4, ImGuiTableFlags.NoClip);
        if(table) {
            ImGui.TableSetupColumn($"c1", ImGuiTableColumnFlags.WidthFixed, float.Min(ImGui.GetContentRegionAvail().X / 4, ImGuiHelpers.GlobalScale * 150f));
            ImGui.TableSetupColumn($"c2", ImGuiTableColumnFlags.WidthFixed, float.Min(ImGui.GetContentRegionAvail().X / 4, ImGuiHelpers.GlobalScale * 150f));
            ImGui.TableSetupColumn($"c3", ImGuiTableColumnFlags.WidthFixed, float.Min(ImGui.GetContentRegionAvail().X / 4, ImGuiHelpers.GlobalScale * 150f));
            ImGui.TableSetupColumn($"c4", ImGuiTableColumnFlags.WidthFixed, float.Min(ImGui.GetContentRegionAvail().X / 4, ImGuiHelpers.GlobalScale * 200f));
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            bool allSelected = AllSelected;
            if(ImGui.Checkbox(ChineseText.Label($"Select All##{GetHashCode()}"), ref allSelected)) {
                foreach(var key in FilterState.Keys.ToArray()) FilterState[key]=allSelected;
                AllSelected=allSelected;
                RequestRefresh();
            }
            ImGui.TableNextColumn();
            bool inheritFromPlayerFilter = InheritFromPlayerFilter;
            if(ImGui.Checkbox(ChineseText.Label($"Inherit from player filter##{GetHashCode()}"), ref inheritFromPlayerFilter)) {
                InheritFromPlayerFilter=inheritFromPlayerFilter;
                RequestRefresh();
            }
            ImGuiHelper.HelpMarker(ChineseText.Text("Will only include stats for players who match all conditions of the player filter."));
            ImGui.TableNextRow();

            foreach(var category in FilterState.ToArray()) {
                ImGui.TableNextColumn();
                bool filterState = category.Value;
                if(ImGui.Checkbox(ChineseText.Label($"{FilterNames[category.Key]}##{GetHashCode()}"), ref filterState)) {
                    FilterState[category.Key]=filterState;
                    UpdateAllSelected();
                    RequestRefresh();
                }
            }
        }
    }

    private void RequestRefresh() {
        _=Task.Run(async()=> {
            try { await Refresh(); _plugin?.WindowManager.CCTrackerWindow.PersistFilters(); }
            catch(Exception e) { _plugin?.Log.Error(e,"Statistics source refresh failed."); }
        });
    }

    internal bool Includes(bool spectated,bool local,bool teammate) => new SourceSelection(
        FilterState.GetValueOrDefault(StatSource.LocalPlayer),FilterState.GetValueOrDefault(StatSource.Teammate),
        FilterState.GetValueOrDefault(StatSource.Opponent),FilterState.GetValueOrDefault(StatSource.Spectated)).Includes(spectated,local,teammate);

    public bool Equals(StatSourceFilter? other) {
        return FilterState.All(x => x.Value == other?.FilterState[x.Key]) && (InheritFromPlayerFilter == other?.InheritFromPlayerFilter);
    }
}
