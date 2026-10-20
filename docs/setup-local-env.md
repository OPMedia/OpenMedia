# 本地开发环境配置（Windows）

> 本文记录本机已完成的 OpenMedia 开发环境搭建与验收结果（2026-10-20）。
> 目标：一条命令起后端，一条命令起前端，PG 走**默认端口 5432 / 密码 `123456`**。

## 1. 已安装组件

| 组件 | 版本 | 安装方式 | 说明 |
| --- | --- | --- | --- |
| .NET SDK | 10.0.402 | `winget install Microsoft.DotNet.SDK.10` | 全项目 `TargetFramework=net10.0` |
| PostgreSQL | 18.6 x64 | `winget install PostgreSQL.PostgreSQL.18 --override "--mode unattended --superpassword 123456 --serverport 5432 --locale Chinese"` | 服务名 `postgresql-x64-18`，随开机自启 |
| Node.js | 24.20.0 | 已存在 | 满足 `web/` 的 `^22.18.0 \|\| >=24.12.0` |
| pnpm | 12.14.0 | corepack 托管 | `web/` 依赖安装；shim 还没 enable 到 PATH，用 `corepack pnpm ...` 调用，或执行一次 `corepack enable` 后直接敲 `pnpm` |
| ffmpeg / ffprobe | 9.0.2 (Gyan full) | `winget install Gyan.FFmpeg` | 转码 / HLS / BIF / MediaInfo 探测 |

PATH 实况（2026-10-20 核对）：

- Machine 级含 `C:\Program Files\dotnet\`（.NET SDK ✓）
- User 级含 ffmpeg 的
  `C:\Users\li\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin`
- **PostgreSQL 18 的 `C:\Program Files\PostgreSQL\18\bin\` 尚未加入 PATH**——
  `psql` 得用完整路径调用，或自行把它补进 PATH

**新开的终端才会刷新 PATH**；旧终端手动补一句：

```powershell
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
```

## 2. PostgreSQL 连接约定

- 主机 `localhost`，端口 `5432`，用户 `postgres`，密码 `123456`，数据库 `openmedia`。
- 连接串（代码默认值见 `src/Emby.Server.PgData/BasePgRepository.cs`）：

  ```
  Host=localhost;Port=5432;Database=openmedia;Username=postgres;Password=123456
  ```

- 数据库 `openmedia` 已按 `emby_pgsql_dump_schema_optimized.sql` 初始化：
  79 张表（含 `media_items` / `user_datas` / `activity_log` 等分区表）+ 2 个物化视图。
  若需重建：

  ```powershell
  $env:PGPASSWORD = '123456'
  psql -h localhost -p 5432 -U postgres -c "DROP DATABASE IF EXISTS openmedia; CREATE DATABASE openmedia ENCODING 'UTF8';"
  psql -h localhost -p 5432 -U postgres -d openmedia -f emby_pgsql_dump_schema_optimized.sql
  ```

  > 服务启动时 `PgSchemaInitializer` 会做幂等补齐（`to_regclass` 门卫 + 补充对齐 DDL），
  > 因此空库也能直接起服务；上面的全量脚本只是让库一次到位。

### 环境变量

| 变量 | 用途 | 本机取值 |
| --- | --- | --- |
| `OPENMEDIA_PGCONN` | 覆盖后端连接串（`ApplicationHost.GetPostgresConnectionString` 优先读它） | `Host=localhost;Port=5432;Database=openmedia;Username=postgres;Password=123456` |
| `OPENMEDIA_TEST_PGCONN` | 覆盖集成测试连接串（`MediaBrowser.Tests/PgData/PgTestBase.cs`） | 同上 |

两个变量都已在**用户级**写入注册表，新开的终端自动继承；值本身与代码默认值一致，属于双保险。

> 压测库请用独立库名（如 `openmedia_scale` / `openmedia_perf`），
> 严禁往共享条目库写合成数据（`docs/phase2-design.md` §100）。

## 3. 起后端

```powershell
cd D:\Dev\OpenMedia
dotnet run --project MediaBrowser.Server/MediaBrowser.Server.csproj -c Debug
```

- Kestrel 监听 `http://localhost:8096`（HTTP）与 `https://localhost:8920`（HTTPS）。
- 配置目录在**输出目录下**（不是仓库根）：
  `MediaBrowser.Server/bin/Debug/net10.0/ProgramData-Server/`（`config` / `logs` / `data` …），已被 `.gitignore` 覆盖。
  首次启动会生成它；浏览器打开 `http://localhost:8096/web/index.html` 走启动向导
  （`/web` 会 302 到该默认页；这里挂的是仓库自带的旧 dashboard `MediaBrowser.WebDashboard/dashboard-ui/`，
  新前端 Serata 走 Vite dev server，见下节）。
