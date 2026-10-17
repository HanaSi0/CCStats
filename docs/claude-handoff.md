# Claude 迁移交接

交接日期：2026-10-18（日本时间）。本文件和源码以迁移分支当前提交为准；较早的开发文档含历史检查数量和旧界面描述，不代表当前状态。

## 从哪里继续

仓库：https://github.com/HanaSi0/CCStats

迁移分支：`handoff/claude`。正式版主分支仍为 1.0.5（提交 `59a335f`，标签 `v1.0.5`）。迁移分支包含该版本及随后尚未发布的范围裁剪实现，另已按用户要求移除 LB 采集测试页并取得七张官方二维地图原图；版本号尚未提升。

```bash
git clone --branch handoff/claude https://github.com/HanaSi0/CCStats.git
cd CCStats
```

把目录交给 Claude Code，读取根目录 `CLAUDE.md`，或把本文件作为 Claude 的交接上下文。原聊天不会自动迁移，但当前任务、关键限制和验证结果已写在这里。

## 用户当前目标与停止点

用户看到 3D 回放中的大范围浅蓝效果延伸到无法活动的地图外围，要求：超出玩家可去区域的部分可以不显示，先试验，可行再实装并发布更新。

实现到一半时，用户要求暂停并询问代码改到了哪里，随后提出迁移到 Claude。因此迁移时没有继续推进画面验证、正式构建或新版本发布。后续应先审查当前方案并验证，而非直接发布。

## 已写入的未发布实现

1. `PvpStats/Analysis/ReplayArenaMask.cs`（新增）：从不透明网格的近水平表面生成 0.5 米栅格显示范围，各地图设独立高度带，向外扩一格保护道路边缘和窄坡道。缓存积分图，快速接受完全位于内部的三角面，剔除完全外部的三角面；边界三角面按栅格行的连续区段进行多边形裁切。显示高度来自筛选后的地面，避免范围随峡谷或高大装饰拉出尖峰。
2. `ReplayMapMesh.cs`：加载内嵌地图时构建并缓存 `ArenaMask`。这一步随现有异步地图加载执行。
3. `ReplaySceneGeometry.cs`：新增可嵌套、可恢复状态的 `ClipTo` 作用域；指定范围的三角面经裁剪后写入原顶点列表。
4. `Windows/Replay/MatchReplay3D.cs`：地图机制与 LB 地面范围接入裁剪，默认开启“裁剪范围”开关，切换会使渲染失效并重绘。
5. `ReplaySummons.cs`：仅召唤物地面波纹接入裁剪；空中占位符与目标连线不裁。棋子、朝向、护盾球等也不裁。
6. `tests/LocalizationChecks/ReplayAreaClipChecks.cs`（新增）及 `Program.cs`：增加裁剪回归检查，可选导出实际 C# 绘制结果供浏览器实验使用。

尚未改写 Direct3D 着色器、地图资源或实际技能半径。裁剪只控制复盘显示，不改变命中统计、采集数据或原始战绩。

## 验证状态及需要重点复核的地方

已执行：

- 完整纯逻辑测试通过：574 项（1.0.5 为 550 项）。
- 覆盖分离地面之间的空洞、越界剔除、内部原拓扑、颜色与高度插值、作用域恢复、七地图范围非空、沙漠两侧补建圆形地板和中央路保留、远处峡谷排除。
- 单个 30 单位圆形 LB 的云端 CPU 试验：9,405 个输出顶点，约 1.8 毫秒。这仅为一次样本，不是帧率、最坏情况或游戏性能结论。
- 可选实验导出：`CCSTATS_CLIP_FIXTURE=/tmp/ccstats-clipped-area.json dotnet run --project tests/LocalizationChecks -c Release --no-restore`。该文件含实际 C# 生成的 80 单位范围顶点；迁移时未完成浏览器画面对比。

尚未执行：

- 范围裁剪的画面可行性仍未确认，尚未打包发布新版本。后续移除 LB 采集页面时补做的构建/检查见下方“追加交接”。
- 裁剪前后画面比较、七地图高台/坡道/窄路边缘检查、多个大范围同屏的耗时与顶点预算验证。
- 游戏内 FFXIV/Dalamud/Direct3D 的实际运行测试。

