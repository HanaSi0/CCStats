# CCStats：Claude 接手说明

先阅读 [交接文档](docs/claude-handoff.md)。此前迁移交接已结束，项目继续在本仓库维护；交接文档中旧的暂停状态只作历史记录。

- 项目：HanaSi0/CCStats，国际服 FF14 Dalamud API 15，C#/.NET 10。
- 当前版本：1.0.6；已接入 3D 回放范围裁剪和七图场外裁边。设置的 LB 采集测试页已移除；用户要七张官方客户端二维贴图，禁止用模型俯视图代替。七张原始 PNG 已下载、验证并保存在仓库，见 `docs/exports/official-maps-2d/` 与交接文档追加部分。
- 使用当前检出，不必额外创建 worktree。保留用户已有改动。
- 不修改、纳入提交或发布 `tools/licensing-prototype/`；本次需求不包含授权/加密功能。
- 模型是显示资源，不是游戏导航/碰撞数据；不得把推算范围、未观测机制或云端测试称为游戏内验证。
- Release 与 Debug 顺序构建，避免 Dalamud 打包器争用。云端 .NET 路径/权限问题见交接文档；其他机器按自己的环境配置。
- 发布前先完成画面与性能核验、构建和回归检查。不要因为存在交接分支就自动打标签或发布。
- 原项目 MIT 许可及署名保留。GitHub 作者为 HanaSi0，README 致谢上游 SaMo/wrath16；不要删除 LICENSE.txt。

最新进展：在 `review/claude-walkable` 上完成七图场外裁边，并从原始导出恢复九霄云上两处出生圆台和连接区域，详见 `docs/arena-crop.md`；随 1.0.6 交付。
