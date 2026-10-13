# OpenMedia（Emby 3.5.3）→ .NET 10 + PostgreSQL 升级方案

> 版本：v2（决策已确认，开始执行）
> 基线：master @ `1d7c2ab4bf`（Emby 3.5.3 开源部分原貌）
> 目标：.NET 10 / SDK 风格工程 / **Kestrel** / PostgreSQL（按 `emby_pgsql_dump_schema_optimized.sql` 初始化）/ 移除 Emby 同步与云连接 / 闭源 DLL 反编译恢复源码
> 执行纪律：**一个功能一个 commit**，每个 commit 必须保持 `dotnet build` 通过

## 0. 已确认决策（2026-10-13）

| 决策点 | 结论 |
|--------|------|
| D1 闭源恢复方式 | **反编译**（ilspycmd，已验证） |
| D2 sync 三表 | **照建留空**（初始化器执行全量 DDL，不做语句过滤；sync 表保留但无代码使用） |
| D3 schema 差异 | **初始化器追加补充 DDL**（§5.2 的 5 条，SQL 文件不动） |
| D4 HTTP 栈 | **本期直接换 Kestrel**（SocketHttpListener 退役，新增 commit C13） |
| D5 入口 | **合并为单一跨平台 MediaBrowser.Server** |
| D6 历史分支 | `backup-migration-20261013-210711` 保留作参考 |
| PG 连接 | 本机 PostgreSQL，密码 `123456`（默认连接串见 §5.1） |

---

## 1. 现状盘点

### 1.1 代码规模

| 分类 | 数量 |
|------|------|
| 解决方案项目 | 21 个（旧式 csproj，net47） |
| 仓库内 C# 源码 | ~1,090 文件 / ~21 万行 |
| 其中最大单文件 | `SqliteItemRepository.cs`（6,443 行，查询引擎） |
| Web UI（dashboard-ui） | 73 个页面，约 15 MB（保留原样） |
| NuGet 依赖 | packages.config 旧格式，含 38+ 个 System.* BCL shim 包 |

### 1.2 闭源部分（仓库里只有 DLL，没有源码）

| DLL | 大小 | 作用 | 处置（见 §3） |
|-----|------|------|---------------|
| `MediaBrowser.Controller.dll` | 6.5 MB | 核心控制器：实体、库管理、持久化接口 | **反编译恢复** |
| `MediaBrowser.Model.dll` | 390 KB | DTO/模型层 | **反编译恢复** |
| `MediaBrowser.Common.dll` | 44 KB | 公共接口 | **反编译恢复** |
| `Emby.Naming.dll` | — | 媒体文件名解析 | **反编译恢复** |
| `Emby.XmlTv.dll` | — | XMLTV 电子节目单解析 | **反编译恢复** |
| `Emby.Server.MediaEncoding.dll` | 258 KB | FFmpeg/FFprobe 封装（转码参数构建） | **反编译恢复** |
| `Emby.Server.Sync.dll` | 163 KB | 设备同步（Sync Jobs/离线下载） | **删除**（§4） |
| `Emby.Server.Connect.dll` | 65 KB | Emby 云账号连接 | **删除**（§4） |
| `TagLib.Portable.dll` | — | 音频元数据 | **NuGet 替代**：TagLibSharp |

`src/` 目录下的 `MediaBrowser.Controller`、`MediaBrowser.Model`、`MediaBrowser.Common`、`Emby.Naming`、`Emby.XmlTv` 目前是空壳（只有 bin/obj），实际编译时引用的是上面的闭源 NuGet DLL。

### 1.3 环境已验证

- .NET SDK **10.0.401** 已安装
- **ilspycmd 11.1.0** 已安装，已实测反编译上述 5+1 个 DLL 全部成功（合计 ~44,000 行可读 C#）
- 存在历史尝试分支 `backup-migration-20261013-210711`（已完成一轮类似迁移，`dotnet build` 0 错误）。本次**不直接沿用其提交**，按本方案重新分阶段执行；其反编译产物与 PG 仓储实现作为参考解法。

---

## 2. 目标架构

