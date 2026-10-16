using Dalamud.Bindings.ImGui;
using System;
using System.Threading.Tasks;

namespace PvpStats.Windows.Filter;

public sealed class MatchSearchFilter : DataFilter {
    public override string Name => "搜索";
    public string SearchText { get; set; } = "";
    public MatchSearchFilter() { }
    internal MatchSearchFilter(Plugin plugin, Func<Task> action, MatchSearchFilter? saved) : base(plugin, action) {
        SearchText = saved?.SearchText ?? "";
    }
    internal override void Draw() {
        var text = SearchText;
        if(ImGui.InputTextWithHint("###CCMatchSearch", "搜索玩家、地图、职业、标签或日期……", ref text, 256)) {
            SearchText = text;
            _ = Task.Run(Refresh);
        }
    }
    internal void Clear() { SearchText = ""; _ = Task.Run(Refresh); }
}
