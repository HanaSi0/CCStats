using System;
using System.Numerics;
namespace PvpStats.Helpers;
public static class ViewLayout {
    public static Vector2 WindowPreset(int index,Vector2 screen) {
        var desired=(index%3) switch { 0=>new Vector2(840,620),1=>new Vector2(1120,800),_=>new Vector2(1440,960) };
        return Vector2.Min(desired,Vector2.Max(new Vector2(280,220),screen-new Vector2(32)));
    }
    public static Vector2 ClampWindow(Vector2 desired,Vector2 screen) {
        if(!float.IsFinite(desired.X)||desired.X<=0)desired.X=1120;
        if(!float.IsFinite(desired.Y)||desired.Y<=0)desired.Y=800;
        return Vector2.Clamp(desired,WindowMinimum(screen),Vector2.Max(WindowMinimum(screen),screen-new Vector2(16)));
    }
    public static Vector2 WindowMinimum(Vector2 screen) => Vector2.Min(new Vector2(600,440),Vector2.Max(new Vector2(280,220),screen-new Vector2(32)));
    public static float FilterHeight(float availableHeight,float requestedHeight,float scale) {
        if(!float.IsFinite(scale)||scale<=0)scale=1;
        if(!float.IsFinite(availableHeight)||availableHeight<=0)return 0;
        if(!float.IsFinite(requestedHeight))requestedHeight=250;
        var maximum=Math.Min(availableHeight*.35f,Math.Max(0,availableHeight-180*scale));
        return Math.Min(maximum,Math.Max(80*scale,requestedHeight*scale));
    }
}