```
MediaBrowser.Server (单一跨平台入口, net10.0)
├── Emby.Server.Implementations      核心实现（含 PG 数据层）
├── MediaBrowser.Api                 REST/WebSocket API
├── MediaBrowser.WebDashboard        内嵌 Web UI（原样保留）
├── MediaBrowser.Providers / LocalMetadata / XbmcMetadata / Photos
├── Emby.Dlna / Emby.Notifications / Emby.Drawing(+Skia/Magick)
├── src/MediaBrowser.Controller      ← 反编译恢复源码
├── src/MediaBrowser.Model           ← 反编译恢复源码
├── src/MediaBrowser.Common          ← 反编译恢复源码
├── src/Emby.Naming / Emby.XmlTv     ← 反编译恢复源码
├── src/Emby.Server.MediaEncoding    ← 反编译恢复源码
├── **ASP.NET Core Kestrel**         ← 替代 SocketHttpListener（D4）
└── PostgreSQL (Npgsql)              ← 替代全部 SQLite
```

- 目标框架：全部 `net10.0`；Windows 专属功能（服务安装/托盘）运行时 `OperatingSystem.IsWindows()` 判断（D5：单一入口）
- 包管理：`packages.config` → `PackageReference`，版本集中在 `Directory.Build.props`
- 解决方案：`MediaBrowser.sln` 保留并更新（不用 slnx，兼容旧工具链）
- HTTP：`WebApplication`（Kestrel）承载，自定义中间件做 ServiceStack 风格路由分发（保持 61 个 API 服务类不动），WebSocket 用 ASP.NET Core 原生实现，dashboard-ui 用静态文件中间件

---

## 3. 闭源代码处置：逆向恢复（已验证可行）

**结论：反编译，不重写。** 理由：

1. **已实测**：ilspycmd 11.1 对 6 个 DLL 全部反编译成功，输出为可编译的 C#（含泛型/迭代器/async），合计约 44,000 行；
2. **API 精确兼容**：仓库内 21 个开源项目全部针对这些 DLL 的公开 API 编写，反编译源码 = 零行为漂移，编译期即可暴露所有不一致；
3. **替代方案对比**：
   - 从 Jellyfin 移植：Jellyfin 虽同源（3.5.2 fork），但已分化 7 年、API 大改，等于重写，否决；
   - 重写 MediaEncoding 为直接调 FFmpeg：`Emby.Server.MediaEncoding` 本身就是 FFmpeg 进程封装（含转码参数构建、探测解析），反编译即得全部逻辑，重写无收益，否决；
   - dnSpy 手工修：ilspycmd 输出质量足够，无需人工修补（历史分支已验证整库编译通过）。

**执行方式**（阶段 P0，一个 commit）：
```
ilspycmd -p -o src/MediaBrowser.Controller  packages/MediaBrowser.Server.Core.3.5.0/lib/netstandard2.0/MediaBrowser.Controller.dll
ilspycmd -p -o src/MediaBrowser.Model       packages/MediaBrowser.Common.3.5.0/lib/netstandard2.0/MediaBrowser.Model.dll
ilspycmd -p -o src/MediaBrowser.Common      packages/MediaBrowser.Common.3.5.0/lib/netstandard2.0/MediaBrowser.Common.dll
ilspycmd -p -o src/Emby.Naming              packages/MediaBrowser.Naming.1.1.7-beta/lib/netstandard2.0/Emby.Naming.dll
ilspycmd -p -o src/Emby.XmlTv               packages/Emby.XmlTv.1.0.15/lib/portable-net45+netstandard2.0+win8/Emby.XmlTv.dll
ilspycmd -p -o src/Emby.Server.MediaEncoding ThirdParty/emby/Emby.Server.MediaEncoding.dll
```
- 每个目录生成 SDK 风格 csproj（`GenerateAssemblyInfo=false`，沿用 `SharedVersion.cs`）
- 删除 `packages/` 目录与所有对闭源 NuGet 的引用，改为 `ProjectReference`
- `TagLib.Portable.dll` → NuGet `TagLibSharp 2.3.0`（同源维护版，API 兼容）

---

## 4. Emby 同步 / 云连接移除清单

用户已确认：同步功能不要了。移除范围：

### 4.1 二进制与源码

