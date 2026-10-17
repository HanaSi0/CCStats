using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using PvpStats.Analysis;
using PvpStats.Interface;
using PvpStats.Services;
namespace PvpStats.Windows.Replay;
internal sealed class MapProbeBatchWindow : Window {
    internal const string Title="地图批量采集进度###CCMapProbeBatch";
    private readonly Plugin _plugin;
    private readonly MapProbeWindow _source;
    private readonly MapProbeBatchStore? _store;
    private readonly CancellationTokenSource _cancel=new();
    private volatile bool _busy;
    private string _status="等待采集当前地图",_zip="";
    private float _progress;
    private string _label="";
    internal MapProbeBatchWindow(Plugin plugin,MapProbeWindow source):base(Title) {
        _plugin=plugin;_source=source;IsOpen=true;Size=new Vector2(820,640);SizeCondition=ImGuiCond.FirstUseEver;
        try { _store=new(Path.Combine(plugin.PluginInterface.GetPluginConfigDirectory(),"map-probes","batch-current")); }
        catch(Exception e) { _status="无法读取批次清单，请保留已有文件并查看日志。";plugin.Log.Error(e,"Map batch initialization failed."); }
    }
    public override void PreDraw()=>WindowChrome.Constrain(this);
    public void Cancel()=>_cancel.Cancel();
    public override void Draw() {
        ImGui.SetWindowFontScale(1);var scale=ImGuiHelpers.GlobalScale;
        ImGui.TextUnformatted("本地批量采集 · 最后导出 ZIP 发到聊天");
        ImGui.TextUnformatted("当前目标："+_source.CurrentTargetLabel);
        ImGui.TextDisabled("没有配置上传服务；这里显示采集和打包进度，不会上传网络。");
        using(var disabled=ImRaii.Disabled(_busy||_store==null)) {
            if(ModernTheme.Button("采集当前地图###BatchCapture"))CaptureMap();
            ImGui.SameLine();if(ModernTheme.Button("完成全部并导出 ZIP###BatchFinish"))ExportZip();
        }
        ImGui.SameLine();if(ModernTheme.Button("打开地图试验页###BatchProbe"))_plugin.WindowManager.OpenMapProbe();
        ImGui.SetNextItemWidth(-1);ImGui.InputTextWithHint("备注###BatchLabel","可选：地图名、取样位置或本次发现的问题",ref _label,160);
        ImGui.ProgressBar(Math.Clamp(_progress,0,1),new Vector2(-1,22*scale),_busy?$"{_progress:P0}":"等待下一步");
        ImGui.TextWrapped(_status);
        var entries=_store?.Snapshot()??[];
        ImGui.TextUnformatted($"已完成 {entries.Count(e=>e.State=="Complete")} 次采集 · {entries.Where(e=>e.State=="Complete").Select(e=>e.Territory).Distinct().Count()} 个目标区域");
        ImGui.TextWrapped("每张地图：用 Hyperborea 加载场景 → 在试验页核对目标地图 → 可选设置圈中心并标记边缘 → 点击采集 → 等待绿色完成提示，再切换下一张。");
        if(_zip.Length>0) {
            ImGui.TextWrapped("压缩包："+_zip);
            if(ModernTheme.SmallButton("复制 ZIP 路径###CopyBatchZip"))ImGui.SetClipboardText(_zip);
            ImGui.SameLine();if(ModernTheme.SmallButton("打开所在文件夹###OpenBatchFolder"))OpenFolder();
        }
        if(_store==null)return;
        using var table=ImRaii.Table("BatchMaps",4,ImGuiTableFlags.RowBg|ImGuiTableFlags.ScrollY|ImGuiTableFlags.Resizable,new Vector2(0,Math.Max(1,ImGui.GetContentRegionAvail().Y)));
        if(!table)return;
        ImGui.TableSetupColumn("地图",ImGuiTableColumnFlags.WidthStretch,1);
        ImGui.TableSetupColumn("区域 / 地图",ImGuiTableColumnFlags.WidthFixed,110*scale);
        ImGui.TableSetupColumn("进度",ImGuiTableColumnFlags.WidthFixed,80*scale);
        ImGui.TableSetupColumn("状态",ImGuiTableColumnFlags.WidthStretch,2);
        ImGui.TableSetupScrollFreeze(0,1);ImGui.TableHeadersRow();
        foreach(var entry in entries.Reverse()) {
            ImGui.TableNextRow();ImGui.TableNextColumn();ImGui.TextUnformatted(entry.Label);
            ImGui.TableNextColumn();ImGui.TextUnformatted($"{entry.Territory} / {entry.Map}");
            ImGui.TableNextColumn();ImGui.TextUnformatted($"{entry.Progress:P0}");
            ImGui.TableNextColumn();var statusColor=entry.State=="Complete"?ModernTheme.Accent:ModernTheme.Gold;
            ImGui.TextColored(statusColor,entry.Status);
        }
    }
    private void CaptureMap() {
        if(_busy||_store==null)return;
        MapProbeWindow.BatchInput input;string id;
        try {
            input=_source.CaptureForBatch();id=_store.Begin(_label.Length>0?input.Label+" · "+_label:input.Label,input.Territory,input.Map);
        } catch(Exception e) { _status="无法开始采集："+e.Message;return; }
        _busy=true;_progress=.1f;_status="对象快照已抓取，正在读取当前目标地图模型；请暂时不要切换地图。";
        _=Task.Run(()=> {
            try {
                _store.Update(id,.2f,"正在读取目标地图模型");_progress=.2f;
                var loader=new ReplaySceneLoader(_plugin,input.Min,input.Max,_cancel.Token,message=>_status=message);
                var scene=loader.Load(input.Background);_cancel.Token.ThrowIfCancellationRequested();
                _progress=.75f;_status="正在保存地图元数据、模型诊断及圈边缘标记";_store.Update(id,.75f,_status);
                var summary=new { scene.Status,scene.Models,scene.MissingModels,scene.TerrainPlates,scene.Limited,TriangleCount=scene.Triangles.Count,scene.AssetPaths,scene.ModelDiagnostics,
                    Scope="static scene diagnostics; no mesh assets exported; circle samples are manually labelled" };
                _progress=.9f;_store.Complete(id,new Dictionary<string,string> { ["capture.json"]=input.CaptureJson,["scene.json"]=JsonSerializer.Serialize(summary,new JsonSerializerOptions { WriteIndented=true }) });
                _progress=1;_status=scene.Triangles.Count>0?"本地图已保存并校验，可以切换下一张地图。":"本地图已保存并校验；未读到几何的诊断也已保存，可以切换下一张地图。";
            } catch(Exception e) {
                _status="本地图采集失败，可重试："+e.Message;
                try { _store.Fail(id,_status); } catch(Exception save) { _plugin.Log.Error(save,"Map batch failure state could not be saved."); }
                _plugin.Log.Error(e,"Map batch capture failed.");
            } finally { _busy=false; }
        });
    }
    private void ExportZip() {
        if(_busy||_store==null)return;
        var path=Path.Combine(_store.Root,"CCStats-map-probes-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".zip");
        _busy=true;_progress=0;_status="正在校验并打包所有已完成地图";
        _=Task.Run(()=> {
            try { _zip=_store.Export(path,(progress,status)=> { _progress=progress;_status=status; });_status="全部已完成地图已打包；请把 ZIP 发到聊天，然后继续分析。"; }
            catch(Exception e) { _status="打包失败："+e.Message;_plugin.Log.Error(e,"Map batch ZIP export failed."); }
            finally { _busy=false; }
        });
    }
    private void OpenFolder() {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute=true,ArgumentList={Path.GetDirectoryName(_zip)!} }); }
        catch(Exception e) { _status="无法打开文件夹，可复制 ZIP 路径。";_plugin.Log.Error(e,"Map probe folder open failed."); }
    }
}
