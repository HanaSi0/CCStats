using PvpStats.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using PvpStats.Helpers;
using PvpStats.Managers.Stats;
using PvpStats.Settings;
using PvpStats.Types.Match;
using PvpStats.Windows.Filter;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace PvpStats.Windows.Tracker;
internal abstract class TrackerWindow<T> : Window where T : PvpMatch {
    protected readonly Plugin Plugin;
    protected readonly StatsManager<T> StatsEngine;
    protected readonly WindowConfiguration WindowConfig;
    protected bool CollapseFilters;
    protected string CurrentTab = "";

    private int _drawCycles;
    private float _longestDraw, _longestPreDraw;

    internal List<DataFilter> MatchFilters { get; private set; } = new();
    internal List<DataFilter> JobStatFilters { get; private set; } = new();
    internal List<DataFilter> PlayerStatFilters { get; private set; } = new();
    protected List<Refreshable<T>> Tabs = [];

    protected TrackerWindow(Plugin plugin, StatsManager<T> statsManager, WindowConfiguration config, string name) : base(ChineseText.Label(name)) {
        Plugin = plugin;
        StatsEngine = statsManager;
        CollapseFilters = config.FiltersCollapsed;
        WindowConfig = config;

        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(425, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Flags |= ImGuiWindowFlags.NoScrollbar;
    }

    public virtual async Task Refresh(bool fullRefresh = false) {
        Stopwatch s0 = new();
        s0.Start();

        Tabs.ForEach(x => {
            x.RefreshProgress = 0f;
            x.RefreshActive = true;
        });

        try {
            var updatedSet = await StatsEngine.Refresh(MatchFilters);

            if(fullRefresh) {
                updatedSet.Removals = updatedSet.Matches;
                updatedSet.Additions = updatedSet.Matches;
            }

            List<Task<Task>> refreshTasks = [];
            Tabs.ForEach((x) => {
                var task = RefreshTab(async () => {
                    await x.Refresh(updatedSet.Matches, updatedSet.Additions, updatedSet.Removals);
                });
                refreshTasks.Add(task);
            });

            await Task.WhenAll([
                Task.Run(SaveFilters),
                .. refreshTasks.Select(x => x.Result),
            ]);
        } catch {
            Plugin.Log.Error($"{WindowName} refresh failed.");
            throw;
        } finally {
            Plugin.Log.Information(string.Format("{0,-50}: {1,4} ms", $"{WindowName} refresh time", s0.ElapsedMilliseconds.ToString()));
        }
    }

    protected void SaveFilters() {
        Stopwatch s1 = Stopwatch.StartNew();
        WindowConfig.MatchFilters.SetFilters(MatchFilters);
        WindowConfig.JobStatFilters.SetFilters(JobStatFilters);
        WindowConfig.PlayerStatFilters.SetFilters(PlayerStatFilters);
        Plugin.Configuration.Save();
        s1.Stop();
        Plugin.Log.Debug(string.Format("{0,-50}: {1,4} ms", $"{WindowName} save config", s1.ElapsedMilliseconds.ToString()));
    }

    public override void Draw() {
#if DEBUG
        var s1 = Stopwatch.StartNew();
#endif
        DrawInternal();

#if DEBUG
        s1.Stop();
        if(_drawCycles % 5000 == 0) {
            _drawCycles = 0;
            if(s1.ElapsedMilliseconds > _longestDraw) {
                _longestDraw = s1.ElapsedMilliseconds;
            }
            if(Plugin.DebugMode) {
                Plugin.Log.Debug($"{WindowName} longest draw: {_longestDraw}");
            }
        }
        _drawCycles++;
#endif
    }

    public abstract void DrawInternal();

    protected void DrawFilters() {
        if(!CollapseFilters) {
            using var child = ImRaii.Child("FilterChild",
            new Vector2(ImGui.GetContentRegionAvail().X, WindowConfig.FilterHeight),
            true, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.AlwaysVerticalScrollbar);
            if(child) {
                using var table = ImRaii.Table("FilterTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersInnerH);
                if(!table) return;
                ImGui.TableSetupColumn("filterName", ImGuiTableColumnFlags.WidthFixed, ImGuiHelpers.GlobalScale * 110f);
                ImGui.TableSetupColumn($"filters", ImGuiTableColumnFlags.WidthStretch);
                foreach(var filter in MatchFilters) {
                    if(filter is MatchSearchFilter) continue;
                    ImGui.TableNextColumn();

                    if(filter.HelpMessage != null) {
                        ImGui.AlignTextToFramePadding();
                        ImGuiHelper.HelpMarker(filter.HelpMessage, false);
                        ImGui.SameLine();
                    }
                    string nameText = $"{ChineseText.Text(filter.Name)}：";
                    ImGuiHelper.RightAlignCursor2(nameText, -5f * ImGuiHelpers.GlobalScale);
                    ImGui.AlignTextToFramePadding();
                    //ImGui.SetCursorPosX(ImGui.GetCursorPosX() + float.Max(0, 16f - 4f * ImGuiHelpers.GlobalScale));
                    ImGui.TextUnformatted(nameText);
                    ImGui.TableNextColumn();
                    filter.Draw();
                }
            }
        }
        if(ImGui.Button(CollapseFilters ? "高级筛选###ToggleAdvancedFilters" : "收起筛选###ToggleAdvancedFilters")) {
            CollapseFilters = !CollapseFilters;
            WindowConfig.FiltersCollapsed = CollapseFilters;
            Plugin.DataQueue.QueueDataOperation(Plugin.Configuration.Save);
        }
    }

    protected Task<Task> RefreshTab(Func<Task> func) {
        var task = new Task<Task>(async () => {
            try {
                await func.Invoke();
            } catch(Exception e) {
                Plugin.Log2.Error(e, "Refresh Tab error");
            }
        });
        task.Start();
        return task;
    }

    protected unsafe void Tab(string name, Action action, bool refreshActive = false, double refreshProgress = 0f) {
        using var tab = ImRaii.TabItem(ChineseText.Label(name));
        if(tab) {
            ChangeTab(name);
            try {
                action.Invoke();
            } catch(Exception e) {
                //suppress all exceptions while a refresh is in progress
                if(!refreshActive) {
                    Plugin.Log2.Error(e, "Tab Draw Error");
                }
            } finally {
                if(refreshActive) {
                    ImGuiHelper.DrawRefreshProgressBar((float)refreshProgress);
                }
            }
        }
    }

    // Called inside the navigation child: never read its size as the root window size.
    protected void ChangeTab(string tab) => CurrentTab = tab;
}
