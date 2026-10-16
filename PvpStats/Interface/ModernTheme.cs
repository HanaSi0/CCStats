using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using PvpStats.Localization;
using PvpStats.Settings;
using System.Numerics;

namespace PvpStats.Interface;

internal static class ModernTheme {
    private static ThemeConfiguration _palette = new();
    internal static Vector4 Accent => _palette.Accent;
    internal static Vector4 Gold => _palette.Highlight;
    internal static Vector4 ButtonBackground => _palette.Button;
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
                .Push(ImGuiCol.Button, ThemeContrast.BackgroundForText(p.Button, p.Text)).Push(ImGuiCol.ButtonHovered, ThemeContrast.BackgroundForText(p.ButtonHovered, p.Text))
                .Push(ImGuiCol.ButtonActive, ThemeContrast.BackgroundForText(p.ButtonPressed, p.Text))
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

    private sealed class Interaction {
        internal float Hover, Press;
        internal bool WasHovered, WasPressed;
        internal int Frame;
    }
    private static readonly Dictionary<uint, Interaction> Interactions = new();
    private static Interaction Animate(uint id) {
        var frame = ImGui.GetFrameCount();
        if(!Interactions.TryGetValue(id, out var state)) Interactions[id] = state = new();
        // Do not replay stale hover/press states when a hidden page becomes visible again.
        if(frame - state.Frame > 2) state.Hover = state.Press = 0;
        state.Hover = ThemeContrast.Ease(state.Hover, state.WasHovered && frame - state.Frame <= 2 ? 1 : 0, ImGui.GetIO().DeltaTime, _palette.EnableAnimations);
        state.Press = ThemeContrast.Ease(state.Press, state.WasPressed && frame - state.Frame <= 2 ? 1 : 0, ImGui.GetIO().DeltaTime, _palette.EnableAnimations);
        state.Frame = frame;
        if(Interactions.Count > 2048) {
            foreach(var key in Interactions.Where(x => frame - x.Value.Frame > 120).Select(x => x.Key).ToArray()) Interactions.Remove(key);
            if(Interactions.Count > 4096) Interactions.Clear();
        }
        return state;
    }
    private static void Capture(Interaction state) {
        state.WasHovered = ImGui.IsItemHovered() || ImGui.IsItemFocused();
        state.WasPressed = ImGui.IsItemActive();
    }
    internal static bool Button(string label, Vector2 size = default) => DrawButton(label, size, false);
    internal static bool SmallButton(string label) => DrawButton(label, default, true);
    private static bool DrawButton(string label, Vector2 size, bool small) {
        label = ChineseText.Label(label);
        var state = Animate(ImGui.GetID(label));
        var normal = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];
        var background = Vector4.Lerp(Vector4.Lerp(normal, _palette.ButtonHovered, state.Hover), _palette.ButtonPressed, state.Press);
        var foreground = ThemeContrast.Foreground(background, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        using var colors = ImRaii.PushColor(ImGuiCol.Button, background).Push(ImGuiCol.ButtonHovered, background)
            .Push(ImGuiCol.ButtonActive, background).Push(ImGuiCol.Text, foreground);
        var clicked = small ? ImGui.SmallButton(label) : ImGui.Button(label, size);
        Capture(state);
        return clicked;
    }
    internal static bool NavigationItem(string label, bool selected, Vector2 size) {
        var state = Animate(ImGui.GetID(label));
        var background = Vector4.Lerp(selected ? _palette.Selection : _palette.Panel, _palette.ButtonHovered, state.Hover * .7f);
        background = Vector4.Lerp(background, _palette.ButtonPressed, state.Press);
        var foreground = ThemeContrast.Foreground(background, _palette.Text);
        using var colors = ImRaii.PushColor(ImGuiCol.Header, background).Push(ImGuiCol.HeaderHovered, background)
            .Push(ImGuiCol.HeaderActive, background).Push(ImGuiCol.Text, foreground);
        var clicked = ImGui.Selectable(label, selected || state.Hover > .01f, ImGuiSelectableFlags.None, size);
        Capture(state);
        if(selected) {
            var a = ImGui.GetItemRectMin(); var b = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddRectFilled(a, new Vector2(a.X + 3 * ImGuiHelpers.GlobalScale, b.Y), ImGui.ColorConvertFloat4ToU32(_palette.Accent), 2);
        }
        return clicked;
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
