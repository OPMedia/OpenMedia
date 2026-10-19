# 播放与转码（SPEC/playback）

> 决策依据：ADR-0010（直传优先、分辨率档位、转码可禁用）。
> 出处：Emby 3.5.2.0 源码 + 闭源二进制取证（研究员报告）。转码 API 与调度在
> 闭源 `Emby.Server.MediaEncoding.dll`（`MediaBrowser.Api\Playback` 目录不在
> 克隆内），故本册凡 Emby 不可取证处均标"自定"，不冒充照抄。

## 1. 交付决策矩阵 〔定稿·自定〕

PlaybackInfo 输入：DeviceProfile（LinWeb 验证用例见 api.md §1.2）+ 用户会话 +
媒体探测结果。裁决顺序：

1. **DirectPlay**：容器+编码均在 DirectPlayProfiles 内
2. **DirectStream（remux）**：编码兼容、容器不兼容（mkv → fMP4/TS）
3. **Transcode**：编码不兼容或客户端要求；管理员禁止转码时返回明确拒绝
   （ADR-0010）

Emby 转码决策闭源不可取证——本矩阵为 Serata 自定规则，验收基准 = LinWeb
DeviceProfile 用例 + 真实 4.8 客户端抓包对照（acceptance.md §2）。

## 2. 质量档位 〔定稿〕

原画 / 1080p / 720p / 480p / 360p 五档，客户端报最大档位；每档内置 ffmpeg
映射（编码器、preset、像素格式），管理员可覆盖；HDR→SDR 默认 zscale+bt2390。

## 3. 转码子系统 〔定稿〕

- **ffmpeg**：官方静态版打进镜像，版本锁死（实现时把版本号写入本节）。
- **GPU**：单镜像启动探测 NVENC（`--gpus`）→ QSV/VAAPI（设备节点）→ 软转。
- **HLS**（出处：二进制命令模板取证）：
  `-f segment -max_delay 5000000 -avoid_negative_ts disabled -start_at_zero
   -segment_time {N} -segment_format {fmp4|mpegts} -segment_list_type m3u8`
  （含带头/无头两变体，源自 Emby.Server.MediaEncoding.dll 字符串）。
  `segment_time` 具体值闭源未取证 → **Serata 默认 6s，可配**（自定，已注明）。
- **会话生命周期**：
  - 杀会话双机制（出处：二进制方法/日志名取证）：PlaySessionId 失配 +
    空闲 KillTimer。空闲秒数闭源未取证 → **Serata 默认 30s，可配**（自定）。
  - 优雅退出：先向 ffmpeg stdin 发 `q`，超时再 kill（二进制日志
    "Stopping ffmpeg process with q command" 取证）。
  - 音轨/字幕切换 = 终止旧会话、起新会话（自定，与 Emby 客户端行为对齐）。
  - 临时目录：默认 `数据目录/transcoding-temp`（出处
    `ServerApplicationPaths.cs:176-183`）；可配置覆盖，覆盖时做可写性探测、
    失败回退默认（出处 `ServerApplicationPaths.cs:186-219`）；临时文件名
    `<guid32>.<ext>`（出处 `LiveStream.cs:63-66`）。
  - 分片清理：DeletePartialStreamFiles 语义照搬——会话结束清理本会话分片 +
    启动时扫孤儿目录。
- **并发上限**：默认 CPU 2 路 / GPU 4 路（可配），超限直接拒绝并明确报错
  （自定，Emby 无此机制）。
- **会话联动**（出处 `SessionManager.cs`）：PlaybackStart 且 PlayMethod≠
  Transcode → 清转码信息（598-601）；PlaybackStopped → 关流（880-887）；
  会话带 ReportTranscodingInfo（1721-1733）——`/Sessions` 端点的转码信息
  字段由此而来。
- **Trickplay**：进 v1 默认关（库级开关），缩略图网格复用 ffmpeg 管线；
  interval/网格尺寸可配。

## 4. 字幕 〔定稿〕

- 外挂：`DeliveryMethod: External` + `DeliveryUrl` 原格式直传（api.md §1.3）。
- PGS/VobSub：必须烧录（subtitles filter 进转码路径）；文本字幕优先外挂。
- 内嵌文本字幕在 DirectStream 下的交付方式（Extract/Embed）：Emby 行为闭源
  不可取证 → **Serata 规则：DirectStream 时按 SubtitleProfile 声明交付**
  （LinWeb 声明 Embed: ass/ssa/pgssub/srt + External: ass/ssa/srt/subrip/vtt，
  照 api.md §1.2 全集支持）。

## 5. 进度与观看状态阈值 〔定稿·照 Emby 展开〕

出处：`UserDataManager.cs:221-280 UpdatePlayState` + ServerConfiguration 反射
默认值。可配（0-100 / 1-100 校验，与 Emby playbackconfiguration 对齐）：

| 规则 | 值 |
|---|---|
| 进度 < **MinResumePct=5%** | position 清零，不记录进度 |
| 进度 > **MaxResumePct=90%** 或 ≥ 总时长 | **Played=true**、position 清零 |
| 中间段停下但总时长 < **MinResumeDurationSeconds=300s** | 直接 Played=true |
| 条目无总时长 | 假设已看完（Played=true） |
| 客户端上报 Stopped 无 positionTicks | PlayCount++、Played=true（`SessionManager.cs:917-924`） |

继续观看列表聚合规则照 Emby（集按剧集归并、电影独立，Filters=
`IsResumable,IsUnplayed` 查询语义，api.md §1.5）。

## 6. 验收

- PlaybackInfo 三档裁决表测（LinWeb DeviceProfile 固定输入）；
  空 TranscodingProfiles 永不返回转码 URL。
- 阈值表逐行单测（5%/90%/300s/无时长/无位置五例）。
- 并发上限拒绝、断开 30s 杀进程（先 q 后 kill）、音轨切换重启会话、
  孤儿分片清理——集成测试。
- `/metrics` 含 DirectPlay/DirectStream/Transcode 计数（ADR-0010 达成度量，
  仅日志与指标，不进看板）。