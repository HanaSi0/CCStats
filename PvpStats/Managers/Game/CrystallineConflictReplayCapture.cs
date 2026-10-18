using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using PvpStats.Analysis;
namespace PvpStats.Managers.Game;
internal partial class CrystallineConflictMatchManager {
    private readonly Dictionary<uint,ReplayMapEvent> _environmentCasts=new();
    private DateTime _lastEnvironmentCastPoll;
    private static float? Finite(float value)=>float.IsFinite(value)?value:null;
    private void CaptureEnvironmentAction(DateTime now,IBattleNpc source,uint action,uint sequence,Vector3? target) {
        if(source.NameId==11350 || _plugin.ObjectTable.SearchByEntityId(source.OwnerId) is IPlayerCharacter)return;
        var data=_currentMatchTimeline?.Detailed;
        if(data==null||_scoreboardPayloadReceived||_currentMatch?.IsCompleted!=false)return;
        // Do not infer an AoE or classify unknown NPCs as confirmed map mechanics.
        var last=data.MapMechanics.LastOrDefault(e=>e.ActionId==action&&e.SourceEntityId==source.EntityId&&e.Sequence==sequence);
        if(last!=null&&(now-last.Timestamp).TotalSeconds<1)return;
        var row=_plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRow(action);
        if(_environmentCasts.TryGetValue(source.EntityId,out var cast)&&cast.ActionId==action&&cast.Timestamp<=now&&now-(cast.ObservedUntil??cast.Timestamp)<TimeSpan.FromSeconds(1))cast.ResolvedAt=now;
        RecordReplayMapEvent(new() { Timestamp=now,ActionId=action,Sequence=sequence,SourceNameId=source.NameId,SourceEntityId=source.EntityId,
            Label=MapMechanicVisuals.Find(action)?.Name??(source.Name+" · "+PvpStats.Localization.ChineseText.ActionName(action,row.Name.ToString())),
            X=Finite(source.Position.X),Y=Finite(source.Position.Y),Z=Finite(source.Position.Z),Heading=Finite(source.Rotation),
            TargetX=target.HasValue?Finite(target.Value.X):null,TargetY=target.HasValue?Finite(target.Value.Y):null,TargetZ=target.HasValue?Finite(target.Value.Z):null,
            Kind=ReplayMapEventKind.ActionResolved,ClientCastType=row.CastType,ClientEffectRange=row.EffectRange,ClientXAxisModifier=row.XAxisModifier });
    }
    private void CaptureEnvironmentCasts(DateTime now) {
        if(now-_lastEnvironmentCastPoll<TimeSpan.FromSeconds(.1))return;
        var data=_currentMatchTimeline?.Detailed;
        if(data==null||_scoreboardPayloadReceived||_currentMatch?.IsCompleted!=false)return;
        _lastEnvironmentCastPoll=now;
        foreach(var source in _plugin.ObjectTable.OfType<IBattleNpc>()) {
            if(!source.IsCasting||source.CastActionType!=1||source.CastActionId==0||source.NameId==11350||_plugin.ObjectTable.SearchByEntityId(source.OwnerId) is IPlayerCharacter)continue;
            if(!float.IsFinite(source.TotalCastTime)||!float.IsFinite(source.CurrentCastTime)||source.TotalCastTime<=0||source.TotalCastTime>600)continue;
            var end=now.AddSeconds(Math.Max(0,source.TotalCastTime-source.CurrentCastTime));
            if(_environmentCasts.TryGetValue(source.EntityId,out var old)&&old.ActionId==source.CastActionId&&!old.ResolvedAt.HasValue&&now-(old.ObservedUntil??old.Timestamp)<TimeSpan.FromSeconds(.3)&&end<=(old.ExpectedEndTime??end).AddSeconds(.5)) {
                old.ObservedUntil=now;continue;
            }
            var row=_plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRow(source.CastActionId);
            var ground=EnvironmentGround(source,source.CastActionId);
            var entry=new ReplayMapEvent { Timestamp=now,Kind=ReplayMapEventKind.CastObserved,ObservedUntil=now,ExpectedEndTime=end,
                SourceEntityId=source.EntityId,SourceNameId=source.NameId,ActionId=source.CastActionId,
                Label=MapMechanicVisuals.Find(source.CastActionId)?.Name??(source.Name+" · "+PvpStats.Localization.ChineseText.ActionName(source.CastActionId,row.Name.ToString())),
                X=Finite(source.Position.X),Y=Finite(source.Position.Y),Z=Finite(source.Position.Z),Heading=Finite(source.Rotation),
                TargetX=ground?.X,TargetY=ground?.Y,TargetZ=ground?.Z,
                ClientCastType=row.CastType,ClientEffectRange=row.EffectRange,ClientXAxisModifier=row.XAxisModifier };
            if(RecordReplayMapEvent(entry))_environmentCasts[source.EntityId]=entry;
        }
        foreach(var id in _environmentCasts.Where(p=>now-(p.Value.ObservedUntil??p.Value.Timestamp)>TimeSpan.FromSeconds(1)).Select(p=>p.Key).ToArray())_environmentCasts.Remove(id);
    }
    private static unsafe Vector3? EnvironmentGround(IBattleNpc source,uint action) {
        if(MapMechanicVisuals.Find(action)?.Ground!=true||source.Address==IntPtr.Zero)return null;
        var cast=((FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*)source.Address)->GetCastInfo();
        if(cast==null||cast->ActionType!=1||cast->ActionId!=action)return null;
        var p=cast->TargetLocation;var point=new Vector3(p.X,p.Y,p.Z);
        return LimitBreakVisuals.Finite(point)?point:null;
    }
    private bool RecordReplayMapEvent(ReplayMapEvent entry) {
        var data=_currentMatchTimeline?.Detailed;
        if(data==null||_scoreboardPayloadReceived)return false;
        lock(_detailedLock) {
            if(data.MapMechanics.Count>=4096||data.EstimatedBytes+256>DetailedTelemetry.ByteBudget) { data.MapMechanicsTruncated=true;return false; }
            data.MapMechanics.Add(entry);data.EstimatedBytes+=256;
            return true;
        }
    }
}
