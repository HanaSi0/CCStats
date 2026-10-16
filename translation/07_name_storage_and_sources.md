# 游戏名词读取、缓存与保存位置

## 固定名称

| 部分 | 来源 | 使用方式 |
| --- | --- | --- |
| 地图名称 | `Helpers/MatchHelper.cs` 的 `GetArenaName` | 比赛列表、筛选、详情和 CSV 导出 |
| 队伍名称 | `MatchHelper.GetTeamName`、`CrystallineConflictTeamName` | 队伍标题、结果、图表 |
| 段位 | `ArenaTier`、`ArenaRankLookup`、`PlayerRank.ToString` | 段位显示和部分字符串识别；识别键必须保留 |
| 职业和职能 | `Helpers/PlayerJobHelper.cs`、`PlayerJobEnums.cs` | 英文全名→职业枚举识别，以及列表、筛选显示 |
| 技能组合 | `Types/Display/Action/ActionSet.cs` | 将多个 Action/Status 汇总为一个具名组合；组合名存于源码 |
| 比赛事件 | `CrystallineConflictMatchEvent`、详情窗口中的固定文案 | 水晶解锁、加时、比赛结束、地图特殊事件等 |
| 资料片 | `Types/GamePeriod.cs` | 赛季/资料片/版本时间筛选 |

## 游戏数据表与内存读取

| 游戏来源 | 读取位置 | 缓存/持久化 | 翻译注意事项 |
| --- | --- | --- | --- |
| `Action.Name` | CC MatchManager、CC MatchDetail | 详情的 `_actionNames`；事件及分析主要保存技能 ID | 多处显式读取 English，回填时应由集中名称提供器处理，不能只改固定组合名 |
| `Status.Name` | CC MatchDetail、WindowManager、MatchManager | 名称用于界面/调试；状态分析保存 ID | 状态在部分分析键中加 `StatusIdOffset`，显示查表时需要去偏移 |
| `BNpcName.Singular` | CC MatchDetail | `_bNPCNames` 名称缓存；事件保存 NameId | 代码会根据英文语法补 A/An，中文显示不应继续套用英文冠词逻辑 |
| `ClassJob.NameEnglish` | CC MatchManager | 转换为 `Job`；另保存 `ClassJobId` | 目前参与职业识别，不能把读取结果简单替换成中文后仍走英文匹配 |
| `World.Name` | MatchManager、PlayerLinkService、GameStateService | `PlayerAlias.HomeWorld`、关联缓存和数据库文档/字典键的一部分 | 翻译必须与身份数据分离，否则可能分裂同一玩家的历史统计 |
| 当前角色 `Name` 和内存玩家名 | GameStateService、MatchManager、DebugWindow | `PlayerAlias.Name` 及比赛、击杀、动作目标等记录 | 玩家自定义名称保留原样，不当作翻译条目 |
| 当前世界的 `DataCenter.Name` | GameStateService | `PvpMatch.DataCenter` 字符串 | 数据中心译名可用于显示，但历史字段兼容需单独处理 |
| `Addon.Text` | LocalizationService 的 8 个固定 ID | `StringCache`，内存字典 | Astra/Umbra 和段位开场文本；可能含格式控制标记 |
| `ColosseumMatchRank` | LocalizationService.TranslateRankString | 字符串翻译辅助方法 | 使用列名 `Unknown0`，且有语言解析假设；方法存在不等于采集流程都在使用 |
| `ContentFinderCondition.Name`、`TerritoryType.PlaceName` | DebugWindow | 仅调试日志中的枚举输出 | 不等于正式列表地图名来源；正式地图名主要由 MatchHelper 固定映射提供 |
| 任意表列文本 | LocalizationService 的泛型翻译/查找方法 | 按表行和目标语言动态取得 | 反射访问列名，是技术标识，不可当 UI 文案翻译 |

## 哪些内容确实写进数据库

- `StorageService` 将水晶冲突文档保存到 `ccmatch`，时间线和技能分析保存到 `cctimeline`，名称关联另存两张 playerlinks 集合。
- 比赛中的地图、队伍、职业、段位主要使用枚举、数字或结构字段，不能为汉化直接重命名/重排枚举。
- `PvpMatch.LocalPlayer`、参赛玩家、时间线目标和部分分析字典包含 `PlayerAlias`。其中玩家名和服务器名属于身份数据。
- `PvpMatch.DataCenter` 是字符串；游戏版本、插件版本也是字符串，但不是应翻译的游戏名词。
- 技能、状态、NPC 通常保存数字 ID，打开详情时再读取英文名称。组合显示名由源码 `ActionSet.Name` 提供。
- 收藏、标签等用户自定义数据保持原样；用户自己的文本不进入固定词条翻译。

完整读取语句及上下文见 `04_runtime_name_reads.csv`，相关源文件见 `source_reference/`。所有源文件快照都为只读参考，不会参与编译。