| 项 | 处置 |
|----|------|
| `ThirdParty/emby/Emby.Server.Sync.dll` | 删除 |
| `ThirdParty/emby/Emby.Server.Connect.dll` | 删除 |
| `src/MediaBrowser.Controller/MediaBrowser.Controller.Sync/`（6 文件：ISyncProvider 等） | 删除 |
| `src/MediaBrowser.Controller/MediaBrowser.Controller.Connect/`（IConnectManager 等） | 删除 |
| `src/MediaBrowser.Model/MediaBrowser.Model.Sync/`（SyncJob 等 5 文件） | 删除 |
| `src/MediaBrowser.Model/MediaBrowser.Model.Connect/`（ConnectUser 等 ~10 文件） | 删除 |

### 4.2 引用清理（编译期驱动）

- 全库 grep `Sync|Connect` 引用点：`UserDto`/`SystemInfo` 等模型若含 Connect 字段（如 `ConnectLink`、`HasSyncJob`）→ 删除字段并同步 DTO 映射
- `ApplicationHost` 程序集扫描列表中移除两个 DLL 名
- `MediaBrowser.Api` 无直接 Sync 引用（已验证），主要是模型字段级清理

### 4.3 Web UI 清理

删除页面与脚本（保留其余 66 个页面）：
`mysync.html`、`mysyncjob.html`、`mysyncsettings.html`、`syncactivity.html`、`syncsettings.html`、`camerauploadsettings.html`、`connectlogin.html` + 对应 `scripts/*.js` + `components/categorysyncbuttons.js`、`components/guestinviter/connectlink.*` + 导航/路由注册点。

### 4.4 数据库

`emby_pgsql_dump_schema_optimized.sql` 中的 `sync_targets` / `sync_jobs` / `sync_job_items` 三张表：**照建留空**（D2 已确认：初始化器执行全量 DDL，sync 表保留但无任何代码使用）。

---

## 5. PostgreSQL 迁移设计

### 5.1 初始化策略

- `emby_pgsql_dump_schema_optimized.sql` **原样嵌入** `Emby.Server.Implementations` 作为 EmbeddedResource（单一事实来源，文件不再复制到运行目录）
- `PgSchemaInitializer` 启动时执行：
  1. `SELECT to_regclass('public.media_items')` 为 NULL 才执行全量 DDL（幂等门卫，避免分区表 `CREATE TABLE ... PARTITION OF` 无 IF NOT EXISTS 导致的重复执行失败）
  2. **全量执行，不做语句过滤**（D2：sync 三表照建留空，无代码使用）
  3. 执行**补充对齐 DDL**（§5.2，全部 `IF NOT EXISTS` 幂等）
- 连接串：`system.xml` 服务配置新增 `<Postgres>` 节点 + 环境变量 `OPENMEDIA_PGCONN` 覆盖 + 默认值 `Host=localhost;Port=5432;Database=openmedia;Username=postgres;Password=123456`
- 驱动：**Npgsql 10.0.3**（已在历史分支验证与 net10 兼容）

### 5.2 Schema 对齐分析（关键！）

对照旧 SQLite 实际表结构（`SqliteItemRepository.cs` L130-149）逐表核对后，发现 4 处必须补充/修正，否则功能不完整：

| # | 问题 | 影响 | 补充 DDL |
|---|------|------|----------|
| 1 | `media_items` 缺 **`data`** 列（旧表存整个 BaseItem 序列化 JSON，读取时反序列化还原完整对象；扁平列只用于查询过滤） | 无法还原媒体项 | `ALTER TABLE media_items ADD COLUMN IF NOT EXISTS data JSONB` |
| 2 | `media_items` 缺 **`type_name`** 列（旧 `type` TEXT 存 .NET 完整类型名，反序列化需要；现有 `type INTEGER` 是分区键，语义不同） | 无法实例化正确实体类型 | `ALTER TABLE media_items ADD COLUMN IF NOT EXISTS type_name TEXT` |
| 3 | `media_items.guid` **无索引**（旧表 guid 是主键；所有按 GUID 的查找——即 Emby 的每一次 item 定位——都会顺序扫描） | 全库性能崩塌 | `CREATE UNIQUE INDEX IF NOT EXISTS idx_media_items_guid ON media_items (guid, type)`（分区表唯一索引必须含分区键，guid 为最左前缀可服务 GUID 查找） |
| 4 | `item_extradata` 主键 `(item_id, extradata_type_id)` 只允许每类型一个值，但 Genres/Studios/Tags/Artists 都是**多值**（旧 ItemValues 表为 `(ItemId, Type, Value, CleanValue)`） | 一部电影存不下 2 个以上类型标签 | 重建为 `PRIMARY KEY (item_id, extradata_type_id, value)`（初始化器内建表时直接用修正后的定义；对已存在的旧库执行迁移语句） |

