using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.IO;
using PvpStats.Settings;
using System.Numerics;

namespace PvpStats.Interface;

internal static class ModernTheme {
    private static ThemeConfiguration _palette = new();
    internal static Vector4 Accent => _palette.Accent;
    internal static Vector4 Gold => _palette.Highlight;
    internal static Vector4 Button => _palette.Button;
    internal static Vector4 Selection => _palette.Selection;
    internal static IDisposable Apply(ThemeConfiguration palette) {
        _palette = palette;
        return new Scope(palette);
    }
    private static Vector4 Hover(Vector4 basis, Vector4 accent, float amount) => Vector4.Lerp(basis, accent, amount);
    private sealed class Scope : IDisposable {
        private readonly IDisposable _styles;
        private readonly IDisposable _colors;
        internal Scope(ThemeConfiguration p) {
            _styles = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 16f)
                .Push(ImGuiStyleVar.ChildRounding, 12f).Push(ImGuiStyleVar.FrameRounding, 8f)
                .Push(ImGuiStyleVar.PopupRounding, 10f).Push(ImGuiStyleVar.TabRounding, 8f)
                .Push(ImGuiStyleVar.ScrollbarRounding, 8f).Push(ImGuiStyleVar.WindowBorderSize, 1f)
                .Push(ImGuiStyleVar.FrameBorderSize, 0f).Push(ImGuiStyleVar.WindowPadding, new Vector2(16, 14))
                .Push(ImGuiStyleVar.FramePadding, new Vector2(10, 7)).Push(ImGuiStyleVar.ItemSpacing, new Vector2(10, 8))
                .Push(ImGuiStyleVar.CellPadding, new Vector2(10, 8));
            _colors = ImRaii.PushColor(ImGuiCol.WindowBg, p.Background).Push(ImGuiCol.ChildBg, p.Panel)
                .Push(ImGuiCol.PopupBg, p.Panel).Push(ImGuiCol.Border, p.Border)
                .Push(ImGuiCol.Text, p.Text).Push(ImGuiCol.TextDisabled, p.Muted)
                .Push(ImGuiCol.TitleBg, p.Background).Push(ImGuiCol.TitleBgActive, p.Panel)
                .Push(ImGuiCol.FrameBg, p.Surface).Push(ImGuiCol.FrameBgHovered, Hover(p.Surface, p.Accent, .18f))
                .Push(ImGuiCol.FrameBgActive, Hover(p.Surface, p.Accent, .30f))
                .Push(ImGuiCol.Button, p.Button).Push(ImGuiCol.ButtonHovered, Hover(p.Button, p.Accent, .30f))
                .Push(ImGuiCol.ButtonActive, Hover(p.Button, p.Accent, .50f))
                .Push(ImGuiCol.Header, p.Selection).Push(ImGuiCol.HeaderHovered, Hover(p.Selection, p.Accent, .25f))
                .Push(ImGuiCol.HeaderActive, Hover(p.Selection, p.Accent, .45f))
                .Push(ImGuiCol.Tab, p.Surface).Push(ImGuiCol.TabHovered, Hover(p.Surface, p.Accent, .30f))
                .Push(ImGuiCol.TabActive, p.Selection)
                .Push(ImGuiCol.CheckMark, p.Accent).Push(ImGuiCol.SliderGrab, p.Accent)
                .Push(ImGuiCol.SliderGrabActive, p.Highlight).Push(ImGuiCol.Separator, p.Border)
                .Push(ImGuiCol.ResizeGrip, p.Selection).Push(ImGuiCol.ResizeGripHovered, p.Accent)
                .Push(ImGuiCol.ResizeGripActive, p.Highlight)
                .Push(ImGuiCol.ScrollbarBg, p.Background).Push(ImGuiCol.ScrollbarGrab, p.Button)
                .Push(ImGuiCol.ScrollbarGrabHovered, Hover(p.Button, p.Accent, .30f))
                .Push(ImGuiCol.ScrollbarGrabActive, p.Accent)
                .Push(ImGuiCol.TableHeaderBg, p.TableHeader).Push(ImGuiCol.TableRowBg, p.Panel)
                .Push(ImGuiCol.TableRowBgAlt, p.TableAlternate)
                .Push(ImGuiCol.TableBorderLight, p.Border).Push(ImGuiCol.TableBorderStrong, p.Border);
        }
        public void Dispose() { _colors.Dispose(); _styles.Dispose(); }
    }

    internal static void Brand(Plugin plugin, string title, string subtitle) {
        var size = 44f * ImGuiHelpers.GlobalScale;
        var path = Path.Combine(plugin.PluginInterface.AssemblyLocation.Directory!.FullName, "icon.png");
        ImGui.Image(plugin.TextureProvider.GetFromFile(path).GetWrapOrEmpty().Handle, new Vector2(size));
        ImGui.SameLine();
        using(var group = ImRaii.Group()) {
            ImGui.TextColored(Accent, title);
            ImGui.TextDisabled(subtitle);
        }
        ImGui.Spacing();
    }

    internal static void Card(string id, string caption, string value, float width, Vector4 color) {
        using var card = ImRaii.Child(id, new Vector2(width, 78f * ImGuiHelpers.GlobalScale), true,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if(!card) return;
        ImGui.TextDisabled(caption);
        ImGui.SetWindowFontScale(1.35f);
        ImGui.TextColored(color, value);
        ImGui.SetWindowFontScale(1f);
    }
}
