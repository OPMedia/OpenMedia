# OpenMedia 测试基建（task-11 第一步产物）

> 目标：对 OpenMedia（Emby 3.5.3 → .NET 10 + Kestrel + PostgreSQL）做系统测试。
> 基址 `http://localhost:8096`，API 前缀 `/emby`。所有脚本 PowerShell 5.1 兼容、幂等可重复。

## 目录结构

```
tests/
├── parse-openapi.ps1      # 解析 openapi.json（4.1.1.0）→ test-plan.json / test-plan.md（可完整重建）
├── test-plan.json         # 414 spec 操作 + 3 code-only，P0/P1/P2 分级 + 404/400 预期清单（机器可读）
├── test-plan.md           # 同上，人类可读（分域统计 + 全量操作表）
├── run-tests.ps1          # API 矩阵测试：P0/P1 逐操作请求 → results/api-matrix.md + api-results-*.json
├── scenarios.ps1          # 功能场景：向导/认证/媒体库/WebSocket/静态+Range → results/scenarios.md
├── contract-check.ps1     # JSON 契约抽查：Id 格式/PascalCase/Type 枚举/无 null → results/contract.md
├── setup-media.ps1        # 测试媒体生成（幂等）：media\Movie1、Movie2（mkv+nfo+poster）
├── setup-tv.ps1           # 剧集素材生成（幂等）：media-tv\Show Alpha（2 季 4 集）
├── setup-music.ps1        # 音乐素材生成（幂等）：media-music\（3 艺人 4 专辑 9 曲目）
├── setup-libraries.ps1    # 把 TV/音乐目录注册为媒体库并扫描（矩阵 ⑤ 类 fixture 前提）
├── setup-livetv.ps1       # LiveTv fixture：XmlTv EPG + M3U 频道（2 频道 2 节目）
├── setup-images.ps1       # 按名实体/用户 Primary 图片（按端点自动取实体名）
├── media/                 # 测试媒体（Movie1: 2020 + poster.jpg；Movie2: 2021）
├── lib/common.ps1         # 共享库：HttpClient 封装/认证/PascalCase 校验
├── selftest/              # 引擎自检（mock 服务器 8196 端口，已验证 20/20 PASS）
│   ├── mock-server.ps1
│   └── selftest.ps1
└── results/               # 运行输出（服务器启动后生成）
```

## 完整回归流程（2026-10-20 固化的可复现顺序）

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
cd D:\Dev\OpenMedia\tests

# 0. fixture 素材（幂等，可重复跑）
.\setup-media.ps1        # 2 部电影
.\setup-tv.ps1           # 1 部剧 2 季 4 集
.\setup-music.ps1        # 3 艺人 4 专辑 9 曲目
# （服务器启动后）注册媒体库并扫描
.\setup-libraries.ps1
.\setup-livetv.ps1       # LiveTv 频道/节目单
.\setup-images.ps1       # 按名实体/用户图片

# 1. 功能场景（先跑：完成启动向导 + 建媒体库，为矩阵提供真实 Id 上下文）
.\scenarios.ps1

# 2. API 矩阵（414 操作 P0/P1 执行、P2 跳过）
.\run-tests.ps1

