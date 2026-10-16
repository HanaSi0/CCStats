using System;
using System.Numerics;

namespace PvpStats.Interface;

public static class ThemeContrast {
    public static double Luminance(Vector4 color) {
        static double Linear(float value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        return .2126 * Linear(color.X) + .7152 * Linear(color.Y) + .0722 * Linear(color.Z);
    }
    public static double Ratio(Vector4 a, Vector4 b) {
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    public static Vector4 Foreground(Vector4 background, Vector4 preferred) {
        if(Ratio(background, preferred) >= 4.5) return preferred with { W = 1 };
        var black = new Vector4(0, 0, 0, 1);
        var white = Vector4.One;
        return Ratio(background, black) >= Ratio(background, white) ? black : white;
    }
    public static Vector4 BackgroundForText(Vector4 background, Vector4 text) {
        if(Ratio(background, text) >= 4.5) return background;
        var black = new Vector4(0,0,0,1); var white = Vector4.One;
        var target = Ratio(black, text) > Ratio(white, text) ? black : white;
        for(var step = 1; step <= 16; step++) {
            var candidate = Vector4.Lerp(background, target, step / 16f);
            if(Ratio(candidate, text) >= 4.5) return candidate;
        }
        return target;
    }
    // Exponential easing is independent of frame rate and never changes layout geometry.
    public static float Ease(float current, float target, float deltaSeconds, bool enabled) =>
        !enabled ? target : float.Lerp(current, target, 1 - MathF.Exp(-Math.Clamp(deltaSeconds, 0, .1f) / .075f));
}
