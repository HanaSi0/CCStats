using PvpStats.Localization;
using PvpStats.Interface;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using PvpStats.Helpers;
using PvpStats.Settings;
using PvpStats.Types.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace PvpStats.Windows;
internal class ConfigWindow : Window {

    private int _section;
    private ThemeConfiguration _savedTheme = new();
    private string _themeStatus = "";
    private Plugin _plugin;
    private PlayerAliasLink _newManualLink;
    private string[] _linksVerbCombo = new[] { ChineseText.Text("IS"), ChineseText.Text("IS NOT") };
    private List<PlayerAliasLink> ManualLinks { get; set; } = new();
    private List<PlayerAliasLink> AutoLinks { get; set; } = new();
    protected SemaphoreSlim RefreshLock { get; init; } = new SemaphoreSlim(1);

    private float _saveOpacity = 0f;
    private bool _ipcUpdateInProgress = false;

    public ConfigWindow(Plugin plugin) : base(ChineseText.Label("PvP Tracker Settings")) {
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(800, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Size = new Vector2(980, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        _plugin = plugin;
        _newManualLink = new();
        //_plugin.DataQueue.QueueDataOperation(Refresh);
    }

    internal async Task Refresh() {
        _newManualLink = new();
        List<PlayerAliasLink> flattenedList = new();
        foreach(var playerLink in _plugin.PlayerLinksService.ManualPlayerLinksCache) {
            foreach(var linkedAlias in playerLink.LinkedAliases) {
                flattenedList.Add(new() {
                    CurrentAlias = playerLink.CurrentAlias,
                    IsUnlink = playerLink.IsUnlink,
                    LinkedAliases = [linkedAlias]
                });
            }
        }
        try {
            await RefreshLock.WaitAsync();
            ManualLinks = flattenedList;
            AutoLinks = _plugin.PlayerLinksService.AutoPlayerLinksCache.OrderBy(x => x.CurrentAlias).ToList();
        } finally {
            RefreshLock.Release();
        }
        //ManualLinks = _plugin.Storage.GetManualLinks().Query().ToList();
    }

    public override void OnOpen() {
        _savedTheme = _plugin.Configuration.Theme.Copy();
        _themeStatus = "";
        _plugin.DataQueue.QueueDataOperation(Refresh);
        base.OnOpen();
    }

    public override void OnClose() {
        _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        base.OnClose();
    }

    public override void Draw() {
        ModernTheme.Brand(_plugin, "设置", "界面、悬浮入口、玩家关联与性能");
        using(var navigation = ImRaii.Child("SettingsNavigation", new Vector2(140f * ImGuiHelpers.GlobalScale, 0), true)) {
            if(navigation) {
                var sections = new[] { "界面与悬浮图标", "玩家关联", "性能与记录", "主题与配色" };
                for(var i = 0; i < sections.Length; i++)
                    if(ImGui.Selectable(sections[i] + "###SettingsSection" + i, _section == i)) _section = i;
            }
        }
        ImGui.SameLine();
        using var content = ImRaii.Child("SettingsContent", Vector2.Zero, true);
        if(!content) return;
        switch(_section) {
            case 1: DrawPlayerLinkSettings(); break;
            case 2: DrawPerformanceSettings(); break;
            case 3: DrawThemeSettings(); break;
            default: DrawInterfaceSettings(); break;
        }
    }

    private void DrawThemeSettings() {
        ImGui.TextColored(ModernTheme.Accent, "主题与配色");
        ImGui.TextWrapped("颜色修改即时预览，关闭设置时自动保存；也可以主动保存或还原到上次保存的主题。");
        ImGui.Spacing();
        var presets = new[] { "水晶冰蓝", "暮色紫晶", "翠绿森林", "清晨浅色" };
        for(var i = 0; i < presets.Length; i++) {
            if(i > 0) ImGui.SameLine();
            if(ImGui.Button(presets[i] + "###ThemePreset" + i)) {
                _plugin.Configuration.Theme = ThemeConfiguration.Preset(i);
                _themeStatus = "已预览：" + presets[i];
            }
        }
        ImGui.Separator();
        var labels = new[] {
            (nameof(ThemeConfiguration.Background), "窗口背景"), (nameof(ThemeConfiguration.Panel), "面板与卡片"),
            (nameof(ThemeConfiguration.Surface), "输入框背景"), (nameof(ThemeConfiguration.Border), "边框与分隔线"),
            (nameof(ThemeConfiguration.Text), "主要文字"), (nameof(ThemeConfiguration.Muted), "次要文字"),
            (nameof(ThemeConfiguration.Accent), "主题强调色"), (nameof(ThemeConfiguration.Highlight), "重点数值"),
            (nameof(ThemeConfiguration.Button), "按钮背景"), (nameof(ThemeConfiguration.Selection), "选中背景"),
            (nameof(ThemeConfiguration.TableHeader), "表格表头"), (nameof(ThemeConfiguration.TableAlternate), "交替行背景"),
        };
        var theme = _plugin.Configuration.Theme;
        foreach(var (name, label) in labels) {
            var property = typeof(ThemeConfiguration).GetProperty(name)!;
            var color = (Vector4)property.GetValue(theme)!;
            ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
            if(ImGui.ColorEdit4(label + "###ThemeColor" + name, ref color,
                ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.DisplayHex)) {
                color.W = name == nameof(ThemeConfiguration.Background) ? theme.Background.W : 1f;
                property.SetValue(theme, color);
                _themeStatus = "正在预览自定义配色";
            }
        }
        var opacity = theme.Background.W;
        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
        if(ImGui.SliderFloat("窗口不透明度###ThemeOpacity", ref opacity, .35f, 1f, "%.2f")) {
            var background = theme.Background;
            background.W = opacity;
            theme.Background = background;
            _themeStatus = "正在预览自定义配色";
        }
        ImGui.Spacing();
        ModernTheme.Card("ThemePreviewCard", "数值卡片预览", "62.5%", 240f * ImGuiHelpers.GlobalScale, theme.Highlight);
        ImGui.Spacing();
        if(ImGui.Button("保存主题###SaveTheme")) {
            theme.Normalize();
            _savedTheme = theme.Copy();
            _plugin.Configuration.Save();
            _themeStatus = "主题已保存";
        }
        ImGui.SameLine();
        if(ImGui.Button("还原主题###RevertTheme")) {
            _plugin.Configuration.Theme = _savedTheme.Copy();
            _themeStatus = "已还原到打开设置或上次保存时的主题";
        }
        ImGui.SameLine();
        if(ImGui.Button("恢复默认主题###ResetTheme")) {
            _plugin.Configuration.Theme = new();
            _themeStatus = "已恢复水晶冰蓝主题";
        }
        ImGui.TextDisabled(_themeStatus);
        ImGui.Separator();
        ImGui.TextWrapped("胜负、职业、收藏和队伍颜色可在“界面与悬浮图标”的颜色区域单独调整。主题不会修改这些统计颜色。");
    }

    private void DrawInterfaceSettings() {
        ImGui.TextColored(_plugin.Configuration.Colors.Header, "悬浮图标");
        var showLauncher = _plugin.Configuration.ShowFloatingLauncher;
        if(ImGui.Checkbox("显示悬浮图标###ShowFloatingLauncher", ref showLauncher)) {
            _plugin.Configuration.ShowFloatingLauncher = showLauncher;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        var launcherSize = _plugin.Configuration.FloatingLauncherSize;
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        if(ImGui.SliderFloat("图标大小###FloatingLauncherSize", ref launcherSize, 32, 96, "%.0f")) {
            _plugin.Configuration.FloatingLauncherSize = launcherSize;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        if(ImGui.Button("重置悬浮图标位置###ResetFloatingLauncher")) {
            _plugin.WindowManager.Launcher.ResetPosition();
        }
        ImGui.TextDisabled("左键打开记录器，按住右键拖动；松开右键后保存位置。");
        ImGui.Separator();


        ImGui.TextColored(_plugin.Configuration.Colors.Header, ChineseText.Text("General"));

        int stretchColumns = _plugin.Configuration.StretchScoreboardColumns ?? false ? 1 : 0;
        string[] columnStyles = [ChineseText.Text("Fixed"), ChineseText.Text("Stretch")];
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 4f);
        if(ImGui.Combo(ChineseText.Label("Column styles"), ref stretchColumns, columnStyles, columnStyles.Length)) {
            bool isStretch = Convert.ToBoolean(stretchColumns);
            _plugin.Configuration.StretchScoreboardColumns = isStretch;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        ImGui.Separator();

        ImGui.TextColored(_plugin.Configuration.Colors.Header, ChineseText.Text("Tracker Window"));

        var filterHeight = (int)_plugin.Configuration.CCWindowConfig.FilterHeight;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 2f);
        if(ImGui.SliderInt(ChineseText.Label("Filter child height"), ref filterHeight, 100, 500)) {
            _plugin.Configuration.CCWindowConfig.FilterHeight = (uint)filterHeight;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        bool filterHeightAdjust = _plugin.Configuration.AdjustWindowHeightOnFilterCollapse;
        if(ImGui.Checkbox(ChineseText.Label("Offset window size when filters hidden"), ref filterHeightAdjust)) {
            _plugin.Configuration.AdjustWindowHeightOnFilterCollapse = filterHeightAdjust;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        ImGuiHelper.HelpMarker("The height of the window will be offset by the height of the filter child whenever filters are shown or hidden.", true, true);
        bool minimize = _plugin.Configuration.MinimizeWindow;
        if(ImGui.Checkbox(ChineseText.Label("Shrink window on collapse"), ref minimize)) {
            _plugin.Configuration.MinimizeWindow = minimize;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        bool minimizeDir = _plugin.Configuration.MinimizeDirectionLeft;
        if(ImGui.Checkbox(ChineseText.Label("Anchor left-side of window on shrink"), ref minimizeDir)) {
            _plugin.Configuration.MinimizeDirectionLeft = minimizeDir;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        ImGuiHelper.HelpMarker("Only applies to previous setting. Otherwise anchors on the right-side.", true, true);
        bool saveTabSize = _plugin.Configuration.PersistWindowSizePerTab;
        if(ImGui.Checkbox(ChineseText.Label("Save window size per tab"), ref saveTabSize)) {
            _plugin.Configuration.PersistWindowSizePerTab = saveTabSize;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        bool resizeLeft = _plugin.Configuration.ResizeWindowLeft;
        if(ImGui.Checkbox(ChineseText.Label("Resize window leftwards on tab switch"), ref resizeLeft)) {
            _plugin.Configuration.ResizeWindowLeft = resizeLeft;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        bool colorScale = _plugin.Configuration.ColorScaleStats;
        if(ImGui.Checkbox(ChineseText.Label("Color scale stat values"), ref colorScale)) {
            _plugin.Configuration.ColorScaleStats = colorScale;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        ImGui.Separator();

        ImGui.TextColored(_plugin.Configuration.Colors.Header, ChineseText.Text("Match Details Window"));

        //bool resizeableWindow = _plugin.Configuration.ResizeableMatchWindow;
        //if(ImGui.Checkbox("Make window resizeable", ref resizeableWindow)) {
        //    _plugin.Configuration.ResizeableMatchWindow = resizeableWindow;
        //    _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        //}
        //ImGuiHelper.HelpMarker("Only affects Crystalline Conflict currently. Reopen windows to reflect changes.", true, true);

        bool showBackgroundImage = _plugin.Configuration.ShowBackgroundImage;
        if(ImGui.Checkbox(ChineseText.Label("Show background image"), ref showBackgroundImage)) {
            _plugin.Configuration.ShowBackgroundImage = showBackgroundImage;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        bool playerTeamLeft = _plugin.Configuration.LeftPlayerTeam;
        if(ImGui.Checkbox(ChineseText.Label("Always show player team on left"), ref playerTeamLeft)) {
            _plugin.Configuration.LeftPlayerTeam = playerTeamLeft;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        bool anchorTeamNames = _plugin.Configuration.AnchorTeamNames;
        if(ImGui.Checkbox(ChineseText.Label("Anchor team stats"), ref anchorTeamNames)) {
            _plugin.Configuration.AnchorTeamNames = anchorTeamNames;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        ImGuiHelper.HelpMarker("Team stat rows will not be affected by sorting.", true, true);

        //bool jobIconCells = _plugin.Configuration.JobIconCells ?? false;
        //if(ImGui.Checkbox("Show job icons in scoreboard", ref jobIconCells)) {
        //    _plugin.Configuration.JobIconCells = jobIconCells;
        //    _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        //}
        //ImGuiHelper.HelpMarker("Recommend increasing row height factor with this.", true, true);

        //var scoreboardRowPadding = _plugin.Configuration.ScoreboardRowPaddingFactor;
        //ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 2f);
        //if(ImGui.SliderFloat("Scoreboard row height factor", ref scoreboardRowPadding, 1f, 5f)) {
        //    _plugin.Configuration.ScoreboardRowPaddingFactor = scoreboardRowPadding;
        //    _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        //}

        var teamRowAlpha = _plugin.Configuration.TeamRowAlpha;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 2f);
        if(ImGui.SliderFloat(ChineseText.Label("Team row alpha"), ref teamRowAlpha, 0f, 1f)) {
            _plugin.Configuration.TeamRowAlpha = teamRowAlpha;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        ImGui.SameLine();
        using(var font = ImRaii.PushFont(UiBuilder.IconFont)) {
            if(ImGui.Button($"{FontAwesomeIcon.Undo.ToIconString()}###teamAlphaReset")) {
                _plugin.Configuration.TeamRowAlpha = new Configuration().TeamRowAlpha;
                _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
            }
        }
        ImGuiHelper.WrappedTooltip(ChineseText.Text("Reset"));
        var playerRowAlpha = _plugin.Configuration.PlayerRowAlpha;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 2f);
        if(ImGui.SliderFloat(ChineseText.Label("Player row alpha"), ref playerRowAlpha, 0f, 1f)) {
            _plugin.Configuration.PlayerRowAlpha = playerRowAlpha;
            _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        ImGui.SameLine();
        using(var font = ImRaii.PushFont(UiBuilder.IconFont)) {
            if(ImGui.Button($"{FontAwesomeIcon.Undo.ToIconString()}###playerAlphaReset")) {
                _plugin.Configuration.PlayerRowAlpha = new Configuration().PlayerRowAlpha;
                _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
            }
        }
        ImGuiHelper.WrappedTooltip(ChineseText.Text("Reset"));

        ImGui.Separator();
        ImGui.TextColored(_plugin.Configuration.Colors.Header, ChineseText.Text("Colors"));
        DrawColorSettings();
    }

    private void DrawColorSettings() {
        if(ImGui.Button(ChineseText.Label("Reset to Defaults"))) {
            _plugin.Configuration.Colors = new();
            //_plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }

        ImGui.SameLine();

        if(ImGui.Button(ChineseText.Label("Revert Changes"))) {
            _plugin.DataQueue.QueueDataOperation(() => {
                var cfgSaved = _plugin.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
                _plugin.Configuration.Colors = cfgSaved.Colors;
            });
        }
        ImGuiHelper.HelpMarker("Colors will lock in after this window is closed or a non-color setting is changed.");

        using var table = ImRaii.Table("ColorSettingsTable", 3);
        if(!table) {
            return;
        }
        ImGui.TableSetupColumn("c1");
        ImGui.TableSetupColumn("c2");
        ImGui.TableSetupColumn("c3");

        ImGui.TableNextColumn();
        var headerColor = _plugin.Configuration.Colors.Header;
        if(ImGui.ColorEdit4(ChineseText.Label("Headers"), ref headerColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.Header = headerColor;
        }
        ImGui.TableNextColumn();
        var favoriteColor = _plugin.Configuration.Colors.Favorite;
        if(ImGui.ColorEdit4(ChineseText.Label("Favorites"), ref favoriteColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.Favorite = favoriteColor;
        }
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var winColor = _plugin.Configuration.Colors.Win;
        if(ImGui.ColorEdit4(ChineseText.Label("Wins"), ref winColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.Win = winColor;
        }
        ImGui.TableNextColumn();
        var lossColor = _plugin.Configuration.Colors.Loss;
        if(ImGui.ColorEdit4(ChineseText.Label("Losses"), ref lossColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.Loss = lossColor;
        }
        ImGui.TableNextColumn();
        var otherColor = _plugin.Configuration.Colors.Other;
        if(ImGui.ColorEdit4(ChineseText.Label("Draws"), ref otherColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.Other = otherColor;
        }
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var localPlayerColor = _plugin.Configuration.Colors.CCLocalPlayer;
        if(ImGui.ColorEdit4(ChineseText.Label("Local Player"), ref localPlayerColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.CCLocalPlayer = localPlayerColor;
        }
        ImGui.TableNextColumn();
        var playerTeamColor = _plugin.Configuration.Colors.CCPlayerTeam;
        if(ImGui.ColorEdit4(ChineseText.Label("Player Team"), ref playerTeamColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.CCPlayerTeam = playerTeamColor;
        }
        ImGui.TableNextColumn();
        var enemyTeamColor = _plugin.Configuration.Colors.CCEnemyTeam;
        if(ImGui.ColorEdit4(ChineseText.Label("Enemy Team"), ref enemyTeamColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.CCEnemyTeam = enemyTeamColor;
        }
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var tankColor = _plugin.Configuration.Colors.Tank;
        if(ImGui.ColorEdit4(ChineseText.Label("Tank"), ref tankColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.Tank = tankColor;
        }
        ImGui.TableNextColumn();
        var healerColor = _plugin.Configuration.Colors.Healer;
        if(ImGui.ColorEdit4(ChineseText.Label("Healer"), ref healerColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.Healer = healerColor;
        }
        ImGui.TableNextColumn();
        var meleeColor = _plugin.Configuration.Colors.Melee;
        if(ImGui.ColorEdit4(ChineseText.Label("Melee"), ref meleeColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.Melee = meleeColor;
        }
        ImGui.TableNextColumn();
        var rangedColor = _plugin.Configuration.Colors.Ranged;
        if(ImGui.ColorEdit4(ChineseText.Label("Ranged"), ref rangedColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.Ranged = rangedColor;
        }
        ImGui.TableNextColumn();
        var casterColor = _plugin.Configuration.Colors.Caster;
        if(ImGui.ColorEdit4(ChineseText.Label("Caster"), ref casterColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.Caster = casterColor;
        }
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var statHighColor = _plugin.Configuration.Colors.StatHigh;
        if(ImGui.ColorEdit4(ChineseText.Label("High stat"), ref statHighColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.StatHigh = statHighColor;
            //_plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        ImGui.TableNextColumn();
        var statLowColor = _plugin.Configuration.Colors.StatLow;
        if(ImGui.ColorEdit4(ChineseText.Label("Low stat"), ref statLowColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.StatLow = statLowColor;
            //_plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var playerRowColor = _plugin.Configuration.Colors.PlayerRowText;
        if(ImGui.ColorEdit4(ChineseText.Label("Player row text"), ref playerRowColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.PlayerRowText = playerRowColor;
            //_plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
        ImGui.TableNextColumn();
        var teamRowColor = _plugin.Configuration.Colors.TeamRowText;
        if(ImGui.ColorEdit4(ChineseText.Label("Team row text"), ref teamRowColor, ImGuiColorEditFlags.NoInputs)) {
            _plugin.Configuration.Colors.TeamRowText = teamRowColor;
            //_plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
        }
    }

    private void ColorPicker(string name, ref Vector4 prop) {
        var color = prop;
        if(ImGui.ColorEdit4(ChineseText.Label("Headers"), ref color, ImGuiColorEditFlags.NoInputs)) {
            prop = color;
        }
    }

    private void DrawPlayerLinkSettings() {
        bool playerLinking = _plugin.Configuration.EnablePlayerLinking;
        if(ImGui.Checkbox(ChineseText.Label("Enable player linking"), ref playerLinking)) {
            _plugin.DataQueue.QueueDataOperation(() => {
                _plugin.Configuration.EnablePlayerLinking = playerLinking;
                _plugin.Configuration.Save();
                _plugin.PlayerLinksService.BuildLinkedAliases();
                _ = _plugin.WindowManager.RefreshAll(true);
            });
        }
        ImGuiHelper.HelpMarker("Enable combining of player stats with different aliases linked with the same unique character or player.");
        bool autoLinking = _plugin.Configuration.EnableAutoPlayerLinking;
        if(ImGui.Checkbox(ChineseText.Label("Enable auto linking (requires PlayerTrack)"), ref autoLinking)) {
            _plugin.DataQueue.QueueDataOperation(() => {
                _plugin.Configuration.EnableAutoPlayerLinking = autoLinking;
                _plugin.Configuration.Save();
                _plugin.PlayerLinksService.BuildLinkedAliases();
                _ = _plugin.WindowManager.RefreshAll(true);
            });
        }
        ImGuiHelper.HelpMarker("Use name change data from PlayerTrack to create player links.\n\n" +
            "Does not work on your own character (for now).");
        bool manualLinking = _plugin.Configuration.EnableManualPlayerLinking;
        if(ImGui.Checkbox(ChineseText.Label("Enable manual linking"), ref manualLinking)) {
            _plugin.DataQueue.QueueDataOperation(() => {
                _plugin.Configuration.EnableManualPlayerLinking = manualLinking;
                _plugin.Configuration.Save();
                _plugin.PlayerLinksService.BuildLinkedAliases();
                _ = _plugin.WindowManager.RefreshAll(true);
            });
        }
        ImGuiHelper.HelpMarker("Use the manual tab to create player links by hand or to track" +
            " un-covered auto link scenarios such as personal character alias changes, track known alt characters or to override mistakes.\n\nEnter format as <player name> <home world>");
        using(var tabBar = ImRaii.TabBar("LinksTabBar")) {
            using(var tab = ImRaii.TabItem(ChineseText.Label("Auto"))) {
                if(tab) {
                    using var disabledButton = ImRaii.Disabled();
                    if(!_ipcUpdateInProgress) {
                        disabledButton.Dispose();
                    }
                    if(ImGui.Button(ChineseText.Label("Update Now"))) {
                        Task.Run(async () => {
                            _ipcUpdateInProgress = true;
                            await _plugin.PlayerLinksService.BuildAutoLinksCache();
                            _ipcUpdateInProgress = false;
                            _ = _plugin.WindowManager.RefreshAll(true);
                        });
                    }
                    if(_ipcUpdateInProgress) {
                        disabledButton.Dispose();
                    }
                    DrawAutoPlayerLinkSettings();
                }
            }
            using(var tab = ImRaii.TabItem(ChineseText.Label("Manual"))) {
                if(tab) {
                    DrawManualPlayerLinkSettings();
                }
            }
        }
    }

    private void DrawAutoPlayerLinkSettings() {
        ImGui.Text(ChineseText.Text("Players with auto-linked aliases: "));

        using(var child = ImRaii.Child("AutoPlayerLinks", ImGui.GetContentRegionAvail(), true)) {
            if(child) {
                foreach(var playerLink in AutoLinks) {
                    DrawAutoPlayerLink(playerLink);
                }
            }
        }
    }

    private void DrawManualPlayerLinkSettings() {
        using(var child = ImRaii.Child("ManualLinksChild", new Vector2(0, -(25 + ImGui.GetStyle().ItemSpacing.Y) * ImGuiHelpers.GlobalScale), true)) {
            if(child) {
                using(var table = ImRaii.Table("ManualLinksTable", 4, ImGuiTableFlags.NoSavedSettings)) {
                    if(table) {
                        ImGui.TableSetupColumn("LinkedAlias", ImGuiTableColumnFlags.WidthStretch, 180f * ImGuiHelpers.GlobalScale);
                        ImGui.TableSetupColumn("Verb", ImGuiTableColumnFlags.WidthFixed, 75f * ImGuiHelpers.GlobalScale);
                        ImGui.TableSetupColumn("SourceAlias", ImGuiTableColumnFlags.WidthStretch, 180f * ImGuiHelpers.GlobalScale);
                        ImGui.TableSetupColumn("Button", ImGuiTableColumnFlags.WidthFixed, 25f * ImGuiHelpers.GlobalScale);
                        try {
                            foreach(var link in ManualLinks) {
                                DrawManualPlayerLink(link);
                            }
                            DrawManualPlayerLink(_newManualLink, true);
                        } catch {
                            //suppress remove elements
                        }
                    }
                }
            }
        }
        if(ImGui.Button(ChineseText.Label("Save"))) {
            _plugin.DataQueue.QueueDataOperation(async () => {
                //_plugin.Storage.SetManualLinks(ManualLinks, false);

                //add current manual link
                if(_newManualLink.CurrentAlias != null) {
                    ManualLinks.Add(_newManualLink);
                }

                await _plugin.PlayerLinksService.SaveManualLinksCache(ManualLinks);
                if(_plugin.Configuration.EnablePlayerLinking && _plugin.Configuration.EnableManualPlayerLinking) {
                    _ = _plugin.WindowManager.RefreshAll(true);
                }
            }).ContinueWith((t) => {
                _saveOpacity = 1f;
            });
        }
        if(_saveOpacity > 0f) {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 1f, 1f, _saveOpacity), ChineseText.Text("Saved!"));
            _saveOpacity -= 0.002f;
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - 60f * ImGuiHelpers.GlobalScale);
        if(ImGui.Button(ChineseText.Label("Cancel"))) {
            _plugin.DataQueue.QueueDataOperation(async () => {
                await Refresh();
            });
        }
    }

    private void DrawAutoPlayerLink(PlayerAliasLink playerLink) {
        ImGuiHelper.FormattedCollapsibleHeader(new[] { (playerLink.CurrentAlias.Name, 200f), (playerLink.CurrentAlias.HomeWorld, 200f), }, () => {
            foreach(var prevAlias in playerLink.LinkedAliases) {
                using(var table = ImRaii.Table($"{playerLink.Id}--AliasTable", 2)) {
                    if(!table) {
                        return;
                    }
                    ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthFixed, 200f * ImGuiHelpers.GlobalScale);
                    ImGui.TableSetupColumn("homeworld");
                    ImGui.TableNextColumn();
                    ImGui.Text(prevAlias.Name);
                    ImGui.TableNextColumn();
                    ImGui.Text(prevAlias.HomeWorld);
                }
            }
        });
    }

    private void DrawManualPlayerLink(PlayerAliasLink playerLink, bool isNew = false) {
        string inputText1 = "";
        string inputText2 = "";
        if(playerLink.LinkedAliases.Count > 0) {
            inputText2 = playerLink.LinkedAliases[0];
        }

        if(playerLink.CurrentAlias != null) {
            inputText1 = playerLink.CurrentAlias;
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if(ImGui.InputTextWithHint($"###{playerLink.GetHashCode()}--CurrentAlias", "Enter main player", ref inputText1, 60)) {
            try {
                playerLink.CurrentAlias = (PlayerAlias)inputText1;
            } catch {
                playerLink.CurrentAlias = null;
            }
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        int verbIndex = playerLink.IsUnlink ? 1 : 0;
        if(ImGui.Combo($"###{playerLink.GetHashCode()}--IsUnlink", ref verbIndex, _linksVerbCombo, _linksVerbCombo.Length)) {
            playerLink.IsUnlink = verbIndex == 1;
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if(ImGui.InputTextWithHint($"###{playerLink.GetHashCode()}--LinkedAlias", "Enter linked player", ref inputText2, 60)) {
            try {
                if(playerLink.LinkedAliases.Count <= 0) {
                    playerLink.LinkedAliases.Add((PlayerAlias)inputText2);
                } else {
                    playerLink.LinkedAliases[0] = (PlayerAlias)inputText2;
                }
            } catch {
                playerLink.LinkedAliases = new();
            }
        }
        ImGui.TableNextColumn();
        using(var font = ImRaii.PushFont(UiBuilder.IconFont)) {
            if(isNew) {
                if(ImGui.Button($"{FontAwesomeIcon.Plus.ToIconString()}###{playerLink.GetHashCode()}--Button")) {
                    ManualLinks.Add(playerLink);
                    _newManualLink = new();
                }

            } else {
                if(ImGui.Button($"{FontAwesomeIcon.Trash.ToIconString()}###{playerLink.GetHashCode()}--Button")) {
                    ManualLinks.Remove(playerLink);
                }

            }
        }
        ImGuiHelper.WrappedTooltip(isNew ? "Add Link" : "Remove");
    }

    private void DrawPerformanceSettings() {
        ImGui.TextColored(_plugin.Configuration.Colors.Header, ChineseText.Text("Match Caching"));
        ImGuiHelper.HelpMarker("Enabling these options will cache your entire match history in memory. Can help with refresh performance at the cost of increased memory usage.", true);

        bool enableCachingCC = _plugin.Configuration.EnableDBCachingCC ?? true;
        if(ImGui.Checkbox(ChineseText.Label("Crystalline Conflict"), ref enableCachingCC)) {
            _plugin.Configuration.EnableDBCachingCC = enableCachingCC;
            _plugin.DataQueue.QueueDataOperation(() => {
                if(enableCachingCC) {
                    _plugin.CCCache.EnableCaching();
                } else {
                    _plugin.CCCache.DisableCaching();
                }
                _plugin.Configuration.Save();
            });
        }
        ImGui.Separator();
        ImGui.TextColored(_plugin.Configuration.Colors.Header, "Enhanced Tracking Features");
        ImGuiHelper.HelpMarker("Disabling these features can help save disk space.");

        bool enableTimelineCC = _plugin.Configuration.EnableTimelineCC ?? true;
        if(ImGui.Checkbox(ChineseText.Label("Crystalline Conflict Timeline Tracking"), ref enableTimelineCC)) {
            _plugin.Configuration.EnableTimelineCC = enableTimelineCC;
            _plugin.DataQueue.QueueDataOperation(() => {
                _plugin.Configuration.Save();
            });
        }
        bool enableActionAnalyticsCC = _plugin.Configuration.EnableActionAnalyticsCC ?? true;
        if(ImGui.Checkbox(ChineseText.Label("Crystalline Conflict Action Tracking"), ref enableActionAnalyticsCC)) {
            _plugin.Configuration.EnableActionAnalyticsCC = enableActionAnalyticsCC;
            _plugin.DataQueue.QueueDataOperation(() => {
                _plugin.Configuration.Save();
            });
        }
    }

}
