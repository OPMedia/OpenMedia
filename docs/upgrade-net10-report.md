# OpenMedia .NET 10 + PostgreSQL 升级验证报告

> 日期：2026-10-14
> 基线：Emby 3.5.3（`1d7c2ab4bf`） → 当前 HEAD
> 执行方式：智能体团队（Lead + 7 队友并行轨道），~30 个功能 commit

---

## 1. 总体结论

| 验证项 | 状态 | 说明 |
|--------|------|------|
| 全解决方案构建 | ✅ **0 错误** | 22 个项目全部 SDK net10.0（`dotnet build MediaBrowser.sln`） |
| 服务器启动流程 | ✅ 验证到 PG 连接点 | 环境信息→22 程序集加载→ApplicationHost.Init→仓储注册→Npgsql 连接 |
| JSON 序列化契约 | ✅ 7 项测试全过 | Guid "N"/枚举字符串/null 省略/宽容读/日期 O 格式/往返 |
| API 规范对齐 | ✅ B1-B5 完成 | 31 DTO 字段 + 13 过滤参数 + 16 新端点；客户端关键路径全对齐 |
| PG 运行时冒烟 | ⏳ 待用户执行 | 沙箱网络层拦截 127.0.0.1:5432（详见 §5），代码路径已验证到连接点 |
| 单元测试执行 | ⏳ 待用户执行 | 编译通过；testhost 进程被沙箱 IPC 限制拦截 |

## 2. 交付清单（commit 序列）

### 地基与转换（P0-P1）
- C01 反编译恢复 6 个闭源 DLL（~44K 行）+ BDInfo/DvdLib 转 SDK
- C02 Directory.Build.props + packages/（2485 文件）退役
- C03 Mono.Nat + RSSDP（leaf-libs）
- C05 绘图层 5 项目 + SkiaSharp 3.116 + Magick.NET 14（media-libs）
- C06 业务层 5 项目（business-libs）
- C07/C07b Implementations 转换 + HttpClientManager 重写（WebRequest→HttpClient）
- C08a-d WebDashboard/Api/入口合并（单一跨平台 MediaBrowser.Server）/Tests

### 组件现代化（P2）
- C09 ServiceStack.Text → System.Text.Json（4 个自定义 converter，契约兼容层）
- C13 Kestrel 替换 SocketHttpListener（23 文件 ~5,780 行新 HTTP 栈，旧栈 4 目录退役）

### 同步移除（P3）
- C14 源码层（DLL + 4 命名空间 + 模型字段 + 9 文件引用清理）
- C15 dashboard-ui（15 文件 + 路由/菜单/API 调用点清理）

### PostgreSQL（P4）
- C16 PgData 第一批（SchemaInitializer + Base + 5 仓储）
- C16b ApplicationHost 六仓储接线（OPENMEDIA_PGCONN 环境变量）
- C23-C27 PgItemRepository 六 partial（~8000 行，60 过滤分支逐条翻译）
- C28 Sqlite 层删除（10 文件 + SQLitePCL 全家）

### API 规范对齐（P6，openapi.json 4.1.1.0）
- C29 差距地图（真实缺口 44 端点，非初步盘点的 95）
- B1 31 DTO 字段 / B2 13 过滤参数 / B3 14 新端点 / B4 subtitles.m3u8 / B5 index.bif 占位
- B6 插件端点 25 个暂缓（spec 从装插件实例导出，官方客户端不调用）

## 3. 架构终态

```
MediaBrowser.Server (net10.0 单一跨平台入口)
├── ASP.NET Core Kestrel（src/Emby.Server.HttpServer）
│   └── ServiceStack 风格路由分发（61 个服务类零改动）+ 原生 WebSocket
├── System.Text.Json（契约兼容层：PascalCase/Guid N/枚举字符串/吞错语义）
├── PostgreSQL（src/Emby.Server.PgData，Npgsql 10.0.3）
│   └── 按用户 schema 初始化（幂等门卫 + 补充对齐 DDL + FK 缺陷内存修正）
├── src/ 六个反编译恢复项目（闭源 DLL 全退役）
├── SkiaSharp 3.116 / Magick.NET 14 / TagLibSharp 2.3 / SharpCompress 0.38
└── SimpleInjector 5.6 / PlaylistsNET / MEF（NuGet 化）
```

