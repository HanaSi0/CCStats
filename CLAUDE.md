# CCStats：Claude 接手说明

先阅读 [交接文档](docs/claude-handoff.md)。用户已暂停当前功能开发，并要求迁移至 Claude；本分支保存交接时的未发布代码，不表示新版本已经完成。

- 项目：HanaSi0/CCStats，国际服 FF14 Dalamud API 15，C#/.NET 10。
- 最新正式版：1.0.5；待办为 3D 回放的大范围地面特效裁剪。
- 使用当前检出，不必额外创建 worktree。保留用户已有改动。
- 不修改、纳入提交或发布 `tools/licensing-prototype/`；本次需求不包含授权/加密功能。
- 模型是显示资源，不是游戏导航/碰撞数据；不得把推算范围、未观测机制或云端测试称为游戏内验证。
- Release 与 Debug 顺序构建，避免 Dalamud 打包器争用。云端 .NET 路径/权限问题见交接文档；其他机器按自己的环境配置。
- 发布前先完成画面与性能核验、构建和回归检查。不要因为存在交接分支就自动打标签或发布。
- 原项目 MIT 许可及署名保留。GitHub 作者为 HanaSi0，README 致谢上游 SaMo/wrath16；不要删除 LICENSE.txt。
