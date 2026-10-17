using System;
using System.Collections.Generic;
using System.Linq;

namespace PvpStats.Analysis;

// Raw event phases stay available in the log; only MapMechanicVisuals selects drawable arena mechanics.
public static class ReplayMechanicObservation {
    public static bool Visible(ReplayMapEvent e,DateTime time) {
        if(time<e.Timestamp)return false;
        if(e.Kind==ReplayMapEventKind.CastObserved) {
            var end=e.ResolvedAt??e.ObservedUntil?.AddSeconds(.15)??e.Timestamp;
            return time<=end&&(!e.ExpectedEndTime.HasValue||time<=e.ExpectedEndTime.Value.AddSeconds(.15));
        }
        if(e.Kind==ReplayMapEventKind.TimerWarning)return e.ExpectedEndTime.HasValue&&time<=(e.ResolvedAt??e.ExpectedEndTime.Value);
        return time<=e.Timestamp.AddSeconds(3);
    }
    public static IEnumerable<ReplayMapEvent> Active(IEnumerable<ReplayMapEvent> events,DateTime time) => events.Where(e=>Visible(e,time));
    public static string Caption(ReplayMapEvent e,DateTime time) => e.Kind switch {
        ReplayMapEventKind.CastObserved=>"环境读条 · "+(MapMechanicVisuals.Find(e.ActionId)?.Name??e.Label)+(e.ExpectedEndTime.HasValue?$" · {Math.Max(0,(e.ExpectedEndTime.Value-time).TotalSeconds):F1} 秒":""),
        ReplayMapEventKind.TimerWarning=>"地图计时器 · "+(e.ExpectedEndTime.HasValue?$"{Math.Max(0,(e.ExpectedEndTime.Value-time).TotalSeconds):F1} 秒":"待触发"),
        ReplayMapEventKind.TimerTriggered=>"地图计时器触发",
        _=>(MapMechanicVisuals.Find(e.ActionId)?.Name??e.Label)
    };
    public static string SourceDescription(ReplayMapEvent e) => e.Kind==ReplayMapEventKind.CastObserved
        ?"客户端环境对象读条观测；位置为施法来源，预计结束时间不是服务器结算时刻。"
        : e.MapWide?"地图计时器观测；未推定具体机制、作用范围或持续时间。"
        :"客户端环境动作观测；来源与落点坐标分开保存，未推定实际作用范围。";
}
