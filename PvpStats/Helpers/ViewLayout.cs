using System;
namespace PvpStats.Helpers;
public static class ViewLayout {
    public static float FilterHeight(float availableHeight,float requestedHeight,float scale) {
        if(!float.IsFinite(scale)||scale<=0)scale=1;
        if(!float.IsFinite(availableHeight)||availableHeight<=0)return 0;
        if(!float.IsFinite(requestedHeight))requestedHeight=250;
        var maximum=Math.Min(availableHeight*.35f,Math.Max(0,availableHeight-180*scale));
        return Math.Min(maximum,Math.Max(80*scale,requestedHeight*scale));
    }
}
