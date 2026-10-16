using System;
using System.Numerics;

namespace PvpStats.Settings;

/// <summary>Window palette, independent of gameplay and job colors.</summary>
public sealed class ThemeConfiguration {
    public Vector4 Background { get; set; } = new(.045f, .065f, .115f, .99f);
    public Vector4 Panel { get; set; } = new(.07f, .10f, .17f, 1);
    public Vector4 Surface { get; set; } = new(.10f, .14f, .22f, 1);
    public Vector4 Border { get; set; } = new(.19f, .27f, .40f, 1);
    public Vector4 Text { get; set; } = new(.89f, .93f, 1, 1);
    public Vector4 Muted { get; set; } = new(.49f, .59f, .73f, 1);
    public Vector4 Accent { get; set; } = new(.40f, .73f, 1, 1);
    public Vector4 Highlight { get; set; } = new(1, .75f, .31f, 1);
    public Vector4 Button { get; set; } = new(.13f, .23f, .35f, 1);
    public Vector4 ButtonHovered { get; set; } = new(.22f, .72f, 1, 1);
    public Vector4 ButtonPressed { get; set; } = new(1, .70f, .24f, 1);
    public bool EnableAnimations { get; set; } = true;
    public static string[] PresetNames => ["水晶冰蓝", "暮色紫晶", "翠绿森林", "清晨浅色", "赤焰朱红", "琥珀暮金", "樱粉夜色", "深海青碧", "石墨极简", "靛蓝霓光", "暖沙浅色", "高对比黑金"];
    public Vector4 Selection { get; set; } = new(.14f, .27f, .42f, 1);
    public Vector4 TableHeader { get; set; } = new(.09f, .15f, .24f, 1);
    public Vector4 TableAlternate { get; set; } = new(.10f, .16f, .24f, 1);

    public ThemeConfiguration Copy() => (ThemeConfiguration)MemberwiseClone();

    public void Normalize() {
        var defaults = new ThemeConfiguration();
        foreach(var property in typeof(ThemeConfiguration).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
            if(property.PropertyType != typeof(Vector4)) continue;
            var value = (Vector4)property.GetValue(this)!;
            var fallback = (Vector4)property.GetValue(defaults)!;
            float Channel(float channel, float original) => float.IsFinite(channel) ? Math.Clamp(channel, 0, 1) : original;
            value = new(Channel(value.X, fallback.X), Channel(value.Y, fallback.Y), Channel(value.Z, fallback.Z), 1);
            if(property.Name == nameof(Background)) {
                var alpha = ((Vector4)property.GetValue(this)!).W;
                value.W = float.IsFinite(alpha) ? Math.Clamp(alpha, .35f, 1) : fallback.W;
            }
            property.SetValue(this, value);
        }
    }

    public static ThemeConfiguration Preset(int index) {
        var palette = index switch {
        1 => new() {
            Background = new(.055f, .035f, .095f, .99f), Panel = new(.105f, .075f, .16f, 1),
            Surface = new(.16f, .12f, .23f, 1), Border = new(.34f, .25f, .45f, 1),
            Accent = new(.76f, .61f, 1, 1), Highlight = new(1, .69f, .81f, 1),
            Button = new(.24f, .16f, .34f, 1), Selection = new(.32f, .22f, .46f, 1),
            TableHeader = new(.16f, .11f, .23f, 1), TableAlternate = new(.13f, .09f, .19f, 1),
        },
        2 => new() {
            Background = new(.025f, .075f, .07f, .99f), Panel = new(.045f, .12f, .11f, 1),
            Surface = new(.07f, .17f, .15f, 1), Border = new(.17f, .36f, .31f, 1),
            Accent = new(.36f, .86f, .69f, 1), Highlight = new(1, .81f, .43f, 1),
            Button = new(.08f, .25f, .20f, 1), Selection = new(.10f, .34f, .27f, 1),
            TableHeader = new(.06f, .18f, .15f, 1), TableAlternate = new(.05f, .15f, .13f, 1),
        },
        3 => new() {
            Background = new(.94f, .95f, .97f, 1), Panel = new(.98f, .985f, 1, 1),
            Surface = new(.88f, .91f, .95f, 1), Border = new(.65f, .71f, .80f, 1),
            Text = new(.10f, .15f, .23f, 1), Muted = new(.35f, .41f, .50f, 1),
            Accent = new(.10f, .38f, .66f, 1), Highlight = new(.62f, .36f, .05f, 1),
            Button = new(.81f, .87f, .94f, 1), Selection = new(.69f, .81f, .94f, 1),
            TableHeader = new(.84f, .89f, .95f, 1), TableAlternate = new(.91f, .94f, .98f, 1),
        },
        >= 4 and <= 11 => Additional(index),
        _ => new ThemeConfiguration(),
        };
        palette.ButtonHovered = palette.Accent;
        palette.ButtonPressed = palette.Highlight;
        return palette;
    }

    private static ThemeConfiguration Additional(int index) {
        var accents = new Vector4[] { new(1,.38f,.28f,1), new(1,.73f,.22f,1), new(1,.48f,.72f,1), new(.16f,.86f,.90f,1), new(.84f,.87f,.92f,1), new(.52f,.57f,1,1), new(.43f,.27f,.13f,1), new(1,.84f,.15f,1) };
        var accent = accents[index - 4];
        if(index == 10) return new() {
            Background = new(.96f,.94f,.89f,1), Panel = new(1,.98f,.94f,1), Surface = new(.90f,.86f,.78f,1),
            Text = new(.18f,.14f,.10f,1), Muted = new(.42f,.35f,.26f,1), Border = new(.65f,.56f,.43f,1),
            Accent = accent, Highlight = new(.13f,.39f,.38f,1), Button = new(.83f,.74f,.60f,1),
            Selection = new(.81f,.71f,.55f,1), TableHeader = new(.87f,.80f,.68f,1), TableAlternate = new(.94f,.90f,.82f,1),
        };
        var tint = Vector4.Lerp(new Vector4(.04f,.045f,.065f,1), accent, .035f);
        var p = new ThemeConfiguration {
            Background = tint, Panel = Vector4.Lerp(tint, accent, .055f), Surface = Vector4.Lerp(tint, accent, .11f),
            Border = Vector4.Lerp(tint, accent, .38f), Accent = accent,
            Highlight = index == 5 || index == 11 ? new(.40f,.83f,1,1) : new(1,.77f,.38f,1),
            Button = Vector4.Lerp(tint, accent, .26f), Selection = Vector4.Lerp(tint, accent, .30f),
            TableHeader = Vector4.Lerp(tint, accent, .14f), TableAlternate = Vector4.Lerp(tint, accent, .08f),
        };
        if(index == 11) { p.Background = new(0,0,0,1); p.Panel = new(.025f,.025f,.025f,1); p.Text = Vector4.One; p.Muted = new(.77f,.77f,.77f,1); }
        return p;
    }
}
