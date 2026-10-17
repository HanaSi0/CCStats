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
    private Task<(Dictionary<string,PlayerDetailedStats> Stats,List<MitigationEstimate> Estimates)>? _calculation;
    private int _selected=-1;
    internal DetailedCombatPanel(DetailedTelemetry? data) => _data=data;
    internal void Draw(bool completed, Action openReplay,Action openReplay3D) {
        if(ModernTheme.Button("地图回放###OpenMapReplay")) openReplay();
        ImGui.SameLine();
        if(ModernTheme.Button("3D 回放###OpenReplay3D"))openReplay3D();
        ImGui.SameLine();
        using(var disabled=ImRaii.Disabled(!completed || _data==null || _calculation!=null)) {
            if(ModernTheme.Button("开始细致推算###CalculateDetailed")) {
                // Completed match data is immutable. Heavy aggregation stays off the game/UI thread.
                var data=_data!;
                _calculation=Task.Run(()=>(DetailedAnalysis.Summarize(data),MitigationEstimator.Calculate(data)));
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
        ImGui.TextWrapped("防御期间承伤是伤害包数值；格挡/招架是命中次数。控制状态秒数按连续采样累计，重叠状态分别计时，缺帧不补算。自返治疗不等于全部吸血；已识别吸血仅覆盖血宴灵蛇与强碎灵蛇的自返治疗包，不代表实际有效回血。");
        using(var table=ImRaii.Table("DetailedPlayers",9,ImGuiTableFlags.ScrollX|ImGuiTableFlags.BordersInnerH|ImGuiTableFlags.RowBg,new(0,220*ImGuiHelpers.GlobalScale))) {
            if(table) {
                foreach(var caption in new[]{"玩家全名","防御承伤","防御受击","格挡/招架","控制成功/抵抗","控制状态秒数≈","自返治疗","已识别吸血","防御减伤估算≈"}) ImGui.TableSetupColumn(caption,ImGuiTableColumnFlags.WidthFixed,(caption=="玩家全名"?210:125)*ImGuiHelpers.GlobalScale);
                ImGui.TableSetupScrollFreeze(1,1); ImGui.TableHeadersRow();
                foreach(var stats in result.Stats.Values.OrderBy(s=>s.Player)) {
                    var estimates=result.Estimates.Where(e=>e.Effect.Target==stats.Player&&e.Supported).ToArray();
                    ImGui.TableNextRow();
                    foreach(var text in new[]{stats.Player,stats.GuardedDamage.ToString("N0"),stats.GuardedHits.ToString(),stats.BlockedOrParriedHits.ToString(),$"{stats.ControlHits} / {stats.ControlResists}",stats.ObservedControlSeconds.ToString("F1"),stats.SelfReturnHealing.ToString("N0"),stats.IdentifiedDrainHealing.ToString("N0"),estimates.Length>0?estimates.Sum(e=>e.GuardPrevented!.Value).ToString("N0"):"待核对"}) { ImGui.TableNextColumn(); ImGui.TextUnformatted(text); }
                }
            }
        }
        var supported=result.Estimates.Count(e=>e.Supported);
        ImGui.TextUnformatted($"防御受击规则覆盖：{supported}/{result.Estimates.Count}；待核对 {result.Estimates.Count-supported} 笔。");
        ImGui.TextWrapped("估算口径：保持当时其余已支持的增减伤不变，仅撤去防御。状态快照不等于服务端结算快照，结果含取整和时序误差；待核对事件不进入减伤合计。当前支持基础直伤与简单固定倍率，其他语言说明、复杂增伤、破防、护盾、召唤物等暂不反推。");
        using(var list=ImRaii.Child("MitigationEvidence",new(0,170*ImGuiHelpers.GlobalScale),true)) {
            if(list) for(var i=0;i<result.Estimates.Count;i++) {
                var row=result.Estimates[i];
                var text=$"{row.Effect.Timestamp.ToLocalTime():HH:mm:ss.fff} · {row.Effect.Actor} → {row.Effect.Target} · 技能 {row.Effect.ActionId} · 收到 {row.Effect.Amount:N0} · "+(row.Supported?$"估算规避 ≈{row.GuardPrevented:N0}":"待核对");
                if(ImGui.Selectable(text+"###Evidence"+i,i==_selected)) _selected=i;
            }
        }
        if(_selected>=0&&_selected<result.Estimates.Count) {
            var row=result.Estimates[_selected];
            ImGui.TextWrapped(row.Reason);
            if(row.Supported) ImGui.TextWrapped($"撤去防御后伤害 ≈{row.WithoutGuard:N0}；已支持增减伤合成倍率 {row.Modifier:F3}；移除这些倍率后的参考伤害 ≈{row.BeforeKnownModifiers:N0}。");
            ImGui.TextWrapped($"攻击方状态：{string.Join("，",row.Effect.ActorStatuses.Select(s=>$"{s.Id}（参数 {s.Param}，余 {s.Remaining:F2} 秒）"))}");
            ImGui.TextWrapped($"受击方状态：{string.Join("，",row.Effect.TargetStatuses.Select(s=>$"{s.Id}（参数 {s.Param}，余 {s.Remaining:F2} 秒）"))}");
            foreach(var id in row.Effect.ActorStatuses.Concat(row.Effect.TargetStatuses).Select(s=>s.Id).Distinct()) {
                if(_data.StatusDescriptions.TryGetValue(id,out var description)) ImGui.TextWrapped($"状态 {id} 客户端原始说明：{description}");
            }
            if(_data.ActionDescriptions.TryGetValue(row.Effect.ActionId,out var actionText)) ImGui.TextWrapped("技能客户端原始说明："+actionText);
        }
    }
}
