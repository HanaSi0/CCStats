CCStats Desktop 0.1.0 · Windows x64 预览

解压全部文件，双击 CCStats.Desktop.exe。已包含 .NET 运行时，不需要 Dalamud。

已提供：独立桌面窗口、回放 JSON 导入、玩家与记录概况、最近文件、设置目录，以及实验性只读进程观测。
独立采集：先打开纯净 FF14 并登录，在“独立采集”连接游戏，再开始记录；结束记录或断开后写出 JSON，保存在本地数据目录 observations 中。
采集配置基于公开客户端结构，尚未完成真实游戏验证。签名或数据校验失败时将停止并提示，不能保证当前版本客户端已经适配。

当前不是插件的完整功能替代版。队伍归属、水晶、技能效果包、结算、战绩库、榜单页面、2D/3D 播放界面仍需逐项接入。已保存的独立记录是观测片段，不代表已完成比赛。不会由血量差捏造伤害统计。

用户数据：%LOCALAPPDATA%\CCStatsDesktop
插件版继续维护，版本号和插件库链接不变。

作者：HanaSi0
上游：SaMo / wrath16 PvPStats；MIT 许可见 LICENSE.txt。
客户端结构参考：FFXIVClientStructs，许可见 ThirdParty。
