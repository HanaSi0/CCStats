# 地图机制来源核实状态

2026-10-17，重新尝试以下 Wiki，均在 HTTPS 代理连接阶段返回 403。所需三个域名已保存到云环境配置草稿，尚未发布生效；没有取得正文，不能标记为已核实。

- https://ffxiv.consolegameswiki.com/wiki/Crystalline_Conflict
- https://ffxiv.gamerescape.com/wiki/Crystalline_Conflict
- https://ff14.huijiwiki.com/wiki/水晶冲突

角力学校、火山之心、九霄云上、机关大殿、赤土红沙、海岸斗场、休兵书库均等待逐图核实。后续需记录机制名称、触发条件、预警、持续时间、范围/方向、应对方式、版本和来源；再与实际客户端技能 ID、对象 ID 及记录时间匹配。

已可验证的技术基础：客户端 NPC 读条、动作效果包和地图计时器观测。来源位置与动作落点分开保存；客户端 Action 表的 CastType、EffectRange、XAxisModifier 仅保留为元数据，未经交叉核实不作为危险区域规则。公开游戏数据表：

- https://github.com/xivapi/ffxiv-datamining/blob/master/csv/en/Action.csv
- https://github.com/xivapi/ffxiv-datamining/blob/master/csv/en/BNpcName.csv

现有战绩缺失的读条或机制位置不能从 Wiki 补造成实际观测记录。