当前方案是视觉近似，**不是可靠的可行走区域**。高度带和坡度筛选可能保留低矮岩石，也可能误裁高台、跳跃通道或缺失导出表面；半米栅格可能有阶梯边缘。需要看实图，不能仅以测试通过判定实装可行。必要时改进范围来源或保留关闭开关。还应检查裁剪增加的几何量是否触及 GPU 动态顶点上限（`ReplayGpuRenderer.MaxDynamicVertices = 120000`），并保留真实技能尺寸，只裁显示。

## 正式版 1.0.5 已有功能，避免回退

- 仅水晶冲突；中文界面；命令 `/ccstats`；悬浮入口支持打开/关闭、右键拖动。
- 历史战绩、查询、职业/玩家分析、配色主题、旧 PvP Tracker 导入、官方国际服四大区榜单。
- 3D 实时渲染回放、30 Hz 目标走位采样、双方血量/Buff、时间轴、水晶轨迹与 2D 进度条、红蓝三棱锥及单字职业标签、LB 和召唤物表现。
- 地图机制按客户端动作 ID 分类，已接入 13 个动作；伤害、分摊、护盾、恢复、吸引、击退和视线分别表达。不在地图上画机制名称、倒计时或通用三角标记。
- 机制绘制由实际读条、动作结算及位置驱动。纯计时器、未知动作、缺失落点不补造成真实机制。
- 赤土红沙原始导出缺失的两侧圆形地板已经补建为显示地板，并保留透明雾/流沙层；透明层不参与地面高度估计。勿再次删除补建地板。

相关实现：`LimitBreakVisuals.cs`、`MapMechanicVisuals.cs`、`ReplayMechanicObservation.cs`、`CrystallineConflictReplayCapture.cs`、`ReplaySummons.cs`、`ReplayGpuRenderer.cs`。

## 构建与检查

.NET SDK 10.0.402、Dalamud.NET.Sdk 15.0.0。完整发布流程见 `.github/workflows/release.yml`；这是可复现环境的主要依据。

在新机器上安装 .NET SDK，并把官方 Dalamud SDK 解压到自己的目录后设置 `DALAMUD_HOME`。CI 使用官方固定 SDK：

- 下载：`https://raw.githubusercontent.com/goatcorp/dalamud-distrib/904e0b30d912e68c6de1812f2f143ee496b1b55d/latest.zip`
- SHA256：`82d3efad4615d4b40fdabd458132473d6b69e341cd700293a6bb3ff2b5b77547`

```bash
python3 tools/generate_localization.py
dotnet restore PvpStats.sln --locked-mode -p:EnableWindowsTargeting=true
dotnet build PvpStats.sln -c Release --no-restore -p:EnableWindowsTargeting=true
dotnet build PvpStats.sln -c Debug --no-restore -p:EnableWindowsTargeting=true
dotnet restore tests/LocalizationChecks --locked-mode
dotnet run --project tests/LocalizationChecks -c Release --no-restore
dotnet run --project tools/TermExporter -- PvpStats /tmp/ccstats-ui-audit.json
python3 tools/audit_ui.py /tmp/ccstats-ui-audit.json
git diff --check
```

本次 Codex 机器已有：`/workspace/.dotnet/dotnet`、`/workspace/.dalamud`、`/workspace/.nuget/packages`。这里运行 .NET 时用 `DOTNET_CLI_HOME=/workspace/.dotnet-cli`、`NUGET_PACKAGES=/workspace/.nuget/packages`。该环境的 NuGet 命名互斥锁需工具的沙箱外执行权限；这不是项目代码故障，也不代表 Claude 的新机器有相同要求。

不需要为了继续当前功能重新上传原始模型：七张显示地图已内嵌在 `PvpStats/Resources/ReplayMaps/`。若要重新删减模型，原始 GLB、多卷压缩包、临时浏览器脚本和图片位于本次机器 `/workspace/map-analysis/`，研究快照位于 `/workspace/map-research/`，它们不在 Git，需另行迁移；不要假定新机器能读取这些目录或旧聊天附件。客户端研究摘要与已核实表已保存在 `docs/research/`。

## 发布注意事项

正式库链和测试库链都提供同一正式版：

