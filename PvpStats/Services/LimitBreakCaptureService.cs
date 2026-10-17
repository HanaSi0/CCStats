using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using PvpStats.Types.Player;
using PvpStats.Types.Action;

namespace PvpStats.Services;

// Explicit opt-in diagnostic capture; independent of match recording and never uploaded.
internal sealed class LimitBreakCaptureService : IDisposable {
    private readonly Plugin _plugin;
    private readonly string _root;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private DateTime _started, _lastSample;
    private uint _territory, _job;
    private int _samples, _events;
    private long _bytes;
    private string? _session;
    private readonly HashSet<uint> _actions = new();
    private readonly HashSet<uint> _breaks = new();
    private Task? _export;
    public bool Active => _writer != null;
    public bool ExportBusy => _export is { IsCompleted: false };
    public bool HookReady { get; set; }
    public string Status { get; private set; } = "未开始";
    public string ExportStatus { get; private set; } = "";
    public string? ExportPath { get; private set; }
    public int Samples => _samples;
    public int Events => _events;
    public int Breaks => _breaks.Count;
    public string Root => _root;
    public string[] Sessions => Directory.Exists(_root) ? Directory.GetFiles(_root,"summary.json",SearchOption.AllDirectories).OrderBy(x=>x).ToArray() : Array.Empty<string>();
    public LimitBreakCaptureService(Plugin plugin) {
        _plugin=plugin;_root=Path.Combine(plugin.PluginInterface.GetPluginConfigDirectory(),"lb-captures");
        plugin.Framework.Update+=Update;
    }
    public void Start() {
        lock(_gate) {
            if(Active||ExportBusy)return;
            var local=_plugin.ObjectTable.LocalPlayer;
            if(!HookReady) { Status="动作采集接口未就绪，请查看插件日志";return; }
            if(local==null) { Status="请先登录并进入狼狱练习场";return; }
            // The Wolves' Den Pier; avoid recording during normal play by accident.
            if(_plugin.ClientState.TerritoryType!=250) { Status="请进入狼狱停船场后开始采集";return; }
            try {
                _started=DateTime.UtcNow;_lastSample=DateTime.MinValue;
                _territory=_plugin.ClientState.TerritoryType;_job=local.ClassJob.RowId;
                _samples=0;_events=0;_bytes=0;_actions.Clear();_breaks.Clear();
                _session=Path.Combine(_root,_started.ToString("yyyyMMdd-HHmmss-fff")+"-job"+_job);
                Directory.CreateDirectory(_session);
                _writer=new StreamWriter(Path.Combine(_session,"observations.ndjson"),false,new System.Text.UTF8Encoding(false),65536);
                Write(new {kind="header",schema=1,pluginVersion=typeof(Plugin).Assembly.GetName().Version?.ToString(),startedUtc=_started,territory=_territory,job=_job,localEntityId=local.EntityId,
                    note="Client observations only; absence of effects does not prove a miss. 30Hz target, dependent on game frame rate. No automatic upload."});
                Status="采集中：请释放 LB 及全部后续技能";
            } catch(Exception e) { Fail(e); }
        }
    }
    private void Write(object value) {
        if(_writer==null)return;
        var line=JsonSerializer.Serialize(value);_writer.WriteLine(line);_bytes+=System.Text.Encoding.UTF8.GetByteCount(line)+1;
    }
    private object Entity(IBattleChara actor,bool statuses) => new {
        entityId=actor.EntityId,ownerId=actor.OwnerId,name=actor.Name.ToString(),
        nameId=actor is IBattleNpc npc?npc.NameId:0u,job=actor.ClassJob.RowId,
        x=actor.Position.X,y=actor.Position.Y,z=actor.Position.Z,heading=actor.Rotation,
        hp=actor.CurrentHp,maxHp=actor.MaxHp,mp=actor.CurrentMp,maxMp=actor.MaxMp,
        targetId=actor.TargetObjectId,shield=actor.ShieldPercentage,
        statuses=statuses?actor.StatusList.Select(s=>new {id=s.StatusId,param=s.Param,remaining=s.RemainingTime,sourceId=s.SourceId}).ToArray():null,
        casting=actor.IsCasting,castActionId=actor.CastActionId,castTime=actor.CurrentCastTime,totalCastTime=actor.TotalCastTime
    };
    private void Update(IFramework _) {
        if(!Active)return;
        lock(_gate)try {
            var now=DateTime.UtcNow;var local=_plugin.ObjectTable.LocalPlayer;
            if(local==null||_plugin.ClientState.TerritoryType!=_territory||local.ClassJob.RowId!=_job) { Stop("已停止：切换职业、区域或登出");return; }
            if((now-_started).TotalMinutes>=10||_bytes>=64*1024*1024||_events>=20000) { Stop("已达到单组采集上限");return; }
            if((now-_lastSample).TotalSeconds<1d/30)return;
            _lastSample=now;
            var entities=_plugin.ObjectTable.OfType<IBattleChara>()
                .Where(x=>Vector3.DistanceSquared(x.Position,local.Position)<=100*100)
                .OrderBy(x=>x.EntityId==local.EntityId?0:1).Take(64).Select(x=>Entity(x,_samples%3==0)).ToArray();
            Write(new {kind="frame",utc=now,entities});_samples++;
            if(_samples%150==0)_writer?.Flush();
        } catch(Exception e) { Fail(e); }
    }
    public unsafe void Observe(uint source,Vector3* targetPos,ActionEffectHandler.Header* header,ActionEffectHandler.TargetEffects* effects,GameObjectId* targets) {
        if(!Active||header==null)return;
        lock(_gate)try {
            if(!Active)return;
            var local=_plugin.ObjectTable.LocalPlayer;var actor=_plugin.ObjectTable.SearchByEntityId(source) as IBattleChara;
            if(local==null||_plugin.ClientState.TerritoryType!=_territory||local.ClassJob.RowId!=_job) { Stop("已停止：切换职业、区域或登出");return; }
            if(actor==null||!(source==local.EntityId||actor.OwnerId==local.EntityId))return;
            if(_bytes>=64*1024*1024||_events>=20000) { Stop("已达到单组采集上限");return; }
            var id=(uint)header->SpellId;
            if(_actions.Add(id)) {
                var row=_plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRow(id);
                Write(new {kind="actionMetadata",actionId=id,name=row.Name.ToString(),castType=row.CastType,effectRange=row.EffectRange,xAxisModifier=row.XAxisModifier,range=row.Range});
            }
            if(Enum.IsDefined(typeof(LimitBreak),id))_breaks.Add(id);
            var hits=new List<object>();
            if(effects!=null&&targets!=null)for(int i=0;i<Math.Min((int)header->NumTargets,32);i++) {
                uint target=(uint)(targets[i]&uint.MaxValue);var targetActor=_plugin.ObjectTable.SearchByEntityId(target) as IBattleChara;
                var raw=new List<object>();
                for(int j=0;j<8;j++) {
                    var e=effects[i].Effects[j];if(e.Type==0)continue;
                    raw.Add(new {type=(byte)e.Type,value=e.Value,p0=e.Param0,p1=e.Param1,p2=e.Param2,p3=e.Param3,p4=e.Param4});
                }
                hits.Add(new {entityId=target,snapshot=targetActor!=null?Entity(targetActor,true):null,effects=raw});
            }
            Write(new {kind="action",utc=DateTime.UtcNow,actionId=id,sequence=header->GlobalSequence,variation=header->AnimationVariation,rotation=header->RotationInt,sourceSequence=header->SourceSequence,actionType=header->ActionType,
                source=Entity(actor,true),targetPosition=targetPos!=null?new {x=targetPos->X,y=targetPos->Y,z=targetPos->Z}:null,reportedTargets=header->NumTargets,targets=hits});
            _events++;
        } catch(Exception e) { Fail(e); }
    }
    public void Stop(string reason="已停止并保存") {
        lock(_gate) {
            if(_writer==null)return;
            try {
                _writer.Dispose();_writer=null;
                File.WriteAllText(Path.Combine(_session!,"summary.json"),JsonSerializer.Serialize(new {schema=1,startedUtc=_started,endedUtc=DateTime.UtcNow,territory=_territory,job=_job,samples=_samples,events=_events,lbActionIds=_breaks.OrderBy(x=>x).ToArray(),actionIds=_actions.OrderBy(x=>x).ToArray(),reason}));
                Status=reason;
            } catch(Exception e) { Fail(e); }
        }
    }
    private void Fail(Exception e) { try {_writer?.Dispose();}catch{} _writer=null;Status="采集失败："+e.Message;_plugin.Log.Error(e,"LB capture failed"); }
    public void Export() {
        if(Active||ExportBusy)return;
        ExportStatus="正在导出…";
        _export=Task.Run(()=> {
            try {
                var sessions=Sessions;
                if(sessions.Length==0) { ExportStatus="没有已完成的采集记录";return; }
                var path=Path.Combine(_root,"LB-captures-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".zip");
                PvpStats.Analysis.LBCaptureArchive.Export(_root,path,(done,total)=>ExportStatus=$"正在导出 {done}/{total} 组");
                ExportPath=path;ExportStatus="导出完成："+sessions.Length+" 组";
            } catch(Exception e) { ExportStatus="导出失败："+e.Message;_plugin.Log.Error(e,"LB export failed"); }
        });
    }
    public void Dispose() { _plugin.Framework.Update-=Update;Stop("插件卸载，已保存"); }
}
