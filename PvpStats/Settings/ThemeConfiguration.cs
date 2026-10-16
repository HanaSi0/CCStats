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
    public Vector4 Selection { get; set; } = new(.14f, .27f, .42f, 1);
    public Vector4 TableHeader { get; set; } = new(.09f, .15f, .24f, 1);
    public Vector4 TableAlternate { get; set; } = new(.10f, .16f, .24f, 1);

    public ThemeConfiguration Copy() => (ThemeConfiguration)MemberwiseClone();

    public void Normalize() {
        var defaults = new ThemeConfiguration();
        foreach(var property in typeof(ThemeConfiguration).GetProperties()) {
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

    public static ThemeConfiguration Preset(int index) => index switch {
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
        _ => new(),
    };
}
