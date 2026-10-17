using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PvpStats.Analysis;

public sealed class MitigationEstimate {
    public ObservedEffect Effect { get; set; } = new();
    public string Reason { get; set; } = "";
    public double? WithoutGuard { get; set; }
    public double? GuardPrevented { get; set; }
    public double? BeforeKnownModifiers { get; set; }
    public double Modifier { get; set; } = 1;
    public bool Supported => GuardPrevented.HasValue;
}

// Counterfactual reconstruction, never a server-provided damage-prevented statistic.
// Evaluate only on explicit request. Rules are checked against descriptions frozen with the match.
public static class MitigationEstimator {
    private static readonly HashSet<uint> NormalActions = new() { 29058,29059,29060,29074,29075,29076,29085,29086,29087,29098,29099,29100,29500,29501,29502,29523,29524,29525,29538,29539,29540 };
    private static readonly HashSet<uint> Cosmetic = new() { 48,902,1078,1411 };
    // Narrow description rules. Conditional, stacking or unknown modifiers are deliberately unresolved.
    private static readonly Regex Outgoing = new(@"^(?:Damage dealt is increased|Increases damage dealt) by (\d+(?:\.\d+)?)%\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Incoming = new(@"^(?:Damage taken is reduced|Reduces damage taken) by (\d+(?:\.\d+)?)%\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GuardReduction = new(@"(?:Reduces damage taken by|Damage taken is reduced by) (\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static List<MitigationEstimate> Calculate(DetailedTelemetry data) => data.Effects
        .Where(e=>DetailedAnalysis.IsDamage(e.Type) && e.GuardObserved).Select(e=>EstimateAdvanced(data,e)).ToList();
    // Marginal removal of Guard: other supported multiplicative modifiers cancel. This is not a raw/base damage reconstruction.
    private static readonly HashSet<uint> CompatibleStatuses = new() {48,902,1078,1411,4495,2037,2038,3033,4290,2863,2858,4309,4116,4117,4118,4119,4312,3144,3145,3042,3051};
    public static MitigationEstimate EstimateAdvanced(DetailedTelemetry data,ObservedEffect effect) {
        var strict=Estimate(data,effect);if(strict.Supported)return strict;
        MitigationEstimate Pending(string reason)=>new() { Effect=effect,Reason=reason };
        if(!effect.GuardObserved||!effect.SnapshotsCaptured||data.GameVersion.Length==0)return strict;
        if(effect.Type!=3||effect.SelfReturn||effect.PetSource||!DetailedAnalysis.IsEnemy(effect)||effect.Amount==0||effect.ShieldPercent>0)return strict;
        if(!GuardActionCatalog.Direct.Contains(effect.ActionId))return strict;
        var guards=effect.TargetStatuses.Where(s=>s.Id==3054).ToArray();
        if(guards.Length!=1||guards[0].Remaining<=.15f||effect.TargetStatuses.Any(s=>s.Id is 3673 or 3021 or 1302 or 3039))return strict;
        if(!data.StatusDescriptions.TryGetValue(3054,out var guard)||!Regex.IsMatch(guard.Replace('％','%'),@"90\s*%"))return Pending("客户端防御说明未确认 90% 减伤");
        if(!data.ActionDescriptions.TryGetValue(effect.ActionId,out var action)||string.IsNullOrWhiteSpace(action))return Pending("缺少当场客户端技能说明");
        if(Regex.IsMatch(action,@"ignores?|unaffected|remaining HP|current HP|防御.{0,20}(?:無視|無効)|(?:无视|無視).{0,20}防御|无视.{0,20}减伤",RegexOptions.IgnoreCase))return Pending("技能包含防御交互，需要专用规则");
        foreach(var status in effect.ActorStatuses.Concat(effect.TargetStatuses)) {
            if(status.Id==3054)continue;
            if(!CompatibleStatuses.Contains(status.Id))return Pending($"状态 {status.Id} 尚未确认与防御减伤独立，暂不计入合计");
            if(status.Remaining>0&&status.Remaining<=.15f)return Pending($"状态 {status.Id} 临近变化，暂不反推");
        }
        return new() { Effect=effect,WithoutGuard=effect.Amount*10d,GuardPrevented=effect.Amount*9d,
            Reason="条件估算：使用官网普通直伤目录及当场防御状态，仅撤去 90% 防御，保留双方其余已支持状态；相乘倍率在比较中抵消。不还原基础伤害，不含服务器取整、状态先后或护盾误差。" };
    }
    public static MitigationEstimate Estimate(DetailedTelemetry data, ObservedEffect effect) {
        var result = new MitigationEstimate { Effect=effect };
        MitigationEstimate Unresolved(string reason) { result.Reason=reason; return result; }
        if(!effect.GuardObserved || !DetailedAnalysis.IsDamage(effect.Type)) return Unresolved("没有防御期间的伤害事件");
        if(!effect.SnapshotsCaptured || data.GameVersion.Length==0) return Unresolved("缺少事件状态快照或游戏版本");
        if(effect.PetSource) return Unresolved("召唤物与主人状态不能等同，暂不反推");
        if(effect.Type!=3 || effect.SelfReturn || !DetailedAnalysis.IsEnemy(effect)) return Unresolved("格挡、招架、反射或非敌对伤害暂不支持");
        if(effect.Amount==0 || effect.ShieldPercent>0) return Unresolved("零伤害或护盾吸收不能仅由伤害值反推");
        if(!NormalActions.Contains(effect.ActionId)) return Unresolved("技能尚未纳入普通直伤规则，需核对破防、固定伤害及特殊效果");
        if(!data.ActionDescriptions.TryGetValue(effect.ActionId,out var actionDescription) || string.IsNullOrWhiteSpace(actionDescription)) return Unresolved("缺少此版本技能说明");
        if(Regex.IsMatch(actionDescription,@"ignores?|unaffected|damage reduction|\bGuard\b|damage over time|remaining HP|current HP",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
            return Unresolved("技能说明含防御交互、特殊伤害或生命值机制，需核对专用规则");
        var guard = effect.TargetStatuses.Where(s=>s.Id==3054).ToArray();
        if(guard.Length!=1 || guard[0].Remaining<=.15f || effect.TargetStatuses.Any(s=>s.Id is 3673 or 3021)) return Unresolved("防御临近结束、破防或状态不明确");
        if(!data.StatusDescriptions.TryGetValue(3054,out var description)) return Unresolved("缺少此版本防御说明");
        var match=GuardReduction.Match(description);
        if(!match.Success || !double.TryParse(match.Groups[1].Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var reduction) || reduction!=90)
            return Unresolved("当前客户端防御说明未匹配已支持的 90% 规则");
        double modifier=1;
        foreach(var pair in new[]{(Statuses:effect.ActorStatuses, Outgoing:true),(Statuses:effect.TargetStatuses, Outgoing:false)}) {
            foreach(var status in pair.Statuses) {
                if(Cosmetic.Contains(status.Id) || (!pair.Outgoing && status.Id==3054)) continue;
                if(status.Remaining<=.15f) return Unresolved($"状态 {status.Id} 临近切换，暂不反推");
                if(!data.StatusDescriptions.TryGetValue(status.Id,out var text)) return Unresolved($"缺少状态 {status.Id} 的说明");
                var rule=(pair.Outgoing?Outgoing:Incoming).Match(text.Trim());
                if(!rule.Success) return Unresolved($"状态 {status.Id} 含未支持的增减伤或附加机制");
                var percent=double.Parse(rule.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)/100;
                if(percent<=0 || percent>=1 || status.Param>1) return Unresolved($"状态 {status.Id} 的倍率或层数规则未支持");
                modifier*=pair.Outgoing?1+percent:1-percent;
            }
        }
        // Removing Guard preserves all other multiplicative buffs, so those cancel in this marginal comparison.
        // This does not claim to reconstruct authoritative pre-rounding damage or lost HP.
        var retention=1-reduction/100;
        result.WithoutGuard=effect.Amount/retention;
        result.GuardPrevented=result.WithoutGuard-effect.Amount;
        result.Modifier=modifier;
        result.BeforeKnownModifiers=result.WithoutGuard/modifier;
        result.Reason="条件估算：保持双方其余已支持增减伤不变，仅撤去防御；不含取整误差、未观测状态变化或过量伤害修正";
        return result;
    }
}
