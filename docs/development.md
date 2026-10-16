# 开发环境与验证

## 云环境

使用 `/workspace/CCStats` 现有检出。每个云任务已隔离，不额外创建 Git worktree。

```bash
export PATH=/workspace/.dotnet:$PATH
export DOTNET_CLI_HOME=/workspace/.dotnet-cli
export NUGET_PACKAGES=/workspace/.nuget/packages
export DALAMUD_HOME=/workspace/.dalamud
export DOTNET_CLI_TELEMETRY_OPTOUT=1
cd /workspace/CCStats
dotnet restore PvpStats.sln --locked-mode -p:EnableWindowsTargeting=true
dotnet build PvpStats.sln -c Release --no-restore -p:EnableWindowsTargeting=true
dotnet build PvpStats.sln -c Debug --no-restore -p:EnableWindowsTargeting=true
```

本机 .NET SDK 为 10.0.402。Dalamud 来自官方 `goatcorp/dalamud-distrib`：

- 提交：`904e0b30d912e68c6de1812f2f143ee496b1b55d`
- 文件：`latest.zip`
- SHA-256：`82d3efad4615d4b40fdabd458132473d6b69e341cd700293a6bb3ff2b5b77547`

当前环境的 .NET 构建需要沙箱外执行权限：沙箱内 NuGet 命名互斥锁初始化失败。在工具支持的权限流程中执行同一条构建命令；不要修改项目代码来规避该系统限制。

## 已执行的检查

- Release：构建成功，102 个警告，0 错误。
- Debug：构建成功，100 个警告，0 错误。
- Roslyn：同时以 Release 和 DEBUG 预处理符号解析全部保留的 C# 源码，语法错误为 0。
- 战场模式引用扫描：保留源码内没有 FL/RW 采集、缓存、模型或窗口引用。
- `git diff --check`：通过。

现有警告包含可空引用、未使用字段和运行时注入 Hook 字段的静态赋值警告。本次未扩展到修改这些既有逻辑。
无自动化游戏运行测试；云环境未启动 FFXIV/Dalamud 游戏宿主，不能据此宣称比赛采集或中文客户端兼容已经验证。

## 下一步游戏内验证

加载 Debug/Release 版本后检查 `/ccstats`、`/lastmatch`、设置和插件重载；在水晶冲突中完成一次比赛，核对结算、时间线、技能分析和重启后的历史记录。其他模式的入口和命令应不存在。翻译回填后检查中文名称、身份识别和排序；UI 改版后再检查字体与布局。

## 本地化与 UI 检查

已确认 374 项聊天译文。运行 `python3 tools/generate_localization.py` 更新中文显示字典，再执行构建。

```bash
dotnet restore tests/LocalizationChecks --locked-mode
dotnet run --project tests/LocalizationChecks -c Release --no-restore
```

142 项检查覆盖技能/状态同名隔离、未知 ID 回退、组合名与单技能名分离、枚举和数据库内容保持不变、原英文识别逻辑、已有 `###` 控件 ID 的散列稳定性、悬浮图标交互与搜索语法。
普通旧标签会获得基于原始文字的固定 ID；没有显式 `###` ID 的旧窗口可能首次重新使用默认位置和大小。配置中保存的标签页键、筛选枚举和比赛数据不改名。

主窗口头部的场数来自当前筛选结果，胜率排除观战和无有效胜方的记录。概览统计只在筛选结果列表刷新后重算，避免每帧遍历历史战绩。
当前默认窗口为 1240×820，使用深蓝圆角主题、侧栏导航、四张统计卡片与快捷筛选。详情和设置共用主题，设置分为界面、玩家关联和性能三类。
战绩表列宽可拖动、列可隐藏，支持横向滚动、单列排序及分页。点击表头排序应用于全部筛选结果，再回到第一页。分页根据实际结果数限制范围。
搜索把玩家、职业缩写及中英文职业名、地图、比赛类型、标签、日期与记录 ID 合为查询内容；空格连接多个必须匹配的词，双引号匹配短语，减号排除词。搜索不会改变数据库内容。
预览采用示例数据；实际 ImGui 字体、缩放、滚动、布局与鼠标交互需在游戏内复核。

