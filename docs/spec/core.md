# Serata 核心架构与工程约束（SPEC/core）

> 本册是全部 SPEC 分册的前提。术语以仓库根 [GLOSSARY.md](../../GLOSSARY.md) 为准；
> 架构级决策的来龙去脉见 [docs/adr/](../../adr/)(0001–0011)。
> 状态标记：〔定稿〕= 已拷问锁定；〔草案〕= 拷问进行中，禁止按草案实现。

## 1. 定位与非目标 〔定稿〕

Serata 是开源的、对标 Emby 的高性能**视频**媒体服务器：Go 后端、PostgreSQL 存储、
Vue 3 + TS 前端、LinWeb 播放核心。

**非目标（v1 明确不做，兼容层除外）**：音乐库、照片库、Live TV/DVR、DLNA、
Sync 离线、家长控制、媒体文件整理与重命名、.strm 文件生成、服务端 .strm 代理、
Quick Connect、SFU 语音中转、主题市场。

**行为基准**：客户端兼容锚定 Emby 4.8 stable OpenAPI（DTO 与 Id 形态照搬，
端点行为允许自有语义，见 ADR-0004）；Web UI 以新近版 Emby 网页端为对齐基准
（ADR-0011）。被砍端点返回**结构性空数据**，不返回 404。

## 2. 技术栈总表 〔定稿〕

| 层 | 选择 | 禁止 |
|---|---|---|
| 语言 | Go（≥1.24），单静态二进制 | cgo 依赖进入主路径 |
| 路由 | 标准库 `net/http`（1.22+ 增强路由） | gin/echo/chi/fiber |
| 数据库 | PostgreSQL（唯一），pgxpool | SQLite、ORM（GORM 等，明令禁用） |
| 数据访问 | sqlc（SQL in `.sql` → 生成 Go） | 运行时拼字符串 SQL |
| 迁移 | golang-migrate，SQL up/down，嵌入二进制，启动自动执行 | 首启建表代码 |
| 配置 | TOML 文件 + 环境变量覆盖（`SERATA_DB__HOST` 风格）；敏感项允许仅环境变量 | viper |
| 日志 | slog 结构化 JSON，分级详尽（请求级 trace、转码会话全程、扫描任务全程） | 打印式日志 |
| 指标 | 内置 `/metrics`（Prometheus） | — |
| HTTP 层外置 | TLS 可内置（证书文件路径 + 密码配置）；进阶防护交 nginx，文档指路 | — |
| 转码 | 官方静态 ffmpeg 打进镜像，版本锁死（版本号写入 SPEC 50 册） | 系统 ffmpeg |
| GPU | 单镜像，启动时探测 NVENC/QSV/VAAPI，逐级降级软转 | 多镜像变体 |
| 插件 | goja，唯一机制，完整宿主 API，信任安装（ADR-0003 修正版） | 权限申报/白名单 |
| LLM | 两套独立配置（仲裁/审核），均 OpenAI 兼容 endpoint+key，可指向本地 ollama/vLLM | 硬编码厂商 SDK |
| 部署 | Docker 主战场 + 公网支持；后端托管 web 静态产物（同源） | — |
| CI/发布 | GitHub Actions：Go test → web build → amd64/arm64 镜像；semver（v0.x 可破坏性变更） | — |

## 3. 进程与包布局 〔定稿〕

单进程。包布局（`server/` 下，Go standard layout 精神、internal 收紧）：

```
server/
  cmd/serata/            # main：装配、配置加载、优雅关停
  internal/
    httpapi/             # 全部 REST 端点；按域分文件（items.go users.go ...）
    auth/                # AuthenticateByName、token、API key、邮箱安全码
    library/             # Library、扫描编排、文件生命周期（Unbound/Rebind）
    metadata/            # Provider 管线、Arbitration、Canonical、物化载荷写入
    playback/            # PlaybackInfo 决策、流端点、会话
    transcode/           # ffmpeg 进程管理、HLS、档位矩阵、GPU 探测
    danmaku/             # 存取、审核任务、导出
    room/                # 房间、Anchor 权威、WebSocket、评论区
    notify/              # 站内通知中心 + 邮件渠道
    plugin/              # goja 宿主、事件总线出口
    store/               # pgxpool、事务助手、sqlc 生成代码
    migrate/             # golang-migrate 装配
    config/              # TOML + env
    obs/                 # slog 装配、/metrics、健康探针
    eventbus/            # 进程内 pubsub（通知/插件钩子/看板数据源共用）
  migrations/            # 0001_init.up.sql ...
```

**分层规则**：`httpapi` 只做协议转换，业务在域包；域包不得互相 import，
跨域协作经 `eventbus` 或显式接口（定义在消费方）；`store` 之外禁止直连 DB。
〔草案〕事件总线的背压与有序性语义在 plugins.md 定稿。

## 4. 时间、ID 与错误规范 〔定稿〕

- **时间**：全库 UTC（`timestamptz`），API 层照 Emby 本地化输出。
- **ID**：Item 等主键 `bigint`，API 输出 Emby 数字字符串形态（ADR-0004 补记）。
- **错误**：API 错误响应形状照 Emby（抓包对照定稿于 20 册）；内部错误一律
  `fmt.Errorf("...: %w")` 包装，禁止吞错。
- **配置默认值**：全部集中一个 `config/defaults.go`，SPEC 引用它，禁止散落魔法数。

## 5. 验收原则 〔定稿〕

每册 SPEC 的"验收"一节必须是可执行命令或可对照契约（抓包记录、基准脚本、
Testcontainers 集成测试），禁止散文式承诺。三条性能靶的测法归 90 册。

**"照 Emby"条款的铁律**：凡 SPEC 中声称"照 Emby"的行为，必须**完完整整展开**
——Emby 具体怎么做、默认值是什么、算法/规则是什么，并附出处（3.5.2 源码的
文件与行号，或 4.8 真实客户端抓包记录）。只写"按照 Emby 来"视同未定义。
