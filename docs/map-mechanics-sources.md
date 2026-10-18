# 地图机制来源核实状态

2026-10-17 已成功抓取 FF14 官网水晶冲突指南，逐张提取全部七张地图的机制说明。

来源：https://na.finalfantasyxiv.com/lodestone/playguide/contentsguide/crystallineconflict/

完整英文原文、抓取时间及页面 SHA256 保存在 [official-map-mechanics.json](research/official-map-mechanics.json)。各职业 PvP 技能完整快照见 [official-pvp-actions.json](research/official-pvp-actions.json)，全部 LB 原文见 [official-limit-breaks.md](research/official-limit-breaks.md)。这些是官网当前页面快照，不能假定与历史战绩版本一致。

| 地图 | 官网列明的机制 |
| --- | --- |
| 角力学校 | 加速区域 |
| 火山之心 | 加速区域；喷发散布自爆炸弹；爆炸后核心提供增伤叠层和极限槽 |
| 九霄云上 | 跳跃机关；乱流击飞与落地伤害；黑陆行鸟羽毛；乱流留下造成伤害和击退的龙卷风及提高移动速度的风球 |
| 机关大殿 | 机关地板翻转；单向机关门；机关人偶巡游；武士周围物理伤害；阴阳师视线缩小；金币提供极限槽 |
| 赤土红沙 | 流沙移动；通过检测点触发沙坑生物；蚁狮吸入禁锢与伤害；仙人掌分摊伤害；稀有生物分摊护盾；绿洲恢复；热浪全场持续伤害；喷泉、恢复与极限槽，以及中心击退伤害 |
| 海岸斗场 | 跳跃机关（官网此节未列出其他机制） |
| 休兵书库 | 魔力计充能；离开水晶圈或敌方进入导致充能归零；充满生成持续至比赛结束的桥梁及跳跃机关；己方恢复机关 |

以上核实的是机制行为。官网未完整提供危险区域坐标、判定尺寸、精确发生周期和客户端技能 ID，不能直接生成准确碰撞区域或凭固定时间伪造实际发生事件。

已可验证的技术基础：客户端 NPC 读条、动作效果包和地图计时器观测。来源位置与动作落点分开保存；1.0.5 起，已按 ID 分类的机制使用客户端 Action 表的 EffectRange 和 XAxisModifier 绘制参考范围。未知动作和纯计时器不生成范围。公开游戏数据表：

- https://github.com/xivapi/ffxiv-datamining/blob/master/csv/en/Action.csv
- https://github.com/xivapi/ffxiv-datamining/blob/master/csv/en/BNpcName.csv

此前访问 consolegameswiki、Gamer Escape 和灰机 Wiki 时在代理阶段返回 403；本次使用官网正文完成行为说明核实，不宣称已取得 Wiki 正文。现有战绩缺失的读条或机制位置不能从说明资料补造成实际观测记录。

## 1.0.5 范围绘制

已对照官网行为和客户端技能名称接入 13 个动作，原始名称、ID 和尺寸见 [客户端机制表](research/map-mechanic-actions.json)，规则见 `MapMechanicVisuals.cs`。

- 赤土红沙：吞噬 35751（伤害圈 6）；100,000 Needles 35752（分摊圈 22）；Therapeutic Needles 35753（分摊护盾圈 22）；Landslip 35754（吸引圈 22）；Sandpearl Shroud 35755（实际落点恢复圈 2）；Geyser 35756（击退圈 2）。中文是功能译名，英文原名来自 Action 表，不沿用 NPC 的日文拼接文字。
- 火山之心：Elemental Impact 28725（圈 5）；Self-destruct 28726（十字，EffectRange 70、宽 10）。十字臂长按客户端范围字段解释绘制，未逐条实测碰撞边界。
- 九霄云上：Biting Wind 28729（圈 5）；Typhoon 28731（击退圈 3）。全场 Turbulence 无可靠局部半径，不画假圆。
- 机关大殿：Tatami Twist 32364（10×10 机关地板）；Tsujigiri 32365（圈 15）；Onmyojutsu 32366（视线作用距离 30，以眼形和轮廓表示，不能据此断定圈内所有人命中）。方形与十字需要记录中的朝向。

地图画面不显示机制名称、倒计时或通用三角标记；名称留在可折叠事件记录里。实际读条显示低透明度范围，动作结算显示短暂强调后淡出，吸引和击退的波纹方向相反。护盾青色、恢复绿色、伤害橙红、分摊金色、击退紫色、视线粉色。

绘制由实际 NPC 读条/动作记录驱动。未知动作、缺失落点、未记录的地面机关状态和单纯的地图计时器都不会推算成已发生机制；因此另外三张地图的固定加速区、传送点及魔力桥不会凭计时器自动点亮。旧战绩可按已有动作 ID 分类，缺失的落点无法补造。新版补充保存地面读条的客户端 CastInfo.TargetLocation。

这些是复盘示意范围，并非完整复制游戏判定（遮挡、视线、目标判定圈等仍影响命中），也不是新的实时战斗提示。

## 后续占位符与国服名称更新

24 种占位符、对象采集、当前接入边界和本轮官方/社区访问记录详见 [机制实现状态](map-mechanics-implementation.md)。地图内不绘制机制名称或倒计时；机关阴阳师改用悬浮眼睛和作用范围。中文显示更新为榻榻米翻转、十万针灸、沙漠之光，其余原有条目为历史记录。
