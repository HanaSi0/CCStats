# 七张官方游戏内二维地图

七张游戏客户端原始地图贴图已成功下载，并验证为完整的 2048×2048 PNG。未旋转、裁剪、缩放或重绘，不是三维模型的俯视图。

图片通过社区 XIVAPI 解码客户端 `.tex` 提供；不是 Square Enix 网站直传文件。Map/TerritoryType 表及插件实际地图加载公式用于核对路径。SHA256 和下载地址见 `assets.json` 与 `images/downloads.json`。

**文件名应为 `s5p100_m.tex`，不是 `s5p1.00_m.tex`；此前的点号路径错误已更正，并已实际下载验证全部七张。**

| 地图 | Map 行 ID | PNG | 客户端资源 |
| --- | ---: | --- | --- |
| 角力学校 | 759 | [palaistra.png](images/palaistra.png) | `ui/map/s5p1/00/s5p100_m.tex` |
| 火山之心 | 760 | [volcanic-heart.png](images/volcanic-heart.png) | `ui/map/s5p2/00/s5p200_m.tex` |
| 九霄云上 | 761 | [cloud-nine.png](images/cloud-nine.png) | `ui/map/s5p3/00/s5p300_m.tex` |
| 机关大殿 | 822 | [castletown.png](images/castletown.png) | `ui/map/s5p4/00/s5p400_m.tex` |
| 赤土红沙 | 854 | [red-sands.png](images/red-sands.png) | `ui/map/s5p5/00/s5p500_m.tex` |
| 海岸斗场 | 1060 | [bayside.png](images/bayside.png) | `ui/map/s5p6/00/s5p600_m.tex` |
| 休兵书库 | 1229 | [harmonias.png](images/harmonias.png) | `ui/map/s5p7/00/s5p700_m.tex` |

[下载七张原图 ZIP](official-maps-2d.zip)。ZIP 包含七张 PNG、资源清单、校验记录和本说明。

重新下载：

```bash
python3 tools/download_official_maps.py
```

脚本按客户端实际路径下载原始贴图的 PNG 解码结果。只在七张全部成功后打包；任意失败都会报错，不生成伪装完整的新包。客户端坐标参数 `SizeFactor`、`OffsetX` 和 `OffsetY` 保存在资源表，后续接入二维回放时使用客户端地图换算，不沿用模型俯视图的中心/缩放。

普通场与自定赛共用各自地图资源。初次访问出现网络 403，重查并使用正确路径和请求头后七张均下载成功，当前不是待下载状态。Claude 新环境可直接使用仓库内 PNG，无需再次联网。
