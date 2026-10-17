using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using PvpStats.Analysis;
using PvpStats.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PvpStats.Windows.Analysis;
internal sealed class DetailedCombatPanel {
    private readonly DetailedTelemetry? _data;
    private Task<(Dictionary<string,AdvancedPlayerStats> Stats,List<MitigationEstimate> Estimates)>? _calculation;
    private int _selected=-1;
    private string _player;
    internal DetailedCombatPanel(DetailedTelemetry? data,string? localPlayer=null) { _data=data;_player=localPlayer??""; }
    internal void Draw(bool completed, Action openReplay,Action openReplay3D) {
        if(ModernTheme.Button("地图回放###OpenMapReplay")) openReplay();
        ImGui.SameLine();using(var disabled3D=ImRaii.Disabled(!completed))if(ModernTheme.Button("3D 回放（试验）###OpenMapReplay3D"))openReplay3D();
        ImGui.SameLine();
        using(var disabled=ImRaii.Disabled(!completed || _data==null || _calculation!=null)) {
            if(ModernTheme.Button("开始细致推算###CalculateDetailed")) {
                // Completed match data is immutable. Heavy aggregation stays off the game/UI thread.
                var data=_data!;
                _calculation=Task.Run(()=> { var estimates=MitigationEstimator.Calculate(data);return (AdvancedCombatAnalysis.Calculate(data,estimates),estimates); });
            }
        }
        ImGui.TextWrapped("仅点击按钮后在本机分析本局；不上传战斗数据。平时保存事件与必要状态快照，不自动推算。");
        if(_data==null) { ImGui.TextWrapped("此战绩未记录细项数据，旧比赛无法补抓。请在设置 → 性能中启用细项与走位记录，然后参加新比赛。"); return; }
        ImGui.TextUnformatted($"已记录 {_data.Effects.Count:N0} 个效果、{_data.Frames.Count:N0} 个位置采样；游戏版本：{(_data.GameVersion.Length>0?_data.GameVersion:"未知")}");
        if(_data.EffectsTruncated||_data.FramesTruncated) ImGui.TextColored(ModernTheme.Gold,"采集达到容量上限，下列数据仅代表已保存的部分。");
        if(!completed) { ImGui.TextWrapped("比赛结束后才能计算。"); return; }
        if(_calculation==null) return;
        if(!_calculation.IsCompleted) { ImGui.TextUnformatted("正在本机计算……"); return; }
        if(_calculation.IsFaulted) { ImGui.TextWrapped("计算失败，重新打开详情可重试。原始记录仍保留。"); return; }
        var result=_calculation.GetAwaiter().GetResult();
        ImGui.Separator();
        var names=result.Stats.Keys.OrderBy(x=>x).ToArray();
        if(names.Length>0&&!result.Stats.ContainsKey(_player))_player=names[0];
        if(ImGui.BeginCombo("分析玩家###AdvancedPlayer",_player)) {
            foreach(var name in names)if(ImGui.Selectable(name,name==_player)) { _player=name;_selected=-1; }
            ImGui.EndCombo();
        }
        if(result.Stats.TryGetValue(_player,out var current)) {
            using(var cards=ImRaii.Table("AdvancedMetrics",3,ImGuiTableFlags.SizingStretchSame|ImGuiTableFlags.BordersInnerH)) {
                if(cards)foreach(var item in new (string Label,string Value)[] {
                    ("防御减伤估算≈",current.EstimatedHits>0?current.EstimatedGuardPrevented.ToString("N0"):"待核对"),
                    ("防御期间控制抵抗",current.ControlResistsUnderGuard.ToString()),("观测防御时长",current.GuardSeconds.ToString("F1")+" 秒"),
                    ("防御承伤 / 受击",$"{current.GuardedDamage:N0} / {current.GuardedHits}"),("估算覆盖 / 待核对",$"{current.EstimatedHits} / {current.PendingHits}"),("护盾期间受击",current.ShieldedHits.ToString()),
                    ("造成伤害（效果包）",current.DamageDealt.ToString("N0")),("受到伤害（效果包）",current.DamageReceived.ToString("N0")),("已识别吸血（治疗包）",current.DrainHealing.ToString("N0")),
                    ("治疗输出 / 获得治疗",$"{current.HealingDealt:N0} / {current.HealingReceived:N0}"),("控制施加 / 采样确认",$"{current.ControlApplications} / {current.ConfirmedControls}"),("控制贡献秒数≈",current.ControlSeconds.ToString("F1")),
                }) { ImGui.TableNextColumn();ImGui.TextDisabled(item.Label);ImGui.TextUnformatted(item.Value); }
            }
        }
        ImGui.TextWrapped("减伤估算仅撤去防御，其余已支持状态保持不变。防御期控制抵抗是实际抵抗包，可能同时存在其他免控；不直接视为全部由防御挡下。护盾受击不等于吸收量，现有记录不足时不反推出护盾吸收总量。治疗与吸血为包数值，含过量治疗。");
        var supported=result.Estimates.Count(e=>e.Supported);
        ImGui.TextUnformatted($"防御受击规则覆盖：{supported}/{result.Estimates.Count}；待核对 {result.Estimates.Count-supported} 笔。");
        ImGui.TextWrapped("估算口径：保持当时其余已支持的增减伤不变，仅撤去防御。状态快照不等于服务端结算快照，结果含取整和时序误差；待核对事件不进入减伤合计。扩展使用官网普通直伤目录与当场客户端说明，日文等客户端可核对 90% 防御；未知状态、破防、护盾、召唤物等仍不计入减伤合计。");
        using(var list=ImRaii.Child("MitigationEvidence",new(0,170*ImGuiHelpers.GlobalScale),true)) {
            if(list) for(var i=0;i<result.Estimates.Count;i++) {
                var row=result.Estimates[i];
                if(row.Effect.Target!=_player)continue;
                var text=$"{row.Effect.Timestamp.ToLocalTime():HH:mm:ss.fff} · {row.Effect.Actor} → {row.Effect.Target} · 技能 {row.Effect.ActionId} · 收到 {row.Effect.Amount:N0} · "+(row.Supported?$"估算规避 ≈{row.GuardPrevented:N0}":"待核对");
                if(ImGui.Selectable(text+"###Evidence"+i,i==_selected)) _selected=i;
            }
        }
        if(_selected>=0&&_selected<result.Estimates.Count) {
            var row=result.Estimates[_selected];
            ImGui.TextWrapped(row.Reason);
            if(row.Supported) { ImGui.TextWrapped($"撤去防御后伤害 ≈{row.WithoutGuard:N0}；防御规避 ≈{row.GuardPrevented:N0}。");if(row.BeforeKnownModifiers.HasValue)ImGui.TextWrapped($"已支持倍率 {row.Modifier:F3}；移除这些倍率后的参考伤害 ≈{row.BeforeKnownModifiers:N0}。"); }
            ImGui.TextWrapped($"攻击方状态：{string.Join("，",row.Effect.ActorStatuses.Select(s=>$"{s.Id}（参数 {s.Param}，余 {s.Remaining:F2} 秒）"))}");
            ImGui.TextWrapped($"受击方状态：{string.Join("，",row.Effect.TargetStatuses.Select(s=>$"{s.Id}（参数 {s.Param}，余 {s.Remaining:F2} 秒）"))}");
            foreach(var id in row.Effect.ActorStatuses.Concat(row.Effect.TargetStatuses).Select(s=>s.Id).Distinct()) {
                if(_data.StatusDescriptions.TryGetValue(id,out var description)) ImGui.TextWrapped($"状态 {id} 客户端原始说明：{description}");
            }
            if(_data.ActionDescriptions.TryGetValue(row.Effect.ActionId,out var actionText)) ImGui.TextWrapped("技能客户端原始说明："+actionText);
        }
    }
}