- `https://raw.githubusercontent.com/HanaSi0/CCStats/main/repo.json`
- `https://raw.githubusercontent.com/HanaSi0/CCStats/main/repo-test.json`

用户要求更新时，在验证完成后可考虑下一版本 1.0.6。更新 csproj、插件 manifest、发布说明和 README 版本链接；使用 `tools/prepare_release.py --version 1.0.6 --direct`，同步两个索引及 `plugin-packages/v1.0.6/`。打标签会触发 GitHub Actions 正式发布，未完成时不要打标签。

推送 main 前 fetch 并检查用户是否自行编辑了 README；保留其删减后的简短介绍。不要重新加回已被用户删除的泛化说明或营销段落。原许可必须打入 ZIP，主作者 HanaSi0，上游致谢保留。

`tools/licensing-prototype/` 是未跟踪的独立遗留文件夹，未纳入此次迁移，不读取或提交；用户此前已明确暂缓加密功能。不要迁移凭据、令牌或云环境认证文件。

## 可直接发给 Claude 的接手指令

> 请阅读 CLAUDE.md 和 docs/claude-handoff.md，接手 CCStats。当前正式版是 1.0.5，handoff/claude 分支保存了尚未发布的 3D 范围裁剪实现。先审查并进行裁剪前后画面对比，检查七张地图道路、坡道、高台和多范围同屏的性能；这是显示地面轮廓而非真实导航网格，不能夸大准确性。确认方案可行后完成实现、检查与下一版发布。保留现有功能，不修改授权原型，不把云端测试当作游戏内验证。

## 追加交接：删除 LB 采集页及官方二维地图

用户要求删除设置里的“LB 采集”，并提供七张官方二维地图及继续完善交接文档。曾错误生成模型俯视图，用户明确纠正为官方地图；所有错误导出图、ZIP 和导出脚本已撤下，不能继续拿模型渲染图代替官方贴图。

- `PvpStats/Windows/ConfigWindow.cs` 已删除“LB 采集”设置项、页面方法及仅用于该页的列表字段。正常比赛的 LB 记录和回放必须保留。后台 `LimitBreakCaptureService`/旧诊断工具仍在源码，但该测试页已无启动入口。
- [官方客户端地图清单](exports/official-maps-2d/README.md) 和 [结构化资源表](exports/official-maps-2d/assets.json) 提供七张地图的真实 Map 行 ID、资源路径、SizeFactor 和 Offset。路径已对照公开客户端 Map、TerritoryType 和 PlaceName 表核对。
- 下载脚本：`python3 tools/download_official_maps.py`。目标是原始 `ui/map/s5p1/00/s5p100_m.tex` 到 `s5p7` 的 PNG 解码图，经社区 XIVAPI 提供，不做模型渲染或重绘。全部成功才打包；失败必须保持“未取得”状态。
- **七张官方原图现已全部取得并提交**：[PNG 与说明](exports/official-maps-2d/README.md)、[七图 ZIP](exports/official-maps-2d/official-maps-2d.zip)。均为完整 2048×2048 PNG，未经裁剪/旋转/缩放/模型渲染，验证了图片解码、尺寸、SHA256 和 ZIP 完整性。
- 初次访问出现 403，随后重查发现原先拼接的 `s5p1.00_m.tex` 文件名有误。正确公式已在插件 `CrystallineConflictMatchManager.cs` 中存在：`ui/map/{id}/{id.Replace("/", "")}_m.tex`，即 `ui/map/s5p1/00/s5p100_m.tex`。改正路径并使用正常请求头后，七张均下载成功。不能再把本项写作“尚未取得”或拿错误链接交给用户。
- 下载地址是社区 XIVAPI 对原始客户端贴图的 PNG 解码结果，并非 Square Enix 官网文件直传。网络域名草稿曾保存但未发布；当前原图获取已完成，新 Claude 环境可直接用仓库内 PNG。
- 移除设置页后，Release/Debug 构建已通过（0 错误）；没有发布新版本。574 项回归检查通过，UI 审计检查了 411 项且无未映射候选。裁剪画面仍未完成对比，未发布新版本。

接手优先顺序：七张**官方原图**已备齐，先确认用户需要如何使用这些原图；之后再继续尚未验证的范围裁剪。正式 main 与标签仍为 1.0.5，迁移分支没有 1.0.6 标签。
