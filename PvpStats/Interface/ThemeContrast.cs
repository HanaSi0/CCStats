using System;
using System.Numerics;

namespace PvpStats.Interface;

public static class ThemeContrast {
    // A small margin keeps contrast >= 4.5 after the draw list rounds colors to 8-bit channels.
    private const double ReadableRatio = 4.6;
    public static double Luminance(Vector4 color) {
        static double Linear(float value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        return .2126 * Linear(color.X) + .7152 * Linear(color.Y) + .0722 * Linear(color.Z);
    }
    public static double Ratio(Vector4 a, Vector4 b) {
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    public static Vector4 Foreground(Vector4 background, Vector4 preferred) {
        if(Ratio(background, preferred) >= ReadableRatio) return preferred with { W = 1 };
        var black = new Vector4(0, 0, 0, 1);
        var white = Vector4.One;
        return Ratio(background, black) >= Ratio(background, white) ? black : white;
    }
    public static Vector4 BackgroundForText(Vector4 background, Vector4 text) {
        if(Ratio(background, text) >= ReadableRatio) return background;
        var black = new Vector4(0,0,0,1); var white = Vector4.One;
        var target = Ratio(black, text) > Ratio(white, text) ? black : white;
        float low = 0, high = 1;
        // Find the nearest readable shade without visible color steps.
        for(var step = 0; step < 16; step++) {
            var middle = (low + high) * .5f;
            if(Ratio(Vector4.Lerp(background, target, middle), text) >= ReadableRatio) high = middle;
            else low = middle;
        }
        return Vector4.Lerp(background, target, high);
    }
    public static (Vector4 Background, Vector4 Foreground) Interaction(Vector4 normal, Vector4 hovered, Vector4 pressed,
        float hover, float press, Vector4 preferredText) {
        var foreground = Foreground(normal, preferredText);
        var background = Vector4.Lerp(Vector4.Lerp(normal, hovered, hover), pressed, press);
        return (BackgroundForText(background, foreground), foreground);
    }
    // Exponential easing is independent of frame rate and never changes layout geometry.
    public static float Ease(float current, float target, float deltaSeconds, bool enabled, float seconds = .10f) =>
        !enabled ? target : float.Lerp(current, target, 1 - MathF.Exp(-Math.Clamp(deltaSeconds, 0, .1f) / Math.Clamp(seconds, .02f, .3f)));
}
