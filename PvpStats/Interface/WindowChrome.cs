using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using System.Numerics;
using Dalamud.Interface.Utility;
using PvpStats.Helpers;

namespace PvpStats.Interface;

internal static class WindowChrome {
    internal static void Constrain(Window window) {
        var screen=ImGui.GetMainViewport().WorkSize/ImGuiHelpers.GlobalScale;
        window.SizeConstraints=new() { MinimumSize=ViewLayout.WindowMinimum(screen),MaximumSize=Vector2.Max(new Vector2(280,220),screen-new Vector2(16)) };
    }
    internal static void Configure(Window window) {
        // Use the same host-rendered icon font and hit testing for every title-bar control.
        // Retain Dalamud's additions menu (pin/opacity), replace only the two mismatched native glyphs.
        window.ShowCloseButton=false;
        window.Flags &= ~(ImGuiWindowFlags.NoResize|ImGuiWindowFlags.AlwaysAutoResize);
        var preset=0;
        window.TitleBarButtons.Add(new() {
            Priority=-3,Icon=FontAwesomeIcon.Expand,IconOffset=new Vector2(2.5f,1),
            ShowTooltip=()=>ImGui.SetTooltip("调整窗口大小：紧凑 → 标准 → 宽屏；也可拖动窗口边缘"),
            Click=_=> {
                window.IsPinned=false;
                var size=ViewLayout.WindowPreset(preset++,ImGui.GetMainViewport().WorkSize/ImGuiHelpers.GlobalScale);
                ImGui.SetWindowSize(size*ImGuiHelpers.GlobalScale,ImGuiCond.Always);
            },
        });
        window.AllowClickthrough=false;
        window.Flags|=ImGuiWindowFlags.NoCollapse;
        window.TitleBarButtons.Add(new() {
            Priority=-1, Icon=FontAwesomeIcon.Times, IconOffset=new Vector2(2.5f,1),
            ShowTooltip=()=>ImGui.SetTooltip("关闭窗口"), Click=_=>window.IsOpen=false,
        });
        window.TitleBarButtons.Add(new() {
            Priority=-2, Icon=FontAwesomeIcon.ChevronDown, IconOffset=new Vector2(2.5f,1),
            ShowTooltip=()=>ImGui.SetTooltip(ImGui.IsWindowCollapsed()?"展开窗口":"折叠窗口"),
            Click=_=>ImGui.SetWindowCollapsed(!ImGui.IsWindowCollapsed()),
        });
    }
}
