# Windows 独立版

采用两个独立入口：`PvpStats.sln` 继续构建 Dalamud 插件，`CCStats.Desktop.slnx` 构建 Windows 桌面版。插件目前仍是 1.1.0，桌面构建预览从 0.1.0 单独编号，桌面标签为 `desktop-v*`，不会触发插件发布或改写 repo.json。

## 当前范围

WPF 桌面窗口、原 Logo、五个页面、回放 JSON 导入、原始玩家全名/记录概况、最近文件、拖放、配置与原始文件隔离。Windows 10/11 x64 可运行，发布包内置 .NET，无需 Dalamud；尚无 ARM64 包和签名安装器。

`CCStats.Core` 通过链接源码复用分析、LB/召唤物/地图机制、坐标插值、相机、七图/分层地面及榜单抓取代码，不引用 Dalamud 或 FFXIVClientStructs 程序集。保留插件现有编译方式，暂未将插件项目改为引用核心库，以降低迁移对已有用户的影响。

这些逻辑已经可独立编译，并不代表全部桌面界面已经完成。战绩库、官方榜单页面、2D/3D 播放、完整统计界面仍待移植。

## 独立采集试验

可在纯净游戏启动后连接 `ffxiv_dx11.exe`。仅使用 Windows 只读进程权限及 ReadProcessMemory，不注入 DLL、不写进程内存。单次连接仅支持一个游戏客户端。

公开布局参考固定为 [FFXIVClientStructs d6397a76](https://github.com/aers/FFXIVClientStructs/tree/d6397a76b3a63c2b247025dc296653182b718ca6)。从本机 EXE 的 .text 段匹配唯一签名、解析 RIP 相对地址，再读取区域和已加载玩家的位置、朝向、血量、职业、世界编号、状态。签名唯一性和字段合理性校验只是防错措施，不能替代客户端版本适配实测。本环境没有游戏，尚未验证该布局在用户客户端中有效，界面明确标为试验。

目标位置采样 30 Hz、状态 5 Hz、界面 5 Hz；只从前 200 个网络对象槽的角色位读取玩家，未读到的模型不补造。后台采样，连续 JSON 流式写盘，不把整局帧堆积在内存；64 MiB 段上限，切图/断开时收尾，未完成文件保留 .partial。目标采样频率不是实测保证。

当前未采集队伍归属、水晶、动作效果包、完整结算、伤害来源或可靠比赛开始/结束；不把 HP 差当作造成伤害，不把观测会话伪装为比赛。记录中 `RecordKind=IndependentObservation`，页面标为观测片段。普通游戏进程不存在、权限不匹配、版本签名不匹配时直接报告原因。

## 构建与检查

```bash
dotnet restore CCStats.Desktop.slnx --locked-mode
dotnet run --project tests/DesktopChecks -c Release --no-restore
dotnet publish CCStats.Desktop/CCStats.Desktop.csproj -c Release --no-restore -o artifacts/desktop/publish
```

Linux 使用 .NET SDK 10.0.402 加 EnableWindowsTargeting 可交叉编译/打包，不能执行 WPF EXE。`.github/workflows/desktop-release.yml` 在 Windows 启动实际发布 EXE，依次点击五个导航入口、导入测试记录、检查配置持久化并保存窗口 PNG；同时运行插件原有离线回归。只有这些检查通过才发布桌面预览。

数据单独保存至 `%LOCALAPPDATA%\CCStatsDesktop`，不改插件数据库。EXE 预览未签名，无法宣称通过实机游戏采集、性能、客户端兼容或所有功能验证。
