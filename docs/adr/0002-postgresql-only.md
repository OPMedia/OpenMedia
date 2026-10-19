# PostgreSQL 是唯一存储，不做 SQLite 起步方案

媒体服务器传统上以 SQLite 起步（Emby/Jellyfin 如此），但本项目直接锚定 PostgreSQL：目标验收口径包含**百万级 Item 的全库查询 P95 < 100ms** 与公网多用户并发写（进度、会话、弹幕、房间），PG 的 MVCC、GIN/tsvector、连接池生态直接满足，仓储层因此不需要为第二套数据库做抽象。

## Considered Options

- SQLite 起步 + 抽象层留 PG 后门：零运维优点真实，但两套方言的抽象层会污染所有仓储代码，且写并发（多用户同时报进度/发弹幕）是 SQLite 的弱项。

## Consequences

- Docker 镜像交付必须内置或编排 PG（单容器 or compose），部署复杂度高于单文件方案——用交付文档化解。
- 迁移工具在 SPEC 工程约束一节固定（唯一、可重复执行）。
