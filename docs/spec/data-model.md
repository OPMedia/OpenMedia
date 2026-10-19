# 数据模型（SPEC/data-model）

> 状态：〔定稿〕条目直接照实现；〔草案〕= 待拷问收口。
> 决策依据：ADR-0002（PG 唯一）、ADR-0007（规范化真相源 + 可重建物化载荷）、
> ADR-0006（多源仲裁）、ADR-0009（Emby 迁移种子）、ADR-0008（文件生命周期）。
> 全库 UTC（timestamptz）；主键 bigint；Emby 兼容 Id 输出数字字符串。

## 真相源表（DDL 以本册为唯一来源；验收命令在 §索引与规模预算 一节随实现补齐）

### 用户与安全
- `user`：id、name(唯一)、password_hash(argon2id)、email、email_verified、
  is_administrator、policy(jsonb：按库授权数组、权限位集合——可下载/可发弹幕/
  可建房间/远程访问/是否管理员〔定稿 Q4〕)、avatar_image_id、email_reset_code、
  date_created。
  - 无设备数上限；无全局限速（ADR 精神：点状限速仅登录/弹幕/评论/注册）。
- `api_key`：id、name、token_hash、date_created、last_used。
- `login_audit`：id、user_name、ip、user_agent、success、reason(锁定/成功/失败)、
  created_at。〔定稿：登录审计页数据源〕
- 会话：`session`：id、user_id、device_name、client_name、app_version、ip、
  token_hash、remote_endpoint、last_activity、playback 绑定可空。

### 媒体库与条目
- `library`：id、name、type(movie/tvshow)、paths(jsonb 数组)、options(jsonb：
  刮削语言/国家、是否抓取章节图、Trickplay 开关〔默认关，Q5 已定〕、
  NFO 写回开关〔定稿：库级开关默认关，写回直写、不做额外覆盖保护〕、自定义排除 glob、Trickplay 设置)。
- `item`：核心列（ADR-0007）——id、type(Movie/Series/Season/Episode/BoxSet/
  Playlist/Folder)、library_id、parent_id、name、sort_name、original_title、
  overview、production_year、premiere_date、community_rating、official_rating、
  runtime、path、container、size、date_created、date_modified、
  **provider_ids**(jsonb：tmdb/tvdb/bangumi/anidb id)、**raw_provider**(jsonb，
  ADR-0006 原料存档)、**payload**(jsonb 物化载荷)、**metadata_version**、
  **locked_fields**(text[]，迁移锁定字段，仲裁禁写)、**unbound**(bool)、
  **series_name、season_number、episode_number、airs_before_season、
  airs_before_episode、airs_after_season_number**(照 Emby，AI 仲裁写这些字段
  实现官方顺序纠偏〔定稿 Q2，展开件：解析规则全集见 40 册〕)。
- 关联表：`genre` + `item_genre`、`person` + `item_person`(role/type)、
  `studio` + `item_studio`、`tag` + `item_tag`、`chapter`(index、
  start、name)、`image`(item_id、type[primary/back/banner/logo/thumb]、
  path、width、height、blurhash)。
- `provider_data`：item_id、provider(tmdb/bangumi/ai/emby-import…)、payload
  (jsonb)、fetched_at——RawProvider 的落库形态（ADR-0006）。
- `media_source`：item_id、**id(独立 bigint，兼容 Emby MediaSource id)**、
  path、container、size、protocol(Local/Http/Strm)、name(版本名)。
- `media_stream`：source_id、type(Video/Audio/Subtitle)、codec、language、
  title、is_default、is_forced、位深/声道/分辨率/帧率等探测列。
- `subtitle_file`：item_id、path、language、is_forced、format(ass/srt/pgs…)。
- `trickplay`：item_id、interval_s、tile_width、tile_height、manifest(jsonb)。
  〔定稿：进 v1 默认关〕

### 用户数据（写热点，独立表 ADR-0007）
- `user_data`：(user_id,item_id) 唯一——played、play_count、last_played_date、
  position、liked、resume 归并字段。阈值照 Emby（具体数值在 50 册展开，禁止
  只写"照 Emby"）。

### 房间（ADR 语义：私密、无弹幕、跟随最快者）
- `room`：id、owner_user_id、name、password_hash(可空)、status(open/banned)、
  created_at、last_active_at。
- `room_member`：room_id、user_id、role(owner/member)、joined_at。
- **评论不入库**：500 条环形缓冲在进程内存（重启清空、房间销毁清空），
  管理面板仅见计数。〔定稿：不长期保存〕
- 会话态（连接、播放头）全内存，PG 只存房间骨架。
- 同步语义：**锚点跟随最快成员**，无群体等待（定稿 2026-10-12 会话）。

### 弹幕
- `danmaku`：id、item_id、user_id、time_ms、mode(scroll/top/bottom)、color、
  text、status(active/deleted_by_admin/deleted_by_moderation)、source
  (user/imported)、created_at。
- `danmaku_report`：(danmaku_id,user_id) 唯一——多人举报 ≥3 进人工审核
  〔定稿〕；无条目级上限；20 条/分钟/用户。

### 通知 / 任务 / 系统
- `notification`：user_id、category、title、body、read、created_at；
  订阅开关存 `user.policy`。
- `task`：name、state、progress、last_run、cron/interval 定义（扫描、导出、
  审核巡检、更新检查…）；`ScheduledTasks` 端点映射它。
- `display_preference`：user_id、client、section、payload(jsonb)。
- `backup`：id、path、size、created_at、kind(manual/scheduled)——定时备份
  可配默认关；内容 = pg_dump + 配置快照〔定稿〕。
- `migration_job`：kind(emby-import)、status、report(jsonb)。

## 索引与规模预算 〔草案→90 册验收〕
- `item`：type+library_id、parent_id、(series_name,season_number,
  episode_number)、premiere_date、unbound 部分索引。
- 全文：`pg_trgm` GIN on name/sort_name/original_title；zhparser 可选。
- `user_data`：(item_id)、(user_id, played)、(user_id,last_played_date)。
- 百万 Item 预算：全表 < 3GB，载荷重建任务可整库重跑。

## Q-OPEN（待拷问）
（当前无——Q-OPEN-1 NFO 写回已定稿；Q-OPEN-2 已裁定 payload 不含 ImageTags，运行时拼。）
