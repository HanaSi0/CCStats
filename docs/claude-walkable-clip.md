> 历史裁剪设计记录；已随正式版本交付。当前构建和发布流程以 [开发文档](development.md) 为准。

# 范围裁剪改用可站立网格（Claude 交付，给 GPT 的应用说明）

> 以下为供稿方交付说明。接收后的实际编译、测试、补修和未完成项见 [当时检查记录](reviews/claude-walkable-review.md)。其后的场外裁边与出生区恢复见 [最新处理说明](arena-crop.md)，出生区缺失现已修复。

基于 `handoff/claude`（`fed2981`）。正式版仍为 1.0.5，本改动**未发布、未打标签**。

## 解决了什么

`handoff/claude` 原裁剪用高度带推算地面轮廓。离线逐行移植后与七张官方二维地图对比：赤土红沙 58%、海岸斗场 69% 的保留区仍在官方轮廓外（墙外沙丘、平台下沙地照样被覆盖）；九霄云上出生台 41% 被误裁；`Surface()` 取格内最高面，贴墙格会被抬到墙顶，墙边出现竖片。

现在：

- 七张地图的显示范围与 1.0.5 相同（竞技场 + 紧邻景物），只提高精度：平滑光照（40° 折角保留硬边）、8 位颜色、无损合并共面顶点、补齐机关大殿庭院 5 处空洞。
- 新增每图 `.ccwalk.gz` 可站立网格：只含落在官方区域地图轮廓内、坡度 ≤35°、净空 ≥1.8 的地面，补木板缝，去小碎块；一格可有多层（桥上/桥下、平台/沙地）。
- `ReplayArenaMask.FromWalk` 用该网格做轮廓；`ClipTo(mask, 事件高度)` 让范围贴在离事件高度最近的那层；与事件高度差 >4 单位的层不画，避免平台边缘向下拉出竖条。分支原有的三角形裁剪与 `ClipTo` 作用域保留。
- 2D 回放机制范围使用同一裁剪；旧记录缺失高度时优先取可站立地面。
- GPU 动态顶点上限 120,000 → 400,000，超出时截掉末尾三角形而不是抛错；机制/LB 预算 12k/48k → 30k/120k。

## 文件清单

| 文件 | 说明 |
| --- | --- |
| `PvpStats/Analysis/ReplayArenaMask.cs` | 新增 `FromWalk`、`Layered`、`MaxLayerDrop`、按层裁剪；旧构造函数与高度带 `Build` 保留作回退 |
| `PvpStats/Analysis/ReplayWalkGrid.cs` | 新增：读取 `.ccwalk.gz`，查询分层地面 |
| `PvpStats/Analysis/ReplaySceneGeometry.cs` | `ClipTo(mask, reference)` |
| `PvpStats/Analysis/ReplayMapMesh.cs` | 加载 `.ccwalk.gz`；`Walk`、`SurfaceHeight`、`EstimatedDisplayHeight`、`LoadEmbeddedWalk` |
| `PvpStats/Analysis/ReplaySummons.cs` | 召唤物波纹传入召唤物/效果高度 |
| `PvpStats/Windows/Replay/MatchReplay3D.cs` | 传事件高度、预算、顶点截断保护 |
| `PvpStats/Windows/Replay/MatchReplayPanels.cs` | 2D 机制范围裁剪 |
| `PvpStats/Interface/Rendering/ReplayGpuRenderer.cs` | `MaxDynamicVertices` 改为 `internal const` 400,000 |
| `PvpStats/PvpStats.csproj`、`tests/LocalizationChecks/LocalizationChecks.csproj` | 嵌入 `*.ccwalk.gz` |
| `PvpStats/Resources/ReplayMaps/*.ccmap.gz`、`*.ccwalk.gz`、`manifest.json`、`README.md` | 精修模型与可站立网格 |
| `tests/LocalizationChecks/Replay3DChecks.cs` | 新面数 |
| `tests/LocalizationChecks/ReplayAreaClipChecks.cs` | 新增 11 项：七图分层轮廓、桥上/桥下、跨层不下拉、赤土红沙墙外沙丘排除、海岸斗场沙地排除、广场范围不上墙 |
| `tools/refine_replay_maps.py`、`tools/replay_walkable.py` | 资源生成工具（Python，numpy/scipy/Pillow/fast-simplification） |

## 应用步骤

```bash
git fetch origin
git checkout claude/walkable-clip        # 已推送的分支；或在 handoff/claude 上：git am 0001-*.patch
```

没有分支访问时，把压缩包里的 `files/` 目录覆盖到仓库根目录（保持相对路径）。

## 必须执行的检查（Claude 环境没有 .NET，C# 未编译）

```bash
python3 tools/generate_localization.py
dotnet restore PvpStats.sln --locked-mode -p:EnableWindowsTargeting=true
dotnet build PvpStats.sln -c Release --no-restore -p:EnableWindowsTargeting=true
dotnet build PvpStats.sln -c Debug --no-restore -p:EnableWindowsTargeting=true
dotnet restore tests/LocalizationChecks --locked-mode
dotnet run --project tests/LocalizationChecks -c Release --no-restore
git diff --check
```

编译报错时优先查看：`ReplayArenaMask` 私有构造函数链（`this(...)` 后给只读字段 `_walk` 赋值）、`Clip` 内的局部函数 `Covered`、`MatchReplay3D` 中 `ReadOnlySpan` 切片。测试数值已用逐行等价的 Python 实现在真实资源上核对（广场范围最高点 −0.59，低于阈值 0.26；6 个 30 单位 LB 共 38,730 顶点）。

重新生成资源（可选，结果逐字节一致）：

```bash
git show v1.0.5:PvpStats/Resources/ReplayMaps/<map>.ccmap.gz > /tmp/maps-1.0.5/<map>.ccmap.gz   # 七张
python3 tools/refine_replay_maps.py --input /tmp/maps-1.0.5 --output PvpStats/Resources/ReplayMaps
```

## 游戏内验证（发布前）

1. 七张地图各开一局 3D 回放：地图外观、红蓝棋子高度、水晶轨迹正常。
2. 赤土红沙东/西广场护盾、海岸斗场中央平台、机关大殿庭院边、九霄云上跳板平台：范围贴地、不上墙顶、不铺到场外、平台边无竖条。
3. “裁剪范围”开关关闭后恢复 1.0.5 画法。
4. 多个大范围 LB 同屏时回放帧率；如有明显卡顿，可按事件缓存裁剪结果（仅透明度随时间变化）。
5. 2D 回放机制范围正常。

## 已知限制

- 可站立网格由显示模型推导，不是游戏碰撞/服务器判定，只控制复盘显示。
- 九霄云上两端出生区不在 3D 模型内（1.0.4 重建时裁掉），该处无地面、不显示范围；需原始 GLB 补回。
- 地面装饰物底部可能有零星小空洞。

## 不要做

- 不修改、不提交 `tools/licensing-prototype/`。
- 验证完成前不提升版本、不打标签、不推 main。发布时按 `docs/claude-handoff.md` 的 1.0.6 流程，保留 README、LICENSE 与上游致谢。
