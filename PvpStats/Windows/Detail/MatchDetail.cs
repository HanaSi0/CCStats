using PvpStats.Localization;
using PvpStats.Interface;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using PvpStats.Helpers;
using PvpStats.Services.DataCache;
using PvpStats.Types.Match;
using PvpStats.Utility;
using System.Numerics;

namespace PvpStats.Windows.Detail;
internal abstract class MatchDetail<T> : Window where T : PvpMatch {

    protected readonly Plugin Plugin;
    protected readonly MatchCacheService<T> Cache;
    protected readonly T Match;

    protected DataQueue RefreshQueue { get; private set; } = new();

    protected string CSV = "";
    protected bool ShowPercentages;
    protected bool ShowTeamRows = true;
    protected string CurrentTab = "";

    private bool _popupOpen = false;
    private Vector2 _lastWindowSize;

    public MatchDetail(Plugin plugin, MatchCacheService<T> cache, T match) : base($"比赛详情###Match Details: {match.Id}") {
        Plugin = plugin;
        Cache = cache;
        Match = match;

        PositionCondition = ImGuiCond.Appearing;
        CollapsedCondition = ImGuiCond.Appearing;
        Position = new Vector2(0, 0);
        Collapsed = false;
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(650, 400),
            MaximumSize = new Vector2(5000, 5000)
        };
        Flags |= ImGuiWindowFlags.NoSavedSettings;
        //if(!plugin.Configuration.ResizeableMatchWindow) {
        //    Flags |= ImGuiWindowFlags.AlwaysAutoResize;
        //}
    }

    protected abstract string BuildCSV();

    public override void PreDraw() => WindowChrome.Constrain(this);
    public override void OnClose() {
        if(_lastWindowSize.X>0) {
            Plugin.Configuration.MatchDetailSize=_lastWindowSize;
            Plugin.DataQueue.QueueDataOperation(Plugin.Configuration.Save);
        }
        Plugin.WindowManager.RemoveWindow(this);
    }

    public override void Draw() {
        ImGui.SetWindowFontScale(1f);
        _lastWindowSize=ImGui.GetWindowSize()/Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;

    }

    protected void DrawFunctions() {
        var scale=Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
        using(var font=ImRaii.PushFont(UiBuilder.IconFont)) {
            if(ModernTheme.Button(FontAwesomeIcon.Copy.ToIconString()+"###CopyMatch",new Vector2(28*scale))) ImGui.SetClipboardText(CSV);
        }
        ImGuiHelper.WrappedTooltip("复制对局数据");
        ImGui.SameLine();
        using(_ = ImRaii.PushFont(UiBuilder.IconFont)) {
            var color = Match.IsBookmarked ? Plugin.Configuration.Colors.Favorite : ImGuiColors.DalamudWhite;
            using(_ = ImRaii.PushColor(ImGuiCol.Text, color)) {
                if(ModernTheme.Button($"{FontAwesomeIcon.Star.ToIconString()}##--FavoriteMatch",new Vector2(28*scale))) {
                    Match.IsBookmarked = !Match.IsBookmarked;
                    Plugin.DataQueue.QueueDataOperation(async () => {
                        await Cache.UpdateMatch(Match);
                    });
                }
            }
        }
        ImGuiHelper.WrappedTooltip(ChineseText.Text("Favorite match"));

        ImGui.SameLine();
        using(var font = ImRaii.PushFont(UiBuilder.IconFont)) {
            var color = !Match.Tags.IsNullOrEmpty() ? Plugin.Configuration.Colors.Favorite : ImGuiColors.DalamudWhite;
            using(_ = ImRaii.PushColor(ImGuiCol.Text, color)) {
                if(ModernTheme.Button($"{FontAwesomeIcon.Tags.ToIconString()}##--Tags",new Vector2(28*scale))) {
                    ImGui.OpenPopup($"{Match.Id}--TagsPopup");
                }
            }
        }
        ImGuiHelper.WrappedTooltip(ChineseText.Text("Set tags"));
        Plugin.WindowManager.SetTagsPopup(Match, Cache, ref _popupOpen);
    }

    protected void SetWindowSize(Vector2 size) {
        SizeCondition = ImGuiCond.Always;
        Size = size;
    }
}
