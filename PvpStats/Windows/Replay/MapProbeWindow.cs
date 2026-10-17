using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.Sheets;
using PvpStats.Analysis;
using PvpStats.Helpers;
using PvpStats.Interface;
using PvpStats.Types.Match;
using PvpStats.Types.Match.Timeline;
namespace PvpStats.Windows.Replay;
internal sealed class MapProbeWindow : Window {
    internal const string Title="地图试验###CCMapProbe";
    private readonly Plugin _plugin;
    private readonly CrystalRangeProbe _range=new();
    private DateTime _last;
    private ProbeCapture _capture=new();
    private string _search="",_status="";
    private bool _saving;
    private sealed class ProbeObject {
        public uint EntityId { get; set; }
        public uint DataId { get; set; }
        public uint NameId { get; set; }
        public string Name { get; set; }="";
        public string Kind { get; set; }="";
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Heading { get; set; }
        public float HitboxRadius { get; set; }
        public uint HP { get; set; }
        public uint MaxHP { get; set; }
        public uint ClassJobId { get; set; }
        public uint CastActionId { get; set; }
        public List<ReplayBuff> Statuses { get; set; }=new();
        public Vector3 Position()=>new(X,Y,Z);
    }
    private sealed class ProbeCapture {
        public DateTime Timestamp { get; set; }
        public uint Territory { get; set; }
        public uint MapId { get; set; }
        public string Background { get; set; }="";
        public string MapTexture { get; set; }="";
        public int MapSizeFactor { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public ProbeObject? LocalPlayer { get; set; }
        public List<ProbeObject> Objects { get; set; }=new();
        public bool Limited { get; set; }
    }
    internal MapProbeWindow(Plugin plugin):base(Title) {
        _plugin=plugin;IsOpen=true;Size=new Vector2(1120,800);SizeCondition=ImGuiCond.FirstUseEver;
    }
    public override void PreDraw()=>WindowChrome.Constrain(this);
    public override void OnClose()=>_plugin.WindowManager.RemoveWindow(this);
    private ProbeObject Read(IGameObject obj) {
        var character=obj as IBattleChara;
        return new() { EntityId=obj.EntityId,DataId=obj.BaseId,NameId=(obj as IBattleNpc)?.NameId??0,Name=obj.Name.ToString(),Kind=obj.ObjectKind.ToString(),
            X=obj.Position.X,Y=obj.Position.Y,Z=obj.Position.Z,Heading=obj.Rotation,HitboxRadius=obj.HitboxRadius,
            HP=character?.CurrentHp??0,MaxHP=character?.MaxHp??0,ClassJobId=character?.ClassJob.RowId??0,CastActionId=character?.CastActionId??0,
            Statuses=character?.StatusList.Where(s=>s.StatusId!=0).Take(60).Select(s=>new ReplayBuff { Id=s.StatusId,Param=s.Param,Remaining=s.RemainingTime }).ToList()??new() };
    }
    private void Capture() {
        _last=DateTime.UtcNow;
        try {
            var capture=new ProbeCapture { Timestamp=_last,Territory=_plugin.ClientState.TerritoryType,MapId=_plugin.ClientState.MapId };
            var local=_plugin.ObjectTable.LocalPlayer;
            if(local!=null)capture.LocalPlayer=Read(local);
            var loaded=_plugin.ObjectTable.Where(o=>ReplayScene3D.Finite(o.Position)).OrderBy(o=>o is IBattleNpc npc&&npc.NameId==11350?0:o is IPlayerCharacter?1:2).ToArray();
            capture.Limited=loaded.Length>256;
            foreach(var obj in loaded.Take(256))capture.Objects.Add(Read(obj));
            try {
                capture.Background=_plugin.DataManager.GetExcelSheet<TerritoryType>().GetRow(capture.Territory).Bg.ToString();
                var map=_plugin.DataManager.GetExcelSheet<Map>().GetRow(capture.MapId);var id=map.Id.ToString();
                capture.MapSizeFactor=map.SizeFactor;capture.OffsetX=map.OffsetX;capture.OffsetY=map.OffsetY;
                if(id.Length>0)capture.MapTexture=$"ui/map/{id}/{id.Replace("/","")}_m.tex";
            } catch { }
            _capture=capture;
            var crystals=capture.Objects.Where(o=>o.NameId==11350).ToArray();
            _range.Scope(capture.Territory,crystals.Length==1?crystals[0].EntityId:0);
        } catch(Exception e) { _status="读取失败，请查看插件日志。";_plugin.Log.Error(e,"Map probe capture failed."); }
    }
    public override void Draw() {
        ImGui.SetWindowFontScale(1);
        if(DateTime.UtcNow-_last>TimeSpan.FromMilliseconds(200))Capture();
        var scale=ImGuiHelpers.GlobalScale;
        if(ModernTheme.Button("当前地图 3D 试验###Probe3D")) { Capture();OpenSnapshot(); }
        ImGui.SameLine();if(ModernTheme.Button("立即采样###ProbeSample"))Capture();
        ImGui.SameLine();using(var disabled=ImRaii.Disabled(_saving))if(ModernTheme.Button("导出试验数据###ProbeExport"))Export();
        ImGui.TextWrapped("只读当前已加载对象；不会进入副本、改动游戏状态或写入正式战绩。数据每秒刷新 5 次。");
        ImGui.TextUnformatted($"区域 {_capture.Territory} · 地图 {_capture.MapId} · 对象 {_capture.Objects.Count}{(_capture.Limited?"（达到显示上限）":"")}");
        ImGui.TextWrapped("场景资源："+_capture.Background);
        ImGui.TextWrapped($"地图投影：倍率 {_capture.MapSizeFactor} · 偏移 {_capture.OffsetX}, {_capture.OffsetY}");
        var local=_capture.LocalPlayer;
        if(local!=null)ImGui.TextUnformatted($"本人 XYZ：{local.X:F3}, {local.Y:F3}, {local.Z:F3} · 朝向 {local.Heading:F3}");
        else ImGui.TextDisabled("请先登录并进入可访问的地图。");
        var crystals=_capture.Objects.Where(o=>o.NameId==11350).ToArray();var crystal=crystals.Length==1?crystals[0]:null;
        ImGui.Separator();ImGui.TextUnformatted("水晶圈边缘采样");
        if(crystal!=null)ImGui.TextWrapped($"战术水晶 XYZ：{crystal.X:F3}, {crystal.Y:F3}, {crystal.Z:F3} · 对象碰撞半径 {crystal.HitboxRadius:F3}（不是已确认的护送圈半径）");
        else ImGui.TextDisabled(crystals.Length==0?"未加载战术水晶（NameId 11350），仍可试验地图模型。":"存在多个水晶对象，暂不进行半径测量。");
        if(crystal!=null&&local!=null)ImGui.TextUnformatted($"距水晶：水平 {Vector2.Distance(new(local.X,local.Z),new(crystal.X,crystal.Z)):F3} · 高差 {Math.Abs(local.Y-crystal.Y):F3}");
        using(var disabled=ImRaii.Disabled(crystal==null||local==null)) {
            if(ModernTheme.Button("标记此处在圈内###ProbeInside"))Mark(true);
            ImGui.SameLine();if(ModernTheme.Button("标记此处在圈外###ProbeOutside"))Mark(false);
        }
        ImGui.SameLine();if(ModernTheme.Button("清空边缘采样###ClearProbeRange"))_range.Clear();
        ImGui.TextWrapped("请观察游戏实际护送圈，在同一高度的圈内外边缘分别标记；这里只记录人工判读，不能证明服务端判定。");
        ImGui.TextUnformatted($"圈内最远：{_range.InsideBound?.ToString("F3")??"—"} · 圈外最近：{_range.OutsideBound?.ToString("F3")??"—"} · 标记 {_range.Samples.Count}");
        if(_range.ReferenceRadius is {} radius)ImGui.TextColored(ModernTheme.Gold,$"参考半径 {radius:F3}；将用于本页 3D 试验圈，尚未确认为真实判定。");
        else if(_range.InsideBound.HasValue&&_range.OutsideBound.HasValue)ImGui.TextColored(ModernTheme.Gold,"圈内外标记相互矛盾，请清空后重新在边缘采样。");
        if(_status.Length>0)ImGui.TextWrapped(_status);
        ImGui.Separator();ImGui.SetNextItemWidth(-1);ImGui.InputTextWithHint("###ProbeObjectSearch","搜索名称、对象 ID 或名称 ID",ref _search,100);
        using var table=ImRaii.Table("ProbeObjects",8,ImGuiTableFlags.ScrollY|ImGuiTableFlags.ScrollX|ImGuiTableFlags.RowBg|ImGuiTableFlags.Resizable,new Vector2(0,Math.Max(1,ImGui.GetContentRegionAvail().Y)));
        if(!table)return;
        foreach(var column in new[]{("对象",170f),("类型",100f),("实体 ID",100f),("名称 ID",80f),("XYZ",220f),("碰撞半径",85f),("体力 / 状态",150f),("读条技能 ID",100f)})ImGui.TableSetupColumn(column.Item1,ImGuiTableColumnFlags.WidthFixed,column.Item2*scale);
        ImGui.TableSetupScrollFreeze(1,1);ImGui.TableHeadersRow();
        foreach(var obj in _capture.Objects.Where(o=>_search.Length==0||o.Name.Contains(_search,StringComparison.OrdinalIgnoreCase)||o.EntityId.ToString().Contains(_search)||o.NameId.ToString().Contains(_search))) {
            ImGui.TableNextRow();ImGui.TableNextColumn();ImGui.TextUnformatted(obj.Name);
            ImGui.TableNextColumn();ImGui.TextUnformatted(KindLabel(obj.Kind));
            ImGui.TableNextColumn();ImGui.TextUnformatted(obj.EntityId.ToString());
            ImGui.TableNextColumn();ImGui.TextUnformatted(obj.NameId.ToString());
            ImGui.TableNextColumn();ImGui.TextUnformatted($"{obj.X:F2}, {obj.Y:F2}, {obj.Z:F2}");
            ImGui.TableNextColumn();ImGui.TextUnformatted(obj.HitboxRadius.ToString("F3"));
            ImGui.TableNextColumn();ImGui.TextUnformatted($"{obj.HP:N0}/{obj.MaxHP:N0} · {obj.Statuses.Count}");
            ImGui.TableNextColumn();ImGui.TextUnformatted(obj.CastActionId.ToString());
        }
    }
    private static string KindLabel(string kind)=>kind switch {
        "Player"=>"玩家","BattleNpc"=>"战斗 NPC","EventNpc"=>"事件 NPC","EventObj"=>"事件物件",
        "Aetheryte"=>"以太之光","Companion"=>"宠物","Housing"=>"房屋物件",_=>"其他",
    };
    private void Mark(bool inside) {
        Capture();var crystals=_capture.Objects.Where(o=>o.NameId==11350).ToArray();
        if(crystals.Length!=1||_capture.LocalPlayer==null)return;
        if(!_range.Mark(_capture.Timestamp,_capture.LocalPlayer.Position(),crystals[0].Position(),inside))_status="位置无效或达到 256 次边缘采样上限。";
    }
    private void OpenSnapshot() {
        if(_capture.Territory==0) { _status="请先进入地图。";return; }
        var data=new DetailedTelemetry { Version=5,MapId=_capture.MapId,MapSizeFactor=_capture.MapSizeFactor,MapOffsetX=_capture.OffsetX,MapOffsetY=_capture.OffsetY,MapTexture=_capture.MapTexture };
        var frame=new MovementFrame { Timestamp=_capture.Timestamp };
        foreach(var obj in _capture.Objects.Where(o=>o.Kind=="Player").Take(32))frame.Players.Add(new() { Player=obj.Name+" #"+obj.EntityId,ClassJobId=obj.ClassJobId,Team=0,X=obj.X,Y=obj.Y,Z=obj.Z,Heading=obj.Heading,HP=obj.HP,MaxHP=obj.MaxHP,StatusTimestamp=_capture.Timestamp,Statuses=obj.Statuses });
        var crystals=_capture.Objects.Where(o=>o.NameId==11350).ToArray();
        if(crystals.Length==1)frame.Crystal=new() { X=crystals[0].X,Y=crystals[0].Y,Z=crystals[0].Z };
        data.Frames.Add(frame);
        var match=new CrystallineConflictMatch { TerritoryId=_capture.Territory,MatchStartTime=_capture.Timestamp,MatchEndTime=_capture.Timestamp,IsCompleted=true };
        _plugin.WindowManager.OpenProbeSnapshot(match,new() { Detailed=data },_range.ReferenceRadius);
    }
    private void Export() {
        Capture();
        var payload=new { Format=1,Capture=_capture,RadiusSamples=_range.Samples.ToArray(),_range.InsideBound,_range.OutsideBound,_range.ReferenceRadius,RadiusScope="manual horizontal boundary samples; not a verified server escort radius" };
        var path=Path.Combine(_plugin.PluginInterface.GetPluginConfigDirectory(),"map-probes",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json");
        _saving=true;_status="正在导出…";
        _=Task.Run(async()=> {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllTextAsync(path,JsonSerializer.Serialize(payload,new JsonSerializerOptions { WriteIndented=true }));_status="已导出："+path; }
            catch(Exception e) { _status="导出失败，请查看日志。";_plugin.Log.Error(e,"Map probe export failed."); }
            finally { _saving=false; }
        });
    }
}
