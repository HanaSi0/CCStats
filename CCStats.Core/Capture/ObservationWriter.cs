using System.Text.Json;
using PvpStats.Analysis;
namespace CCStats.Core.Capture;

/// <summary>Bounded streaming observations, including supplemental events. Does not fabricate a settlement.</summary>
public sealed class ObservationWriter : IDisposable {
    private readonly FileStream _file;private readonly Utf8JsonWriter _json;
    private readonly uint _territory;private readonly DateTime _start;
    private readonly DetailedTelemetry _details=new(){CaptureMethod="ReadOnlyMemoryPolling",SampleIntervalSeconds=1.0/30};
    private readonly Dictionary<(uint,uint,uint,bool),ReplaySkillEvent> _skills=new();
    private readonly Dictionary<ulong,ReplayArenaObject> _objects=new();
    private long _supplementBytes;private bool _finished;private string _localPlayer="";
    private PlayerPvpProfile? _profile;
    private readonly string _matchId=Guid.NewGuid().ToString("N")[..24];
    private DateTime? _lastFrame;
    public string? SummaryError {get;private set;}
    private bool _teamsObserved,_rosterLimited,_directorObserved,_crystalObserved,_effectsPolled;
    public long BytesWritten=>_file.Position+_json.BytesPending+_supplementBytes;
    public bool ShouldRollOver=>BytesWritten>=60L*1024*1024;
    public uint Territory=>_territory;
    public string Destination {get;}
    public int Frames {get;private set;}
    public ObservationWriter(string directory,uint territory,DateTime? start=null) {
        _start=start??DateTime.UtcNow;
        Directory.CreateDirectory(directory);_territory=territory;
        Destination=Path.Combine(directory,"Observation-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]+".json");
        _file=new FileStream(Destination+".partial",FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);_json=new Utf8JsonWriter(_file);
        _json.WriteStartObject();_json.WriteString("RecordKind","IndependentObservation");_json.WriteString("MatchId",_matchId);
        _json.WriteNumber("TerritoryId",territory);_json.WriteString("StartUtc",_start);_json.WriteString("CaptureProfile",ClientLayout.Profile);
        _json.WriteStartObject("Telemetry");_json.WriteNumber("Version",2);_json.WriteNumber("SampleIntervalSeconds",1.0/30);_json.WriteStartArray("Frames");
    }
    public void Flush() {if(!_finished){_json.Flush();_file.Flush();}}
    public void Append(uint territory,MovementFrame frame,ObservationBatch? batch=null) {
        if(_finished)throw new InvalidOperationException("记录已经结束。");
        if(territory!=_territory)throw new InvalidOperationException("地图已切换，本段记录已结束。");
        if(BytesWritten>64L*1024*1024)throw new InvalidOperationException("本段记录已达到 64 MiB，已停止以限制资源占用。");
        JsonSerializer.Serialize(_json,frame);Frames++;_lastFrame=frame.Timestamp;_teamsObserved|=frame.Players.Any(p=>p.Team is 1 or 2);_crystalObserved|=frame.Crystal!=null;
        if(batch!=null)AppendBatch(batch);
        if(Frames%30==0)_json.Flush();
    }
    private void AppendBatch(ObservationBatch batch) {
        _details.GameVersion=batch.GameVersion;if(batch.Profile!=null)_profile=batch.Profile;if(batch.LocalPlayer.Length>0)_localPlayer=batch.LocalPlayer;
        _rosterLimited|=batch.UnconfirmedRosterLimited;_directorObserved|=batch.DirectorAvailable;_effectsPolled|=batch.EffectsPolled;
        // Budget each channel independently. Keep recording movement when any event channel fills.
        foreach(var effect in batch.Effects) {
            if(_details.Effects.Count>=50000||_supplementBytes>=8L*1024*1024) {_details.EffectsTruncated=true;break;}
            _details.Effects.Add(effect);_supplementBytes+=512;
        }
        foreach(var skill in batch.Skills) {
            var key=(skill.Sequence,skill.SourceEntityId,skill.ActionId,skill.Resolved);
            if(_skills.TryGetValue(key,out var old)) {foreach(var id in skill.SelfStatusIds)if(!old.SelfStatusIds.Contains(id))old.SelfStatusIds.Add(id);foreach(var target in skill.SummonTargets)if(!old.SummonTargets.Any(t=>t.Player==target.Player&&t.Benefit==target.Benefit))old.SummonTargets.Add(target);continue;}
            if(_details.ReplaySkills.Count>=8192||_supplementBytes>=10L*1024*1024) {_details.ReplaySkillsTruncated=true;break;}
            _skills[key]=skill;_details.ReplaySkills.Add(skill);_supplementBytes+=768;
        }
        foreach(var entry in batch.MapEvents) {
            if(_details.MapMechanics.Count>=8192) {_details.MapMechanicsTruncated=true;break;}
            if(entry.Kind==ReplayMapEventKind.ActionResolved) {
                var cast=_details.MapMechanics.LastOrDefault(e=>e.Kind==ReplayMapEventKind.CastObserved&&e.SourceEntityId==entry.SourceEntityId&&e.ActionId==entry.ActionId&&!e.ResolvedAt.HasValue&&e.Timestamp<=entry.Timestamp&&(entry.Timestamp-(e.ObservedUntil??e.Timestamp)).TotalSeconds<1);
                if(cast!=null)cast.ResolvedAt=entry.Timestamp;
            }
            _details.MapMechanics.Add(entry);_supplementBytes+=512;
        }
        var seen=new HashSet<ulong>();
        foreach(var obj in batch.ArenaObjects) {
            seen.Add(obj.ObjectId);
            if(_objects.TryGetValue(obj.ObjectId,out var last)&&obj.Timestamp-last.ObservedUntil<TimeSpan.FromSeconds(.5)&&last.EntityId==obj.EntityId&&last.DataId==obj.DataId&&last.Kind==obj.Kind&&last.EventState==obj.EventState&&last.RenderFlags==obj.RenderFlags&&Math.Abs(last.X-obj.X)<.02&&Math.Abs(last.Y-obj.Y)<.02&&Math.Abs(last.Z-obj.Z)<.02&&Math.Abs(last.Heading-obj.Heading)<.02) {last.ObservedUntil=obj.ObservedUntil;continue;}
            if(_details.ArenaObjects.Count>=16384) {_details.ArenaObjectsTruncated=true;break;}
            _objects[obj.ObjectId]=obj;_details.ArenaObjects.Add(obj);_supplementBytes+=384;
        }
        if(batch.Summons is {} summons) {
            foreach(var id in _objects.Keys.Where(id=>!seen.Contains(id)).ToArray())_objects.Remove(id);
            if(_details.SummonFrames.Count>=8192)_details.SummonsTruncated=true;
            else if(summons.Summons.Count>0||_details.SummonFrames.LastOrDefault()?.Summons.Count>0) {_details.SummonFrames.Add(summons);_supplementBytes+=128+summons.Summons.Count*256;}
        }
        foreach(var state in batch.MapStates)if(_details.ArenaStates.Count<16384) {_details.ArenaStates.Add(state);_supplementBytes+=128;}
    }
    public void Dispose() {
        if(_finished)return;_finished=true;
        try {
            _json.WriteEndArray();
            _details.CaptureWarnings.Add("内存轮询无法保证完整瞬时事件；事件时间和施法位置为首次读取值。未捕获官方结算，记录数据不参与胜率统计。");
            _details.CaptureWarnings.Add("记录的状态不能替代命中瞬间快照；防御减伤与免控精确推算不可用。");
            if(_details.EffectsTruncated||_details.ReplaySkillsTruncated||_details.MapMechanicsTruncated||_details.ArenaObjectsTruncated||_details.SummonsTruncated)_details.CaptureWarnings.Add("部分事件通道达到容量限制，相关统计只代表已保留的记录。");
            if(_rosterLimited)_details.CaptureWarnings.Add("参赛名单未确认时有超过十名角色，位置记录仅保留前十名已加载角色。");
            if(!_teamsObserved)_details.CaptureWarnings.Add("未确认队伍归属。");
            if(!_directorObserved)_details.CaptureWarnings.Add("未确认推进状态。");
            if(!_crystalObserved)_details.CaptureWarnings.Add("未读取到唯一水晶对象。");
            if(!_effectsPolled)_details.CaptureWarnings.Add("未读取到动作效果槽。");
            Write("DirectorObserved",_directorObserved);Write("EffectsPolled",_effectsPolled);Write("GameVersion",_details.GameVersion);Write("CaptureMethod",_details.CaptureMethod);Write("CaptureWarnings",_details.CaptureWarnings);
            Write("Effects",_details.Effects);Write("EffectsTruncated",_details.EffectsTruncated);Write("ReplaySkills",_details.ReplaySkills);Write("ReplaySkillsTruncated",_details.ReplaySkillsTruncated);
            Write("MapMechanics",_details.MapMechanics);Write("MapMechanicsTruncated",_details.MapMechanicsTruncated);Write("ArenaObjects",_details.ArenaObjects);Write("ArenaObjectsTruncated",_details.ArenaObjectsTruncated);
            Write("SummonFrames",_details.SummonFrames);Write("SummonsTruncated",_details.SummonsTruncated);Write("ArenaStates",_details.ArenaStates);
            _json.WriteEndObject();Write("ClientProfile",_profile);_json.WriteString("LocalPlayer",_localPlayer);_json.WriteString("MatchEndTime",_lastFrame.HasValue&&_lastFrame>_start?_lastFrame.Value:_start);_json.WriteEndObject();_json.Flush();
        } finally {_json.Dispose();_file.Dispose();}
        File.Move(Destination+".partial",Destination);
        try {
            var effects=_details.Effects;
            var actions=effects.GroupBy(e=>new {e.Actor,e.ActionId}).Select(g=>new {g.Key.Actor,g.Key.ActionId,Effects=g.Count(),Damage=g.Where(e=>DetailedAnalysis.IsDamage(e.Type)).Sum(e=>(long)e.Amount),Healing=g.Where(e=>e.Type==4).Sum(e=>(long)e.Amount),SelfReturnHealing=g.Where(e=>e.Type==4&&e.SelfReturn).Sum(e=>(long)e.Amount),StatusHits=g.Count(e=>e.Type is 14 or 15),StatusResists=g.Count(e=>e.Type is 20 or 55)}).ToArray();
            var received=effects.GroupBy(e=>e.Target).Select(g=>new {Player=g.Key,Damage=g.Where(e=>DetailedAnalysis.IsDamage(e.Type)).Sum(e=>(long)e.Amount),Healing=g.Where(e=>e.Type==4).Sum(e=>(long)e.Amount)}).ToArray();
            CaptureDiagnostics.SaveObservationSummary(Path.GetDirectoryName(Destination)!,_matchId,new {MatchId=_matchId,File=Path.GetFileName(Destination),Territory=_territory,LocalPlayer=_localPlayer,StartUtc=_start,LastFrameUtc=_lastFrame,Frames,GameVersion=_details.GameVersion,CaptureMethod=_details.CaptureMethod,Effects=effects.Count,_details.EffectsTruncated,EventTimeSnapshots=effects.Count(e=>e.SnapshotsCaptured),Actions=actions,Received=received,Note="动作槽记录汇总，不代表完整结算；持续伤害/治疗、过量治疗和漏事件等可能造成差异。"});
        } catch(Exception error) when(error is IOException or UnauthorizedAccessException) {SummaryError=error.Message;}

    }
    private void Write<T>(string name,T value) {_json.WritePropertyName(name);JsonSerializer.Serialize(_json,value);}
}
