using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace PvpStats.Interface;

internal static class WindowChrome {
    internal static void Configure(Window window) {
        // Use the same host-rendered icon font and hit testing for every title-bar control.
        // Retain Dalamud's additions menu (pin/opacity), replace only the two mismatched native glyphs.
        window.ShowCloseButton=false;
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
