using System;
using System.Linq;
using System.Numerics;
namespace PvpStats.Analysis;
public static class ReplayObservation {
    public static DateTime? Step(DetailedTelemetry data,DateTime time,int direction) => direction>0
        ? data.Frames.FirstOrDefault(f=>f.Timestamp>time.AddMilliseconds(.5))?.Timestamp
        : data.Frames.LastOrDefault(f=>f.Timestamp<time.AddMilliseconds(-.5))?.Timestamp;
    public static float Remaining(ReplayBuff status,DateTime sampledAt,DateTime time) =>
        status.Remaining>0 ? Math.Max(0,status.Remaining-(float)Math.Max(0,(time-sampledAt).TotalSeconds)) : 0;
    public static bool ReuseStatuses(PlayerPosition old,PlayerPosition current,DateTime now) {
        if(now-old.StatusTimestamp>TimeSpan.FromSeconds(1)||old.Statuses.Count!=current.Statuses.Count)return false;
        return old.Statuses.Zip(current.Statuses).All(p=>p.First.Id==p.Second.Id&&p.First.Param==p.Second.Param
            && Math.Abs(Remaining(p.First,old.StatusTimestamp,now)-Math.Max(0,p.Second.Remaining))<.15f);
    }
    public static bool CrystalSegment(MovementFrame a,MovementFrame b,double gap) => a.Crystal is {} x&&b.Crystal is {} y
        && b.Timestamp>a.Timestamp&&(b.Timestamp-a.Timestamp).TotalSeconds<=gap
        && float.IsFinite(x.X)&&float.IsFinite(x.Z)&&float.IsFinite(y.X)&&float.IsFinite(y.Z)
        && Vector2.Distance(new(x.X,x.Z),new(y.X,y.Z))<=10;
}