# 3. JSON 契约抽查
.\contract-check.ps1
```

> **为什么 fixture 必须先跑**：矩阵 ⑤ 类 fixture（`/Artists`、`/Albums`、`/Shows`、`/LiveTv/*`）的
> `{Name}` / `{Id}` 占位符从**库里实时取第一个实体**。库里没有对应实体时占位符不替换，
> 请求字面量路径 → 404 → 误判 FAIL。2026-10-20 全量回归实测：补齐 fixture 前 22 个 FAIL，
> 补齐后 0 FAIL。

三个脚本对未启动的服务器都会**快速失败**（预检 GET /System/Info/Public 不可达即退出），不会空转。

## 分级规则

| 级别 | 规则 | 判定 |
|------|------|------|
| P0 | 客户端关键路径（/System/Info/Public、/System/Ping、/Users、/Users/Public、/Items、/Users/{UserId}/Items、/Users/{UserId}/Views、/Items/{Id}/Images*、POST /Items/{Id}/PlaybackInfo、/Sessions、/web/index.html） | 必须 200 且结构正确（含 Id 32hex、PascalCase、关键字段断言） |
| P1 | 常规查询（GET/OPTIONS + 白名单 POST：AuthenticateByName、LiveTv/Programs） | 200/204/405 PASS；404 → 核对 B6 暂缓清单（命中→SKIP-EXPECTED，未命中→FAIL）；无真实条目时 404→SKIP-NOFIXTURE |
| P2 | DELETE 全部 + POST/PUT 写操作（151 个） | 一律跳过（矩阵中列出清单） |

404 预期清单（SKIP-EXPECTED 判定）：B6 插件端点 25（user_usage_stats 19 / Reports 4 / ThumbnailSet 1 / SMTP 1）+
跳过清单 29（Sync 21 / Connect 4 / swagger+openapi 4）+ B5 index.bif 占位 1 + 合成参数 400 预期 11 +
环境限制（无游戏媒体 → GameGenre 实体不存在）3。**全部规则在 `parse-openapi.ps1` 的
`$expected404Rules` / `$expected400Rules` 表里**——手工增补的期望也已脚本化，
`parse-openapi.ps1` 重跑可完整重建 `test-plan.json`（2026-10-20 修复：此前 31 条
手工增补只存在于入库的 json 里，脚本重建会整批丢失，导致矩阵从 0 FAIL 变 30 FAIL）。

## 认证约定

- `POST /Users/AuthenticateByName`，body `{"Username":"admin","Pw":"admin"}`，头 `X-Emby-Authorization: MediaBrowser Client="OpenMediaTest", Device="TestRunner", DeviceId="openmedia-test-01", Version="4.1.1.0"`
- 401 时自动回退空密码（`POST /Startup/User` 仅设置用户名，初始用户无密码——源码 StartupWizardService.cs 证实）
- 后续请求带 `X-Emby-Token` 头

## 已知环境坑（脚本已规避）

1. 本机 PowerShell 为 5.1：脚本必须 UTF-8 **带 BOM**（否则中文注释变 GBK 乱码破坏语法）、无 `??`/三元/`&&`。
2. `New-Object System.Collections.Generic.List[object]` 产生的对象无法被 `@()` 转换（ArgumentException）——一律用 `[T]::new()`。
3. PS 5.1 把 `$null` 转空串传给 .NET string 参数（HttpListener AcceptWebSocketAsync 会拒绝）。
4. HttpClient 用 `System.Net.Http.HttpClient`（PS 5.1 无 `-SkipHttpErrorCheck`）。
5. PS 5.1 的 `ConvertFrom-Json` 把**单元素 JSON 数组**解包成标量——判数组要看 HTTP 原文
   （`$body.TrimStart().StartsWith('[')`），不能用 `$ju -is [Array]`（`contract-check.ps1` 误报根因）。
6. `-like '[*'` 里的 `[` 被当字符集起始符 → `WildcardPatternException`；判断前缀用 `StartsWith()`。
7. ffmpeg 默认路径不能写死（`D:\OpenMedia\ffmpeg.exe` 本机不存在）——`setup-*.ps1` 改为
   `-Ffmpeg` 显式传入 > PATH 解析 > 历史路径回退。
8. `scenarios.ps1` 的扫描轮询不能只等"条目数 ≥ 2"——首轮扫描时条目先建、元数据（nfo year / poster）
   后落库，会读到中间态产出假 FAIL；轮询需同时校验 `ProductionYear` 已有值。

## 自检（已完成，可重跑验证引擎）

```powershell
# 后台起 mock（端口 8196），再跑 selftest，期望 20/20 PASS
powershell -ExecutionPolicy Bypass -File .\selftest\mock-server.ps1   # 后台
powershell -ExecutionPolicy Bypass -File .\selftest\selftest.ps1
```