- 冒烟：`GET http://localhost:8096/emby/System/Ping` 应返回 200。

## 4. 起前端（Serata）

```powershell
cd D:\Dev\OpenMedia\web
corepack pnpm install   # 已执行过；pnpm 在 Windows 用 junction，搬目录后需 --force
corepack pnpm dev       # http://localhost:5173
```

> 下列命令里的 `pnpm` 等价于 `corepack pnpm`（本机 corepack shim 未 enable 到 PATH）。

- 默认走 MSW 假数据，账号 `demo` / 密码 `demo`，无需后端即可看界面。
- 接真实后端：`VITE_ENABLE_MOCK=false`，并用 `VITE_API_PREFIX` 指向后端（默认 `/emby`）。
  后端未内置前端代理，本地联调建议 `VITE_API_PREFIX=http://localhost:8096/emby`。
- 用 `localhost:5173` 访问，**不要用 `127.0.0.1`**（dev server 只监听 IPv6，实测 127.0.0.1 连不上）。
- 校验：`pnpm type-check`、`pnpm build-only` 均已通过（2026-10-20 实测）。

## 5. 测试

```powershell
cd D:\Dev\OpenMedia
dotnet test MediaBrowser.Tests/MediaBrowser.Tests.csproj -c Debug
```

- 28 个用例全部通过、0 跳过（2026-10-20 实测，耗时 24s）。
  其中 BatchWrite 集成、where 分支差分（7 个 `Diff_*`）、查询门（9 个 `A1`–`A9`）等用例依赖 PG 可达；
  PG 不可达时这些用例报 Inconclusive 而非假绿。
- 集成用例在独立 schema（`pgtest_xxx`）中建表，结束 `DROP SCHEMA CASCADE`，不污染共享库。

## 6. 测试媒体（可选）

`tests/setup-media.ps1`、`tests/setup-tv.ps1` 默认从 `D:\OpenMedia\ffmpeg.exe` 取 ffmpeg，
本机实际在 winget 的 `Gyan.FFmpeg` 包目录（`...\ffmpeg-9.0.2-full_build\bin`），
跑的时候显式传参：

```powershell
$ff = (Get-Command ffmpeg.exe).Source
powershell -File tests\setup-media.ps1 -Ffmpeg $ff
```

`tests/media/` 下已有 Movie1/Movie2 两个真实 H.264+AAC 的 mkv，可直接建库扫描。

## 7. 已知差异与注意点

- `docs/perf-baseline.md` 记录的库在 15432：那是 docker-compose 时代的端口，
  本机已统一到 5432；`docker-compose.yml` 相应只保留 `5432:5432`。
- `tests/perf/` 下脚本的默认 `-PgPort` 已于 2026-10-20 统一为 `5432`；
  仍留历史值 `15432` 的只剩 `tests/cleanup-byname-duplicates.ps1`，跑它时显式加 `-PgPort 5432`。
- ✅ **PG `max_connections` 已从 100 提到 250**（2026-10-20，方案 A）。
  改的是 `C:\Program Files\PostgreSQL\18\data\postgresql.conf` 第 65 行，
  改完需**重启服务**才生效（PG 非管理员权限无法重启，需 UAC 提权）：
  ```powershell
  Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-Command','Restart-Service postgresql-x64-18 -Force'
  ```
  备份在同目录 `postgresql.conf.bak-20261020`。
  效果：应用池上限 100 与服务端 250 之间有 150 个连接的余量，
  并发认证负载不再出现 `53300 too many clients`（实测 L4 档位 rps +49%、p95 -32%），
  应用启动时的「连接预算不足」告警也不再出现。
  若以后要恢复：`max_connections = 100` + 再次重启。
- `pg_stat_statements` **已启用**（`shared_preload_libraries = pg_stat_statements`、
  `track=all`），可直接跑 `tests/perf/pg-stat-statements-top20.ps1`。