`tools/cloud-install.sh` 已包含显示字典生成、应用 Release/Debug 构建及上述回归检查。
`tools/export_translation.py` 是初始导出工具，检测到已有译文会拒绝覆盖。`translation/source_reference/` 是旧的交接快照，日常开发应阅读 PvpStats 内的实际源文件。

## 悬浮图标

登录后显示，左键打开主窗口，按住右键拖动；拖动离开原图标区域后仍继续跟随，松开右键后保存位置。
位置以游戏主视口的相对逻辑坐标保存；默认大小 48，支持 UI 缩放，视口变化时限制到可见区域。
设置中可关闭悬浮图标、调整大小（32–96）、重置位置。登出后取消拖动并隐藏。
14 项新增交互检查与原有 28 项本地化检查一起运行；鼠标真实捕获、UI 缩放和贴图呈现仍需游戏内验证。
`images/icon.png` 原样使用用户上传的 PNG，构建输出和插件 ZIP 共用同一张图。

## 主题配置

`ThemeConfiguration` 独立保存窗口配色，旧配置缺少此字段时使用默认主题，初始化时校正非法通道和背景透明度。`ModernTheme` 仅在插件绘制作用域内应用主题，结束时恢复 ImGui 样式。卡片、筛选按钮、表格、滚动条、标签和边框共用调色板。Logo 保持原图，胜负/职业等业务颜色继续使用 `ColorConfiguration`。

新增检查覆盖保存快照独立性、非法颜色恢复、旧配置默认值、四个预设的序列化往返。网页预览单独以浏览器验证主题切换、编辑和导航；不能代替游戏内验证。

## 榜单、官方接口与按天缓存

侧栏为“榜单”，大区为日服、北美服、欧服、大洋洲服。当前官网一个物理大区只有一份榜单；不按逻辑数据中心或玩家服务器重复抓取。`RankingRegion` 从官方菜单归类四区；`dcgroup` 代表值分别为 Elemental、Primal、Light、Materia。页面保留玩家所属服务器显示，但只有大区选择器。

`RegionalLeaderboardService` 为四区创建独立服务，缓存 `leaderboards/<地区键>/latest.json`；旧单文件不删除。插件加载后 `Task.Run` 发起后台检查，`Lazy<Task>` 去重。没有定时器，切换页面和大区只读缓存。`Configuration.LeaderboardRegion` 保存选区。

真实 DOM 样本已于 2026-10-16 核对：

- 日期在 `.summarytime span`，日期和时段不能直接拼接解析；优先读日期 span。JP 为 `2026/10/16`，NA 为 `10/15/2026`，EU 为 `16/10/2026`，不转换到电脑时区。
- 赛季是 `.cc-ranking__header-data h2 img[alt]`。元数据标题必须排除玩家 h3，否则会把整页玩家名塞进界面头部。
- 玩家行是 `.ranking_set[data-href]`，并非 a 标签。姓名在 `.cc-ranking__result__name h3`，服务器与逻辑数据中心在 `.world`，头像在 `.face img`。
- 段位由图片 alt/data-tooltip 给出，阶位是 `.tier p`。低段位胜星为 `.points .data` 中 `★/☆`，不能把阶位数字当作段位或把胜星当作积分变化。
- 积分与胜场使用各自 p 值，`.up/.down` 中的变化独立保存。`.prev_order` 是上次名次，排名变化用前次减当前计算，并保存原值。
- 首页 50 条，`.js--ranking-loading[data-next-page=2]` 表示继续加载。相同路径 `?dcgroup=...&page=2` 返回第 51–100 名的 HTML 片段；其后依次读取，直到 HTTP 200 的空片段。片段不带日期或下一页链接，不能只靠 a 标签遍历分页。

