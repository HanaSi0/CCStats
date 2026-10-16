using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.IO;
using System.Numerics;

namespace PvpStats.Windows.Floating;

internal sealed class FloatingLauncher {
    private readonly Plugin _plugin;
    private readonly LauncherInteraction _interaction = new();
    private readonly string _iconPath;

    internal FloatingLauncher(Plugin plugin) {
        _plugin = plugin;
        _iconPath = Path.Combine(plugin.PluginInterface.AssemblyLocation.Directory!.FullName, "icon.png");
    }

    internal void ResetPosition() {
        _interaction.CancelDrag();
        _plugin.Configuration.FloatingLauncherPosition = LauncherInteraction.DefaultPosition;
        _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
    }

    internal void Draw() {
        if(!_plugin.Configuration.ShowFloatingLauncher || !_plugin.ClientState.IsLoggedIn) {
            _interaction.CancelDrag();
            return;
        }
        var viewport = ImGui.GetMainViewport();
        var scale = ImGuiHelpers.GlobalScale;
        var configuredSize = _plugin.Configuration.FloatingLauncherSize;
        var logicalSize = float.IsFinite(configuredSize) ? Math.Clamp(configuredSize, 32, 96) : 48;
        var size = new Vector2(logicalSize * scale);
        var viewportSize = viewport.WorkSize / scale;
        var position = LauncherInteraction.Clamp(_plugin.Configuration.FloatingLauncherPosition, viewportSize, logicalSize);
        ImGui.SetNextWindowPos(viewport.WorkPos + position * scale, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero)
            .Push(ImGuiStyleVar.WindowBorderSize, 0f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav;
        var draw = ImGui.Begin("##CCStatsFloatingLauncher", flags);
        try {
            if(!draw) return;
            var origin = ImGui.GetCursorScreenPos();
            if(File.Exists(_iconPath)) {
                var texture = _plugin.TextureProvider.GetFromFile(_iconPath).GetWrapOrEmpty();
                ImGui.GetWindowDrawList().AddImage(texture.Handle, origin, origin + size);
            } else {
                ImGui.GetWindowDrawList().AddText(origin + new Vector2(8, 14) * scale, 0xFFFFFFFF, "CC");
            }
            ImGui.InvisibleButton("##CCStatsFloatingIcon", size,
                ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            var result = _interaction.Update(_plugin.Configuration.FloatingLauncherPosition,
                (ImGui.GetMousePos() - viewport.WorkPos) / scale, viewportSize, logicalSize,
                ImGui.IsItemClicked(ImGuiMouseButton.Left), ImGui.IsItemClicked(ImGuiMouseButton.Right),
                ImGui.IsMouseDown(ImGuiMouseButton.Right));
            _plugin.Configuration.FloatingLauncherPosition = result.Position;
            if(result.SavePosition) _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save);
            if(result.OpenWindow) _plugin.WindowManager.OpenCCWindow();
            if(ImGui.IsItemHovered() && !_interaction.IsDragging) {
                ImGuiHelperTooltip();
            }
        } finally {
            ImGui.End();
        }
    }

    private static void ImGuiHelperTooltip() {
        using var tooltip = ImRaii.Tooltip();
        ImGui.TextUnformatted("左键：打开水晶冲突记录器\n按住右键：拖动图标");
    }
}
