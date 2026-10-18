# 开发与验证

当前版本：1.1.0。使用现有检出，不必另建 worktree。项目为国际服 Dalamud API 15 / .NET 10；云端能够构建与运行离线逻辑检查，无法启动 FF14、Dalamud 游戏宿主或 Direct3D。

## 构建

云端使用现有 `/workspace/.dotnet`、`/workspace/.nuget/packages` 与 `/workspace/.dalamud`。其他机器按实际位置设置环境变量。

```bash
export PATH=/workspace/.dotnet:$PATH
export DOTNET_CLI_HOME=/workspace/.dotnet-cli
export NUGET_PACKAGES=/workspace/.nuget/packages
export DALAMUD_HOME=/workspace/.dalamud
cd /workspace/CCStats
python3 tools/generate_localization.py
dotnet restore PvpStats.sln --locked-mode -p:EnableWindowsTargeting=true
dotnet build PvpStats.sln -c Release --no-restore -p:EnableWindowsTargeting=true
dotnet build PvpStats.sln -c Debug --no-restore -p:EnableWindowsTargeting=true
dotnet restore tests/LocalizationChecks --locked-mode
dotnet run --project tests/LocalizationChecks -c Release --no-restore
dotnet run --project tools/TermExporter -- PvpStats /tmp/ccstats-ui.json
python3 tools/audit_ui.py /tmp/ccstats-ui.json
```

Release 与 Debug 须顺序构建，避免打包器争用。同一云环境中 NuGet 命名互斥锁可能需要通过工具权限流程执行构建；不要改项目来绕过系统限制。

SDK 10.0.402。官方 Dalamud SDK 固定为 `goatcorp/dalamud-distrib` 提交 `904e0b30d912e68c6de1812f2f143ee496b1b55d` 的 latest.zip，SHA-256 为 `82d3efad4615d4b40fdabd458132473d6b69e341cd700293a6bb3ff2b5b77547`。CI 复核校验值、锁定依赖及全部检查。

## 当前行为与边界

- `/ccstats` 打开主窗口；悬浮图标左键切换开关、右键拖动；设置在主窗口内切换。折叠、字体缩放、鼠标交互仍需游戏内实测。
- 首次识别 PvP Tracker 数据时选择是否导入，之后可从设置手动导入；原库保留。历史修复使用独立修订号，仅成功后标记完成，新导入数据会重新触发。
- 战绩缓存向读取者提供稳定列表快照；保存成功后发布更新。统计页、搜索、分页和分析均使用实际筛选数据，职业分析默认本人。
- 详情包括结算、细项分析与回放、事件时间线、推进曲线、技能分析。高级减伤值是有条件推算，未知状态及特殊伤害不强行给出结果。
- 2D/3D 回放使用已保存观测；缺失的历史走位、技能落点和召唤物无法完整补造。采样目标 30 Hz，长局会按容量限制降采样。七张三维地图与分层地面为显示资源，并非游戏导航或服务器碰撞网格。
- 24 种地图机制近似占位符可在“机制图例”查看。阴阳师按自身实际读条开眼。准确冲刺带位置和书库魔力供给、桥梁动态状态仍未可靠接入，见[机制实现](map-mechanics-implementation.md)。
- 官方榜单按日服、北美、欧洲、大洋洲缓存；启动检查一次、切页不轮询，支持手动刷新。相同官网日期复用缓存；抓取失败保留旧数据。离线测试使用保留的官方 HTML 样本，不能证明官网当前可达或页面未变化。
- 配色预设、窗口大小、筛选和导入选择保存于用户配置；清理仓库不删除用户本机配置、数据库或榜单缓存。

## 发布与资料

`python3 tools/prepare_release.py --version <版本> --direct` 校验安装包并生成 repo.json；同步 repo-test.json，两者继续提供正式版。标签触发 `.github/workflows/release.yml`，构建、检查后上传 Release。当前仓库保留最新包及上一个可用包作为过渡，较早包从 GitHub Releases 或对应标签获取；不重写 Git 历史。

[验证记录](release-validation-1.1.0.md) · [地图处理](arena-crop.md) · [机制资料](map-mechanics-sources.md) · [官方二维地图](exports/official-maps-2d/README.md)

`translation/` 的已确认译文与原始采集依据保留；`source_reference/` 明确是初始翻译时的历史快照，日常改代码请阅读 `PvpStats/`。旧版本说明仅为历史，不代表当前界面。