另有一处映射决策：旧 `userdisplaypreferences(id, userId, client, data)` 在 PG schema 中无对应表（`user_settings` 主键形状不匹配）。补充：

```sql
CREATE TABLE IF NOT EXISTS display_preferences (
    user_id  INTEGER NOT NULL,           -- local_users.id
    item_id  UUID    NOT NULL,           -- 旧 id GUID
    client   TEXT    NOT NULL,
    data     JSONB   NOT NULL,
    PRIMARY KEY (item_id, user_id, client)
);
```

> 以上 5 条补充 DDL 全部集中在 `PgSchemaInitializer` 的"对齐迁移"段，幂等可重复执行。**你的 SQL 文件一字不改**。若你希望直接改进 SQL 文件本身，也可以，评审时告诉我。

### 5.3 GUID ↔ 整数 ID 策略

Schema 用 `media_items.id BIGINT IDENTITY` 代理键 + `guid UUID` 业务键；C# 域模型（BaseItem.Id、User.Id）全是 Guid。策略：**域模型不动，仓储层翻译**。

- 写入：`INSERT ... RETURNING id`，同时写 `guid`；内存维护 `ConcurrentDictionary<Guid, long>` 缓存
- 读取：`WHERE guid = @g`（走 §5.2-3 的唯一索引）
- `parent_id/series_id/album_id`：写入时先查/插父项拿 id（批量保存时按拓扑序处理）
- `user_id`：`local_users.guid → id` 查询（有唯一索引）
- `type INTEGER` 映射：静态双向字典，0-10 按 schema 注释固定（0=CollectionFolder, 1=Folder, 2=Movie, 3=Series, 4=Season, 5=Episode, 6=Audio, 7=MusicAlbum, 8=Book, 9=Photo, 10=MusicVideo），11+ 顺序分配给 Person/Genre/Studio/BoxSet/Playlist/LiveTvProgram/…（其余类型落 `media_items_other DEFAULT` 分区，行为正确）

### 5.4 仓储重写清单（替代全部 Sqlite*Repository）

| 旧文件（行数） | 新文件 | commit 拆分 |
|----------------|--------|-------------|
| BaseSqliteRepository (424) + ManagedConnection + SqliteExtensions | `BasePgRepository`（连接池/事务/日志） | 1 个 commit |
| SqliteUserRepository (235) | `PgUserRepository`（local_users JSONB） | 1 个 commit |
| Security/AuthenticationRepository (430) | `PgAuthenticationRepository`（devices/tokens） | 1 个 commit |
| SqliteUserDataRepository (418) | `PgUserDataRepository`（user_datas + user_data_keys） | 1 个 commit |
| SqliteDisplayPreferencesRepository (253) | `PgDisplayPreferencesRepository`（display_preferences） | 1 个 commit |
| Activity/ActivityRepository | `PgActivityRepository`（activity_log 分区表） | 1 个 commit |
| **SqliteItemRepository (6,443)** | **PgItemRepository** | **6 个 commit**（见下） |

`PgItemRepository` 拆分（对应旧接口 `IItemRepository` 的方法组）：
1. 基础 CRUD：SaveItem/SaveItems/DeleteItem/GetItem（data JSONB 水合 + guid↔id）
2. chapters / media_streams 读写
3. item_people（含 Person 项 upsert）+ item_links
4. ancestor_ids 闭包表维护 + UpdateInheritedValues
5. 查询引擎：InternalItemsQuery → 动态 SQL（WHERE/ORDER/分页/IncludeTypes→type 码过滤）——最大块
6. 聚合查询：Genres/Studios/Artists/PeopleNames（item_extradata 分组统计）

最后 1 个 commit：删除全部 Sqlite* 文件与 SQLitePCL 依赖。