`LodestoneParser` 同时支持真实 data-href 行与旧形式的测试行，保留完整页面、图片链接、未知字段。仅允许官方 HTTPS 榜单路径，不跨历史赛季或段位筛选；`rank_type=all` 与缺省 ALL 规范化。空首页/维护页不会成为空榜单，只有明确后续页的空响应可终止。重复分页响应、跨区、超时、失败和取消均不更新缓存。

日期相同且区域/缓存格式匹配时跳过完整抓取，不写缓存；否则复用探测页完成该区分页。HTML 片段无日期，结束后再读一次首页，确认日期和赛季未变再原子替换。`RankingSnapshot.SchemaVersion` 用于一次性升级旧解析器缓存，临时文件使用唯一名称避免插件重载冲突。

布局已收为大区选择和检查按钮，加一行日期/人数/简短状态；完整状态、源地址与赛季放入悬停提示。移除“更新策略”卡片、服务器/数据中心选择及长篇常驻说明。网页预览包含实际抓取快照，其他页面仍为示例。

142 项默认离线检查含原有本地化/悬浮入口/主题/查询、27 项按天检查、18 项真实官网 DOM 片段检查，以及 3 项滚动分页与最终日期校验回放。样本出处见 `tests/LocalizationChecks/Fixtures/sources.json`；只保留相关元数据和前两行，完整源页面留在运行产物中。

本次四区真实抓取共 1000 条：日服 300、北美 300、欧服 300、大洋洲 100。JP/NA/EU 的原始分页含 6 批数据加空终止页，OC 含 2 批加空终止页。后续同日启动检查四区均报告缓存一致，不重抓分页。运行产物在 `/workspace/artifacts/ccstats-official-rankings/`。

可手动复现（默认离线测试不会联网）：

```bash
dotnet run --project tests/LocalizationChecks -c Release --no-build -- --capture-rankings /tmp/ccstats-live-rankings
```

`--reparse-rankings <目录>` 用当前解析器重新解析已保存原始页，不联网。CLI 与插件共用相同抓取服务。真实游戏宿主 UI 尚未运行，编译与 HTTP 验证不能替代游戏内验证。

## 结算指标和胜率图表

`CrystallineConflictPostMatchRow` 已保存 `DamageDealt`、`DamageTaken`、`HPRestored`、`TimeOnCrystal`；`CrystallineConflictMatchManager` 从游戏结算包读取，`ToScoreboard` 转为详情使用的 `CCScoreboardTally`。此次仅调整显示：15 列默认可见，核心四项 NoHide，提供恢复全部列按钮、中文表头、千位分隔及横向滚动。排序 ColumnUserID 仍沿用原始字段散列，数据库字段和 CSV 原值不变。结算默认窗口为 1240×850/870，取消先自动缩窄后再调整的临时行为；其他标签尺寸保留。

`WinRateLabel` 使用胜负有效样本生成百分比和分子/分母，零样本返回不可用符号。概览结果、地图、比赛类型、职业和玩家汇总在进度条上直接显示这些数值，悬停显示胜负及记录总场数。每个汇总行保持其原有统计口径，对阵职业/玩家胜率按已有本地玩家视角计算。新增 4 项格式/空样本检查，默认回归总数 142。

网页详情预览展示 10 名玩家与双方合计，伤害、承伤、治疗和推车秒数使用示例记录；地图/职业/模式/玩家胜率文字从对应示例源记录计算，浏览器检查验证数值一致，并验证队伍合计和贡献占比切换。`docs/ui-match-detail-preview.png` 与 `docs/ui-winrate-preview.png` 为对应截图。正式 ImGui 界面仍需游戏内检查。

比赛类型汇总复用现有 `IncrementAggregateStats`，随同一批筛选记录增删并刷新；百分比和胜负计数保持相同口径。
