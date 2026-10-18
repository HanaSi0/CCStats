namespace PvpStats.Analysis;
// Display names follow the user's supplied Chinese guide. International behavior is cross-referenced separately.
public static class ArenaMechanicGuide {
    public static string Detail(ArenaMechanicKind k)=>k switch {
        ArenaMechanicKind.SprintZone=>"冲刺带：进入后获得极速冲刺。示意箭头不代表实际触发边界。",
        ArenaMechanicKind.Bomb=>"火山喷发：爆弹怪落地后自爆，伤害为十字范围。落点和引爆以记录中的读条、动作和对象为准。",
        ArenaMechanicKind.BombCore=>"爆弹怪之魂：拾取后增伤，最多十层，并增加极限槽。只在采集到对象时显示拾取物，获得状态时只标记玩家。",
        ArenaMechanicKind.JumpGlyph=>"跳跃台：九霄云上会随双方进度变化；海岸斗场也有跳跃台。休兵书库在魔力检测点供给完成后生成，并保留到本局结束。位置及启用状态需要实际机关记录。",
        ArenaMechanicKind.Turbulence=>"湍流：击飞、落地伤害；防御可避免被卷起。实际高度沿用玩家走位采样，不从计时器推测。",
        ArenaMechanicKind.Feather=>"黑陆行鸟的羽毛：免除湍流落地伤害，短时提高攻速和移速，并增加极限槽。地面提示与空中拾取物不能混作同一个判定。",
        ArenaMechanicKind.Twister=>"龙卷：造成伤害并击退，解除防御。与湍流的全场提示采用不同的局部旋涡占位符。",
        ArenaMechanicKind.GaleSphere=>"风球：单次拾取提高移速，湍流期间会重新出现。必须观测到再次出现，不按固定间隔补画。",
        ArenaMechanicKind.TrickFloor=>"机关榻榻米：停留后触发榻榻米翻转，造成伤害与击退，并解除防御。翻板是机制示意，不推断玩家是否必定中招。",
        ArenaMechanicKind.TrickDoor=>"机关门：单向传送，部分技能发动期间无法通行。没有实际出口记录时不连接出口，也不画反向通道。",
        ArenaMechanicKind.Yojimbo=>"机关狂欢 · 机关保镖：沿路线行进，在中点、战术点和终点附近发动周围物理攻击；移动跟随实际对象采样，伤害范围由动作记录驱动。",
        ArenaMechanicKind.Onmyoji=>"机关狂欢 · 机关阴阳师：悬浮眼睛与范围线代表视线攻击；命中会缩小，降低输出和移速、增加承伤。不能将范围内所有玩家认定为中招。",
        ArenaMechanicKind.Gold=>"机关狂欢：景山向场内撒金币，拾取金币堆增加极限槽。金币只按真实对象或动作落点显示，不随机生成。",
        ArenaMechanicKind.ShiftingSands=>"流沙：快速移动到指定地点。流动纹样与冲刺带的直箭头区别显示，不凭位移猜测精确入口或出口。",
        ArenaMechanicKind.Antlion=>"红沙蚁狮：吸入巢穴中央并拘束，随后跃出造成大伤害。国服写到达战术点，英文写通过检查点；回放按实际出怪及动作记录。",
        ArenaMechanicKind.Sabotender=>"仙人刺：十万针刺由范围内实际命中的玩家分摊。只显示记录范围，不从圈内人数推定结算。",
        ArenaMechanicKind.Acutender=>"仙人盾：十万针灸给予分摊护盾，与仙人刺用盾牌和针形区分。",
        ArenaMechanicKind.Oasis=>"绿洲：持续恢复，离开后状态可保留一段时间。仅凭玩家身上的恢复状态，不能反推绿洲的场地边界。",
        ArenaMechanicKind.HeatWave=>"热浪：全场持续伤害；根据实际热浪状态显示场地热纹，未记录时不按地图计时器触发。",
        ArenaMechanicKind.Geyser=>"间歇泉：喷发期间巢穴不出怪，中心造成伤害和击退。喷泉与沙漠之光恢复落点分别显示。",
        ArenaMechanicKind.Sandpearl=>"沙漠之光：落地前有圆形预兆，触发后恢复体力并增加极限槽；使用实际落点，不能拿施法来源坐标代替。",
        ArenaMechanicKind.Aetherometer=>"魔力检测点：推进方占圈供给魔力；离圈或敌方任一人进圈会重置。供给满后生成魔力桥和跳跃台；未采集到供给量时不画虚构进度。",
        ArenaMechanicKind.AetherBridge=>"魔力桥：生成后本局持续存在；回放需要记录生成状态与桥的两端，不能根据计时器虚构架桥。示意光板不是真实模型或判定尺寸。",
        ArenaMechanicKind.HealingGlyph=>"治疗区：己方出生点附近，持续回血并减伤 50%。只显示实际记录到的机关或治疗状态，不推断归属。",
        _=>"未知机制不自动绘制。"
    };
    // Reference rule only. It never activates a pad without a corresponding object/state observation.
    public static (int Astra,int Umbra)? CloudJumpPads(float astra,float umbra) {
        if(!float.IsFinite(astra)||!float.IsFinite(umbra)||astra<0||umbra<0||astra>1||umbra>1)return null;
        if(astra<=.5f&&umbra<=.5f)return (1,1);
        if(astra==umbra)return null; // The supplied rule does not define equal progress above halfway.
        return astra>umbra?(2,0):(0,2);
    }
    public static float ResetAetherCharge(float? recorded,bool advancingPresent,bool enemyPresent)=>
        !advancingPresent||enemyPresent?0:recorded.HasValue&&float.IsFinite(recorded.Value)?System.Math.Clamp(recorded.Value,0,1):float.NaN;
}
