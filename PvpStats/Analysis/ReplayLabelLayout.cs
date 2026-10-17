using System.Collections.Generic;
using System.Numerics;
namespace PvpStats.Analysis;
public static class ReplayLabelLayout {
    // Reposition only screen-space badges; recorded world coordinates never change.
    public static Vector2 Place(Vector2 anchor,int team,float half,Vector2 min,Vector2 max,List<Vector2> occupied) {
        var low=min+new Vector2(half+2);var high=Vector2.Max(low,max-new Vector2(half+2));
        var step=half*2+5;Vector2 best=Vector2.Clamp(anchor,low,high);
        for(int row=0;row<12;row++)for(int column=0;column<9;column++) {
            var side=team==1?-1:1;
            var x=side*(half+6+column*step);var y=half+(row==0?0:(row%2==0?-row/2f:(row+1)/2f)*step);
            var candidate=Vector2.Clamp(anchor+new Vector2(x,y),low,high);
            bool free=true;
            foreach(var at in occupied)if(System.MathF.Abs(candidate.X-at.X)<half*2+2&&System.MathF.Abs(candidate.Y-at.Y)<half*2+2) {free=false;break;}
            if(free) {occupied.Add(candidate);return candidate;}
        }
        occupied.Add(best);return best;
    }
}