### 5.5 并发与事务

- Npgsql 自带连接池（`Max Pool Size` 默认 100），替代旧 `ManagedConnection` 手工锁
- 旧代码的 `ReaderWriterLockSlim` 写锁 → PG 行级锁 + 事务（`SELECT ... FOR UPDATE` 仅在需要处）
- `user_datas` 高频更新（播放进度）走 `INSERT ... ON CONFLICT (user_data_key_id, user_id) DO UPDATE`（UPSERT）

---

## 6. 组件替换清单

| 旧组件 | 版本 | 新组件 | 版本（已验证可编译） | 破坏性处理 |
|--------|------|--------|---------------------|-----------|
| .NET Framework | 4.7 | .NET | 10.0 | 全量 SDK csproj 重写 |
| SQLitePCL.pretty + SQLitePCLRaw | 1.1.0/1.1.11 | **Npgsql** | 10.0.3 | §5 全部仓储重写 |
| ServiceStack.Text | 5.1.0 | **System.Text.Json** | 内置 | JsonSerializer 包装类重写；JsConfig↔JsonSerializerOptions 映射；~15 处调用点 |
| ImageMagickSharp（已死项目） | 1.0.0.19 | **Magick.NET** | 14.0.0 | Emby.Drawing.ImageMagick 重写（API 名近似） |
| SkiaSharp | 1.60.0 | SkiaSharp | 3.116.1 | SKBitmap/SKImage API 更新，Emby.Drawing.Skia 适配 |
| TagLib.Portable（闭源 DLL） | — | TagLibSharp | 2.3.0 | API 兼容，改 using |
| SharpCompress | 0.21.1 | SharpCompress | 0.38.0 | 入口类名基本不变 |
| SimpleInjector | 4.3.0 | SimpleInjector | 5.6.0 | 小（Lifestyle API 微调） |
| PlaylistsNET | 1.0.2 | PlaylistsNET | 1.0.2 | 无变化，仅转 PackageReference |
| WebRequest/ServicePointManager | — | HttpClient/SocketsHttpHandler | 内置 | HttpClientManager 重写（TLS/超时/代理语义保持） |
| packages.config | — | PackageReference + Directory.Build.props | — | 38+ System.* shim 包全删 |
| ConfigurationManager(app.config) | — | Microsoft.Extensions.Configuration | 10.0.0 | 入口项目改用 |
| System.Management / ServiceProcess | GAC | NuGet 同名包 | 10.0.0 | 仅 Windows 入口 |
| MEF（System.ComponentModel.Composition） | GAC | NuGet 同名包 | 10.0.0 | 插件加载保持 MEF |
| **SocketHttpListener（仓库内源码）** | 2016 | **ASP.NET Core Kestrel** | 10.0.0 | HttpListenerHost 重写为 Kestrel 中间件管线（C13）：ServiceStack 风格路由分发 + 原生 WebSocket + 静态文件；SocketHttpListener 项目退役删除 |
| Mono.Nat / RSSDP / BDInfo / DvdLib（仓库内源码） | — | 保留 | — | net10.0 直接编译 |

---

## 7. 执行计划（每行 = 1 个 commit，共 ~30 个）

### P0 反编译恢复（1 commit）
- C01 反编译 6 个闭源 DLL → src/ 源码项目 + TagLibSharp 替换 + 删除 packages/ 引用

### P1 SDK 工程化（7 commits，依赖顺序：叶子→根）
- C02 Directory.Build.props + sln 重组 + .gitignore 更新（packages/、bin/、obj/）
- C03 叶子库：BDInfo、DvdLib、Mono.Nat、RSSDP、Emby.XmlTv、Emby.Naming、Emby.Server.MediaEncoding
- C04 模型层：MediaBrowser.Model、MediaBrowser.Common、MediaBrowser.Controller
- C05 绘图层：Emby.Drawing(+.Skia/.ImageMagick/.Net)、Emby.Photos
- C06 业务层：Emby.Dlna、Emby.Notifications、MediaBrowser.LocalMetadata、MediaBrowser.XbmcMetadata、MediaBrowser.Providers
- C07 核心层：Emby.Server.Implementations（含 HttpClientManager 重写）
- C08 API 层 + 入口：MediaBrowser.Api、WebDashboard、Server.Mono→MediaBrowser.Server（合并 ServerApplication，Windows 功能运行时判断）、Tests

