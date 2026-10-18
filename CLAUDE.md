# CCStats：Claude 接手说明

先阅读 [交接文档](docs/claude-handoff.md)。此前迁移交接已结束，项目继续在本仓库维护；交接文档中旧的暂停状态只作历史记录。

- 项目：HanaSi0/CCStats，国际服 FF14 Dalamud API 15，C#/.NET 10。
- 当前版本：1.0.9；已接入 3D 回放范围裁剪和七图场外裁边。设置的 LB 采集测试页已移除；用户要七张官方客户端二维贴图，禁止用模型俯视图代替。七张原始 PNG 已下载、验证并保存在仓库，见 `docs/exports/official-maps-2d/` 与交接文档追加部分。
- 使用当前检出，不必额外创建 worktree。保留用户已有改动。
- 不修改、纳入提交或发布 `tools/licensing-prototype/`；本次需求不包含授权/加密功能。
- 模型是显示资源，不是游戏导航/碰撞数据；不得把推算范围、未观测机制或云端测试称为游戏内验证。
- Release 与 Debug 顺序构建，避免 Dalamud 打包器争用。云端 .NET 路径/权限问题见交接文档；其他机器按自己的环境配置。
- 发布前先完成画面与性能核验、构建和回归检查。不要因为存在交接分支就自动打标签或发布。
- 原项目 MIT 许可及署名保留。GitHub 作者为 HanaSi0，README 致谢上游 SaMo/wrath16；不要删除 LICENSE.txt。

最新进展：在 `review/claude-walkable` 上完成七图场外裁边，并从原始导出恢复九霄云上两处出生圆台和连接区域，详见 `docs/arena-crop.md`；随 1.0.6 交付。

九霄云上后续修复（随 1.0.8 发行）：完整核心原始 GLB 重建并按相同轮廓裁切，修复旧包围框内部缺面；256,860 面，分层地面 31,696 项，598 检查与双构建通过。查看 `docs/arena-crop.md` 末节及 `docs/research/cloud-nine-repair-verification.json`；旧网格比较有 64 项源模型高度变化，不宣称该比较零差异通过。

机制渲染后续（随 1.0.9 发行）：24 种独特占位符与“机制图例”，5 Hz 机关对象合并记录，阴阳师仅悬浮眼睛+动作范围、实际 Mini 状态缩小棋子、全场热浪按状态显示。详情见 `docs/map-mechanics-implementation.md`。不能宣称全部动态机制自动完成：书库供给/桥端点/生成状态未稳定映射；社区/官网本轮 CONNECT 403，已保存网络域名草稿，待用户在环境设置保存并发布。正式版本为 1.0.9。

机制占位符精修：阴阳师眼睛按自身 32366 真实读条睁开、结算/中断闭合，移动时强制闭眼；不凭停下就发动。新增独立 EntityId，680 项逻辑检查、最终 Release/Debug 构建通过。样式增加面部、羽片、草席、木框、盔甲和材质明暗，仍非原生模型。七图原始导出无可识别的VFX/加速带触发器，不能据此提取准确静态冲刺带；见 sprint-zone-resource-inventory.json。网络草稿另增 lds-img.finalfantasyxiv.com，仍待用户保存并发布。随 1.0.9 发行。
