# 国服桌面测试版

基于当前桌面版，使用 `CCStatsEdition=China` 构建，保留原有战绩、分析和回放功能。

- 不编译排行榜页面及 `PvpStats/Leaderboards` 服务；没有排行榜启动更新和后台网络请求。
- 导航保留八个页面；页面内部使用原有编号，移除榜单不会错开设置、个人中心等入口。
- 使用独立的 `%LocalAppData%\CCStatsDesktop-CN` 数据目录；国际版仍使用原目录。
- 补充已固定版本的国服服务器名称资料，来源和摘要位于 `CCStats.Core/Resources/china-worlds.json`。
- 补充国服结算列名称及明确的获胜队伍文字；仍须十人成绩完整、名单一致才确认结算。
- 游戏进程和窗口自动识别、托盘运行、关闭选择、主题、JSON/数据库导入等沿用当前版本。

构建：

```sh
dotnet restore CCStats.Desktop.slnx --locked-mode
dotnet run --project tests/DesktopChecks -c Release --no-restore -p:CCStatsEdition=China
dotnet publish CCStats.Desktop/CCStats.Desktop.csproj -c Release --no-restore -p:CCStatsEdition=China -o artifacts/desktop-cn
```

Windows 验证同时覆盖国际版和国服版，分别检查九/八个导航入口及排行榜实现是否隔离。国服版本的测试包名称为 `CCStats-Desktop-CN-internal-test`，包内 `build-info.json` 的 `Edition` 为 `China`。

这不是国服实机数据验证报告：当前没有国服客户端或国服内存诊断样本，内存布局仍基于已有读取实现及地址校验。签名不匹配时停止连接，不自动猜测偏移；已知的结算、完整伤害和命中瞬间状态限制仍然存在。