### P2 组件现代化（5 commits）
- C09 ServiceStack.Text → System.Text.Json（JsonSerializer 兼容层 + 全部调用点）
- C10 SkiaSharp 1.60 → 3.116 适配
- C11 ImageMagickSharp → Magick.NET 14
- C12 SharpCompress/SimpleInjector/PlaylistsNET 升级 + MEF/Configuration NuGet 化
- C13 **Kestrel 替换 SocketHttpListener**：HttpListenerHost → WebApplication 中间件（ServiceStack 风格路由分发、原生 WebSocket、dashboard-ui 静态文件、范围请求/流式响应），删除 SocketHttpListener 项目

### P3 同步移除（2 commits）
- C14 删除 Sync/Connect DLL、源码命名空间、模型字段、引用清理
- C15 dashboard-ui 同步/连接页面与脚本清理

### P4 PostgreSQL（12 commits）
- C16 Npgsql + 连接配置（system.xml/env/默认值）+ PgSchemaInitializer（嵌入 SQL + 幂等门卫 + sync 过滤 + §5.2 对齐 DDL）
- C17 BasePgRepository（连接/事务/日志基座）
- C18 PgUserRepository
- C19 PgAuthenticationRepository
- C20 PgUserDataRepository
- C21 PgDisplayPreferencesRepository
- C22 PgActivityRepository
- C23 PgItemRepository① 基础 CRUD + guid↔id
- C24 PgItemRepository② chapters/media_streams
- C25 PgItemRepository③ people/links
- C26 PgItemRepository④ ancestors/inherited values
- C27 PgItemRepository⑤ 查询引擎（InternalItemsQuery→SQL）
- C28 PgItemRepository⑥ 聚合查询 + 删除全部 Sqlite 文件与 SQLitePCL 依赖

### P5 验证（不产生功能 commit，产出验证记录）
- 全量 build 0 error；启动冒烟（连 PG、建库、初始化向导）；扫描样例媒体库；核心 API 冒烟（/Items、/Users、播放信息）；提交验证记录文档

> 每个 commit 前跑 `dotnet build MediaBrowser.sln -c Debug`，非绿不提交。

---

## 8. 风险清单

| 风险 | 等级 | 缓解 |
|------|------|------|
| 反编译代码泛型/迭代器细节失真 | 低 | 编译期暴露；历史分支已整库编译通过 |
| SqliteItemRepository 查询引擎重写遗漏过滤条件 | 中 | 逐方法对照旧 SQL 翻译；P5 用真实媒体库对比行数/排序 |
| 分区表 FK 要求查询总带 type | 中 | 仓储层 WHERE 强制带 type 码；guid 唯一索引含 type |
| `GENERATED ALWAYS AS IDENTITY` 不允许显式插 id | 低 | 全部 `RETURNING id` + upsert 缓存 |
| dashboard-ui 老前端与新后端 DTO 字段差异（删 Connect 字段后） | 低 | 字段删除保持 JSON 兼容（前端读不到即忽略） |
| Kestrel 迁移：ServiceStack 风格路由/过滤器/流式响应语义差异 | 中 | C13 单独 commit + P5 冒烟覆盖（/Items、图片、视频流、WebSocket）；SocketHttpListener 删除前先并存验证 |
| PG 不可达时服务启动失败 | 低 | 启动期明确报错 + 重试 3 次 |

---

## 9. 决策记录（已全部确认，见 §0）

D1 反编译 / D2 sync 表照建留空 / D3 初始化器追加补充 DDL / D4 本期直接换 Kestrel / D5 合并单一入口 / D6 备份分支保留参考。

---

## 10. 交付物清单（执行完成后）

1. 全 net10.0 SDK 风格解决方案，`dotnet build` 0 error
2. `src/` 下 6 个恢复源码的项目（闭源 DLL 全部退役）
3. PostgreSQL 数据层（12 个 Pg* 文件 + SchemaInitializer）
4. 同步/云连接功能全链路移除
5. ~30 个按功能划分的 commit
6. `docs/upgrade-net10-report.md` 验证记录