## 4. 已知语义差异（全部有代码注释）

1. **AuthenticationInfo.UserName**：创建时快照 → JOIN local_users 实时值（tokens 表无该列）
2. **activity_log**：item/user 引用在目标行删除后无法还原 GUID（旧表存字符串）；LogSeverity 枚举名映射（Info↔Information 等，schema CHECK 所迫）
3. **旧 MinDateLastSaved 过滤参数名错位 bug**（恒空结果）已修正为可用
4. **GetPeopleNames 排序**：SQLite 任意序 → MIN(list_order)（首次出现序）
5. **item_extradata PK 去重**（旧表允许重复值）
6. **DISTINCT ON** 代表行选择比 SQLite GROUP BY 更确定（按排序取组内最优）
7. **EmbeddedImage 流**超出 schema CHECK(0,1,2) 保存时跳过
8. **Trim() fuzz 容差丢弃**（Magick.NET API 差异，视觉差异轻微）
9. **index.bif 404 占位**（BIF 生成未实现，客户端优雅降级）

## 5. 用户侧待执行验证（沙箱限制）

沙箱网络层拦截 127.0.0.1:5432（IPv4/IPv6 TCP 均超时；PG 日志可见连接尝试到达，
SYN-ACK 回程被丢）。请在正常终端执行：

```powershell
# 1. PG 连通冒烟（建库 + schema 初始化 + 启动）
cd D:\Dev\OpenMedia\MediaBrowser.Server
dotnet run -c Debug
# 预期：ProgramData-Server 目录生成 → schema 全量建表（含分区）→
#       Kestrel 监听 8096 → 浏览器打开 http://localhost:8096/web/index.html 进入启动向导

# 2. 单元测试
cd D:\Dev\OpenMedia
dotnet test MediaBrowser.Tests/MediaBrowser.Tests.csproj

# 3. API 冒烟（启动后）
curl http://localhost:8096/emby/System/Info/Public
# 预期：PascalCase 键名、Id 为 32 位无连字符 hex
```

P5 冒烟重点（pg-data 建议）：SchemaInitializer 全量 DDL + item_extradata 重建、
SaveItems 两遍式、GetItems DISTINCT ON 路径、GetItemValues 聚合。

## 6. schema 文件缺陷记录（重要）

`emby_pgsql_dump_schema_optimized.sql` 存在系统性缺陷：14 处
`FOREIGN KEY (单列) REFERENCES media_items (id, type)` 在 PostgreSQL 直接报错
（引用列数不匹配；分区表唯一索引必须含分区键）。处置：PgSchemaInitializer
执行前内存内剥离这些 FK 子句（磁盘文件一字未动），应用层维护完整性
（DeleteItem 显式删子行），与旧 SQLite 行为一致。合法外键全部保留。

若希望改为 `(item_id, item_type)` 复合外键（需同时给引用表加 item_type 列），
可后续调整 SQL 文件与初始化器。

## 7. 遗留与后续建议

1. **B6 插件端点 25 个**：暂缓（不影响客户端零适配）；若将来装 Playback Reporting 等插件再对齐
2. **index.bif**：BIF 生成需 ffmpeg 定时抽帧打包，Route 已建好，补 Get 方法体即可
3. **GenreItems Id**：保持 Guid（域层无 long 数据源；将来 PG genre 表切 bigint 主键时联动改）
4. **Emby.Dlna 的 4.1 API 端点**（14 个）：DlnaManager 已有实现基础，P5 冒烟后按需补
5. **WinForms 托盘/闪屏**：随 D5 决策放弃（控制台运行；Windows 服务可用 sc create + 常驻进程方式）
6. **自更新**（ApplicationUpdater）：随 Emby 授权体系退役删除
