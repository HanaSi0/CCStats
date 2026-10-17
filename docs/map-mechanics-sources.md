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

已可验证的技术基础：客户端 NPC 读条、动作效果包和地图计时器观测。来源位置与动作落点分开保存；客户端 Action 表的 CastType、EffectRange、XAxisModifier 仅保留为元数据，未经交叉核实不作为危险区域规则。公开游戏数据表：

- https://github.com/xivapi/ffxiv-datamining/blob/master/csv/en/Action.csv
- https://github.com/xivapi/ffxiv-datamining/blob/master/csv/en/BNpcName.csv

此前访问 consolegameswiki、Gamer Escape 和灰机 Wiki 时在代理阶段返回 403；本次使用官网正文完成行为说明核实，不宣称已取得 Wiki 正文。现有战绩缺失的读条或机制位置不能从说明资料补造成实际观测记录。
