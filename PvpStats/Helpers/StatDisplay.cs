using System;
namespace PvpStats.Helpers;
public static class StatDisplay {
    public static string Number(double value, bool available=true, string format="N0") => available && double.IsFinite(value) ? value.ToString(format) : "—";
    public static string Duration(double seconds, bool available=true) {
        if(!available || !double.IsFinite(seconds) || seconds<0 || seconds>TimeSpan.MaxValue.TotalSeconds-1) return "—";
        var time=TimeSpan.FromSeconds(seconds);
        return $"{(long)time.TotalMinutes}:{time.Seconds:00}";
    }
    public static int CardColumns(float width,float scale) => Math.Clamp((int)((width+12*scale)/(300*scale+12*scale)),1,3);
}
