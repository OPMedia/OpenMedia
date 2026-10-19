# Schema 风格：规范化真相源 + 可重建的物化载荷，拒绝 EAV

性能验收口径包含百万级 Item 全库查询 P95 < 100ms。决定：**规范化关系模型为唯一真相源**——`item`（过滤/排序/展示所需字段全部成列）、`item_genre`/`item_person`/`item_studio`/`item_tag`、`media_source`/`media_stream`、`user_data`（user_id+item_id 唯一索引，写热点单独一表）、`chapter`、`image`、`collection`/`playlist`、`display_preference`、`device`、`session`、`task`。

在此之上每个 Item 存一份 **物化载荷**（JSONB，静态 DTO 部分）：单一写入方（元数据管线在刮削/仲裁完成时写入一次），API 层只读；**UserData、ImageTags、ChildCount 等一切按用户或按状态易变的字段永不进载荷**，运行时实时拼；载荷带 `metadata_version`，且必须能随时从规范化表 + `raw_provider` 整库重建——可重建者非真相，只是缓存。

## Considered Options

- 照抄 Emby 的 EAV 宽表（BaseItems + ItemValues）：正是它百万级查询靠 `LIKE '%x%'` 爆掉的原因。
- 全 JSONB：GIN 能查但统计/排序/分面全靠表达式索引，脆且慢。
- 纯规范化无载荷：每页列表 ~10 个关联查询，正确但在百万级高并发下白烧 PG CPU。
- 不可重建的 DTO 快照：被否决——会造成规范化表与快照的双真相源漂移。

## Consequences

- ORM 选型倾向 sqlc/pgx（见 SPEC 工程约束），禁用会掩盖 SQL 的懒加载 ORM。
- 中文检索：`pg_trgm` GIN 内置扩展为硬依赖；zhparser 为可选增强，不进硬依赖。
- 载荷写入只属于元数据管线代码路径，其他模块禁止写。
