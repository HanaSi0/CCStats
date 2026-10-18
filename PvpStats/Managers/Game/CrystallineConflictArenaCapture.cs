using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using PvpStats.Analysis;

namespace PvpStats.Managers.Game;
internal partial class CrystallineConflictMatchManager {
    private DateTime _lastArenaPoll;
    private readonly Dictionary<ulong,ReplayArenaObject> _arenaTracks=new();
    private readonly Dictionary<(bool Npc,uint Id),string> _arenaNames=new();
    private string ArenaEnglishName(bool npc,uint id) {
        if(_arenaNames.TryGetValue((npc,id),out var old))return old;
        var name="";
        try {
            name=npc?_plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.BNpcName>(ClientLanguage.English).GetRow(id).Singular.ToString()
                :_plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.EObjName>(ClientLanguage.English).GetRow(id).Singular.ToString();
        }catch(ArgumentException) { }catch(KeyNotFoundException) { }
        if(_arenaNames.Count<1024)_arenaNames[(npc,id)]=name;
        return name;
    }
    private unsafe void CaptureArenaObjects(DateTime now) {
        var data=_currentMatchTimeline?.Detailed;
        if(data==null||_currentMatch?.IsCompleted!=false||_scoreboardPayloadReceived||(now-_lastArenaPoll).TotalSeconds<.2)return;
        _lastArenaPoll=now;var map=ReplayMapMesh.MapKey(_currentMatch.TerritoryId);var seen=new HashSet<ulong>();
        lock(_detailedLock)foreach(var obj in _plugin.ObjectTable) {
            if(obj is IPlayerCharacter||obj.Address==IntPtr.Zero||!LimitBreakVisuals.Finite(obj.Position)||!float.IsFinite(obj.Rotation))continue;
            bool npc=obj is IBattleNpc;uint nameId=npc?((IBattleNpc)obj).NameId:0;
            if(npc&&(nameId==11350||_plugin.ObjectTable.SearchByEntityId(obj.OwnerId) is IPlayerCharacter))continue;
            // EventObj=7. Keep unclassified event object identities for later verified catalog additions.
            if(!npc&&(byte)obj.ObjectKind!=7)continue;
            var kind=ArenaMechanicScene.FromNpc(nameId,map);
            if(kind==ArenaMechanicKind.Unknown)kind=ArenaMechanicScene.FromObjectName(ArenaEnglishName(npc,npc?nameId:obj.BaseId),map);
            if(npc&&kind==ArenaMechanicKind.Unknown)continue;
            if(seen.Count>=96)break;
            ulong id=obj.GameObjectId;if(id==0||!seen.Add(id))continue;
            var native=(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
            byte state=native->EventState;ulong renderFlags=(ulong)native->RenderFlags;
            if(_arenaTracks.TryGetValue(id,out var last)&&(now-last.ObservedUntil).TotalSeconds<.5&&last.Kind==kind&&last.NameId==nameId&&last.EntityId==obj.EntityId&&last.ObjectKind==(byte)obj.ObjectKind&&last.EventState==state&&last.RenderFlags==renderFlags&&last.DataId==obj.BaseId&&Vector3.DistanceSquared(last.Position(),obj.Position)<.04f&&MathF.Abs(MathF.IEEERemainder(last.Heading-obj.Rotation,MathF.Tau))<.1f) {
                last.ObservedUntil=now;continue;
            }
            if(data.ArenaObjects.Count>=16384||data.EstimatedBytes+192>DetailedTelemetry.ByteBudget) { data.ArenaObjectsTruncated=true;continue; }
            var entry=new ReplayArenaObject { Timestamp=now,ObservedUntil=now,ObjectId=id,EntityId=obj.EntityId,NameId=nameId,DataId=obj.BaseId,ObjectKind=(byte)obj.ObjectKind,EventState=state,RenderFlags=renderFlags,Kind=kind,
                X=obj.Position.X,Y=obj.Position.Y,Z=obj.Position.Z,Heading=obj.Rotation };
            data.ArenaObjects.Add(entry);data.EstimatedBytes+=192;_arenaTracks[id]=entry;
        }
        foreach(var id in _arenaTracks.Keys.Where(id=>!seen.Contains(id)).ToArray())_arenaTracks.Remove(id);
    }
}
