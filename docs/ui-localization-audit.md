# 界面翻译复查（0.1.3）

经过两轮 C# 语法树导出与人工复核，补齐统计表头、时间线、技能事件、技能分析、纪录、筛选条件、菜单、图表坐标和设置说明。

显示翻译集中在 `translation/ui-layout.zh-CN.json`，用户已确认的游戏名词仍以原 CSV 为准。新增文案不得覆盖已确认译名，角色姓名、世界名、数据库字段、枚举值和 ImGui 控件 ID 保持原样。

静态扫描最终复核 257 个候选，无未映射候选。内部无表头表格的列 ID、数字格式、ID 等技术文字列为明确例外；动态选项、纪录标题、组合表头和提示拼接另作人工检查。原始 JSON 编辑器与客户端数据表的未收录名称不改写。

复查命令：

```sh
dotnet run --project tools/TermExporter -- PvpStats /tmp/ccstats-ui-audit.json
python3 tools/audit_ui.py /tmp/ccstats-ui-audit.json
```

静态扫描作为辅助检查，无法替代游戏客户端的字体、原生控件菜单和动态游戏数据验证。发布工作流已加入上述检查。
