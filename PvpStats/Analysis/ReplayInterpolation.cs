using System;
using System.Linq;
using System.Numerics;

namespace PvpStats.Analysis;

public static class ReplayInterpolation {
    public static MovementFrame? Sample(DetailedTelemetry data,DateTime time,Func<string,DateTime,DateTime,bool>? crossedDeath=null) {
        var first=DetailedAnalysis.FrameAt(data,time);if(first==null)return null;
        int lo=0,hi=data.Frames.Count;
        while(lo<hi) { int mid=lo+(hi-lo)/2;if(data.Frames[mid].Timestamp<=time)lo=mid+1;else hi=mid; }
        if(lo>=data.Frames.Count)return first;
        var next=data.Frames[lo];float gap=(float)(next.Timestamp-first.Timestamp).TotalSeconds;
        if(gap<=0||gap>data.FrameGapSeconds)return first;
        float alpha=Math.Clamp((float)(time-first.Timestamp).TotalSeconds/gap,0,1);
        var result=new MovementFrame { Timestamp=time,CrystalProgress=first.CrystalProgress,AstraProgress=first.AstraProgress,UmbraProgress=first.UmbraProgress,Crystal=first.Crystal };
        foreach(var a in first.Players) {
            var b=next.Players.FirstOrDefault(p=>p.Player==a.Player);
            if(b==null||a.HP==0||b.HP==0||a.Team!=b.Team||crossedDeath?.Invoke(a.Player,first.Timestamp,next.Timestamp)==true||!CanBlend(a.X,a.Z,b.X,b.Z,gap,30)) { result.Players.Add(a);continue; }
            result.Players.Add(new PlayerPosition { Player=a.Player,Team=a.Team,X=float.Lerp(a.X,b.X,alpha),Z=float.Lerp(a.Z,b.Z,alpha),
                Y=a.Y.HasValue&&b.Y.HasValue?float.Lerp(a.Y.Value,b.Y.Value,alpha):a.Y,
                Heading=a.Heading+MathF.IEEERemainder(b.Heading-a.Heading,MathF.Tau)*alpha,
                HP=a.HP,MaxHP=a.MaxHP,ClassJobId=a.ClassJobId,Controls=a.Controls,Statuses=a.Statuses,StatusTimestamp=a.StatusTimestamp });
        }
        if(first.Crystal is {} c&&next.Crystal is {} d&&CanBlend(c.X,c.Z,d.X,d.Z,gap,8))result.Crystal=new() {
            X=float.Lerp(c.X,d.X,alpha),Z=float.Lerp(c.Z,d.Z,alpha),Y=c.Y.HasValue&&d.Y.HasValue?float.Lerp(c.Y.Value,d.Y.Value,alpha):c.Y };
        return result;
    }
    private static bool CanBlend(float x,float z,float nx,float nz,float gap,float speed) =>
        float.IsFinite(x)&&float.IsFinite(z)&&float.IsFinite(nx)&&float.IsFinite(nz)&&Vector2.Distance(new(x,z),new(nx,nz))<=speed*gap+1;
}
