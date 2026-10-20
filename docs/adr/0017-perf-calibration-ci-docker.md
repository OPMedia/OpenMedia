# ADR 0017：性能口径重建、CI 门禁边界与 Docker 镜像的 web/dist 托管缺口

- 状态：已接受
- 日期：2026-10-20
- 影响面：`tests/perf/**`、`.github/workflows/**`、`Dockerfile`、`.dockerignore`、`docs/perf-baseline.md`
- 上游依据：`docs/spec/core.md:38`（CI/发布 = GitHub Actions：Go test → web build → amd64/arm64 镜像）、`docs/spec/acceptance.md` §3（压测脚本进 CI，阈值放宽跑）
- 不变量来源：`docs/perf-baseline.md` §5 P4 的 BR-45 教训（合成 fixture 祖先分布失真）

## 背景：三件互相咬合的事

二期 T2 交付的 `docs/perf-baseline.md` 处于"看起来有数、其实不能引用"的状态：

1. **测于 Debug 构建**（该文件 §0 自己写明 `.NET 10 Debug 构建`）。无 ReadyToRun、分层编译未预热，
   JIT 成本全额计入请求延迟。用它做容量承诺等于把开发期成本当成生产期成本。
2. **库规模口径断裂**。老数字基于 147 条目的库；现在共享库 `openmedia` 只剩 1 个 Playlists 条目。
   §7.2 已经栽过一次：库从 64 涨到 147（+2.3×）就让 L1 P50 从 40.8ms 涨到 363ms（9×）——
   规模是主导变量，跨规模比数字没有意义。
3. **`pg_stat_statements` 从未启用**。§1 与 §6 从 2026-10-14 起就把"慢查询 Top-20"挂在
   "待排期管理员重启 PG"，§1 还写着"conf 修改已回滚"。欠了 4 天。

同期项目零 CI（无 `.github/`）、零容器化（无 `Dockerfile`），而 core.md:38 与
acceptance.md §3 两条 spec 都是空的。

## 决策

### D1：树形 fixture 替代平面 fixture（根治 BR-45 教训）

BR-45 的 `AncestorWithPresentationUniqueKey` 曾被报成 118.5ms 的"头号慢分支"，
四期判定为**跳过 + 结论更正**：早期 fixture 让全部条目共享同一个 CollectionFolder 祖先，
导致查询的 IN 子查询列表 = 全部条目，逐行回探被放大，EXPLAIN 结论完全是 fixture 假象。

所以新 fixture 生成器 `tests/perf/fixture-100k-tree.ps1` 从形态上保证祖先分布真实：
库 → 系列 → 季 → 集，深度 3-4 层，每个 CollectionFolder 只管自己的子树。

`tests/perf/fixture-ab-compare.ps1` 是同一套逻辑的小规模版（471 条目），专供 Debug/Release A/B。

**验收断言写进 fixture 尾部**（不是文档里口头约定）：
`SELECT l.name, count(a.item_id) ... GROUP BY l.name` —— 每库子孙数必须远小于总条目数。
历史平面 fixture 在这一步会输出单行 10k，一眼可辨。

### D2：Release 口径重测，且只在同规模下做前后对比

`docs/perf-baseline.md` 新增"Release 口径复测"节，保留历史 Debug 数字但标注口径作废原因。

**不做跨规模对比。** 老 Debug 数字（147 条目）与新 Release 数字（471 条目或 195k 条目）
之间不画任何箭头。要看构建配置的影响，就在同一个库、同一份数据、同一个用户、同一个负载下
Debug 和 Release 各跑一遍——`tests/perf/run-ab-load.ps1 -Tag debug|release` 就是这么设计的。

结果（471 条目树形库，同用户 `ab-user-001`，各 15s 档，各 2 轮干净数据）：
P50 上 Release 与 Debug 基本同量级（L1@10rps 26-27ms vs 23ms，L4 认证 45-49ms vs 49ms），
P95 上 Release 略优或持平（L1b 49-53ms vs 51-71ms，L3 16-18ms vs 13-28ms，
L4 155-199ms vs 206-269ms）。

这个结果本身是个有价值的结论：**该服务在 471 条目规模下不是 JIT/R2R 敏感的**
（耗时主要在等 PG 与 I/O，不在 CPU 密集的 JIT 代码上）。所以老 Debug 数字的"失真"
主要体现在大规模 + 高并发档，而不是小库暖路径。

**注意 L3 静态流在对比库里 100% 500**：对比库没有真实的 `c40cea18...` 视频文件。
两侧都是 500，所以 P95 仍可比（都在测"找不到文件的错误路径"开销），
但它不是流式吞吐的有效度量。要看真流式吞吐，用共享库 + 真媒体文件跑 `load-test.py`。

### D3：pg_stat_statements 启用方式（含一个值得记住的坑）

`postgresql.conf` 第 814 行改为：
```
shared_preload_libraries = 'pg_stat_statements'
```
重启 `postgresql-x64-18`，然后在 `postgres` 与 `openmedia_perf` 两库
`CREATE EXTENSION pg_stat_statements;`。

**踩坑记录（本机 2026-10-20 10:53-11:01，服务 STOPPED 约 8 分钟）**：
第一次重启失败后服务起不来，PG log 与 Application 事件都指向
"配置文件第 1 行行尾附近语法错误"，而 conf 内容看起来完全正常。
真正原因是 **PowerShell 写文件时加了 UTF-8 BOM（`EF BB BF`）**，
PG 的配置解析器不认 BOM，把它当第 1 行的非法字符。
修复：按字节去掉前 3 个 BOM 字节，服务 5 秒内起来。

教训：**改 PG 配置必须用无 BOM 的字节写入**（`[IO.File]::WriteAllBytes` 或
`UTF8Encoding($false)` 且确认首字节不是 `EF BB BF`）。
同理，`tests/perf/` 下的 `.ps1` 反而**必须**带 BOM（PS 5.1 子进程解析中文注释），
两种文件的 BOM 要求正好相反，别搞混。

**重启影响记录（用户明确要求）**：
- 重启前确认 `pg_stat_activity` 无外部连接（仅空 datname 的后台进程）
- 2026-10-20 10:53-10:54 首次重启；服务 Stopped 后因 BOM 问题多次启动失败
- 2026-10-20 11:01:41 最后一次失败尝试；11:02 去 BOM 后 `Start-Service` 成功
- 服务不可用窗口约 8 分钟（10:54 - 11:02），期间 `openmedia_perf`（195,251 条目）
  与共享库 `openmedia` 均未被写入，数据无损
- 恢复后验证：`SELECT 1` OK、`openmedia_perf.media_items` = 195,251、`openmedia.media_items` = 1

### D4：压测进 CI，但不设硬阈值门禁

`docs/spec/acceptance.md` §3 要求"压测脚本进 CI（阈值放宽跑）"。CI 里确实跑了
（job `perf-smoke`），P95 表 post 到 `$GITHUB_STEP_SUMMARY` 并传 artifact，
但**不 fail、不 block 合入**。理由：

1. **fixture 生成时间**。本机实测 195k 条目树形 fixture 灌库 5.58 分钟，CI runner 更慢。
   GitHub job 上限 6 小时，但 PR 上每次等 10 分钟出个不拦人的数字，Reviewer 会学会忽略它。
2. **runner 硬件方差**。GitHub runner 是共享/限量 CPU，同一次提交的 P95 能差 2-3×。
   任何固定阈值都会随机红，然后被人改成永不触发，比没有更糟。
3. **容量口径的正确位置**。容量承诺本该在本机安静窗口测（perf-baseline.md 的既有口径），
   CI 只该回答"链路没坏、没有数量级劣化"。

所以 CI 用 `fixture-ab-compare.ps1`（471 条目，秒级生成）做冒烟；
要看全量 195k 口径，本机跑 `setup-perf-db.ps1 -Fixture tree-100k` + `run-ab-load.ps1`。

**如果将来要设门禁**，门槛应该按"相对本机基线的倍数"而非绝对值，且先在
`workflow_dispatch` 上收集 20+ 个样本画出分布再定。

### D5：Docker 镜像的 web/dist 托管缺口（明确记录，不假装已能用）

`docs/spec/core.md` 要求"后端托管 web 静态产物（同源）"，
但**实现是零**：`MediaBrowser.Server.csproj` 里没有任何引用 `web/dist` 的逻辑
（已 grep 确认），新前端目前只能靠 Vite 代理访问。

所以 `Dockerfile` 只把 `web/dist` 拷到 `/opt/openmedia/web`（备用路径），
并在文件头与 COPY 处都标注"待人接线"。
镜像跑起来后根路径 `/` 仍然是旧 dashboard-ui，**不是新前端**。

接线需要后端加静态文件托管（`UseStaticFiles` / 嵌入式资源 / SPA fallback），
那是 `src/` 与 `MediaBrowser.Server/` 的改动，不在本次范围内（也不在本 Agent 写权限内）。

同时记录了 ffmpeg 的路径决策：`FFMpegLoader` 三级查找里用了第一级
`ProgramSystemPath`（与 `MediaBrowser.Server.dll` 同级），
因为 Linux 分支下代码硬编码 `Version = "20170308"`，走第三级
`ProgramDataPath/ffmpeg/20170308/` 会去找一个 2017 年的旧版。
版本锁死为 `7.1.1`（johnvansickle 静态构建，amd64/arm64 同源）。

## 后果

**正面**
- `docs/perf-baseline.md` 的 Release 口径节可被引用；Debug 数字保留作历史但不作承诺
- 欠 4 天的 Top-20 已补，且暴露出 §2 EXPLAIN 矩阵没覆盖到的问题（见下）
- 项目第一次有 CI 与容器化骨架，两条 spec 不再空转
- fixture 生成器带祖先分布断言，BR-45 那类失真不会再次悄悄发生

**负面 / 未解决**
- CI 的 perf-smoke 不拦人，属于"可见但无强制力"
- Dockerfile 的 web/dist 仍是死路径，需要后端接线才是真同源
- `pg_stat_statements` 只在本机 PG 启用；CI 的 postgres service container 没开
  （GitHub 的 postgres 镜像需要额外 command 参数才能 preload）
- Top-20 只在 `openmedia_perf`（195k 树形）上取证过；小库与共享库口径未取

**Top-20 暴露的新问题（超出本任务范围，登记待办）**
`total_exec_time` 口径下（408,179 calls / 2,266,288ms 总量）：
- 第 1 名 `count(DISTINCT A.presentation_unique_key)`：236 calls 吃掉 1,294,690ms
  （占全库 PG 时间 57%），mean 5,486ms。EXPLAIN 显示 8 分区全 Seq Scan +
  195,252 行 external merge 落盘排序（3248kB temp）。这是 §2 EXPLAIN 矩阵
  （50 个分支、10k 数据）没覆盖到的形态，且量级远超当年任何分支。
- 第 2 名 `INSERT activity_log_types ... ON CONFLICT`：14,750 calls / 691,693ms
- 第 3 名 `count(*) FROM media_items A WHERE A.type IN`：16,948 calls / 193,612ms

这三条的调用次数（236 / 14,750 / 16,948）说明它们是**被高频调用的**，
不是偶发重查询——这是下一步优化最该看的地方。

## 备选方案（未采用）

- **CI 设 P95 硬阈值**：否决，见 D4 第 2 条。
- **用 docker service container 跑 PG 集成测试**：CI 里采用了
  （GitHub runner 有 docker），但本机没有 docker，所以本机等价步骤写的是
  "本机 PG 已在 5432 跑"，并在 CI 注释里明确标注这个不对称。
- **平面 fixture + 在文档里提醒"祖先分布要注意"**：否决。BR-45 证明人不会记得看提醒，
  断言必须长在 fixture 里。
- **Dockerfile 里顺手加个 nginx 托管 web/dist**：否决。那会造出第二个同源服务的假象，
  且 core.md 要的是"后端托管"，不是"旁边再站一个"。

## 参考

- `docs/perf-baseline.md` §0/§1/§5 P4/§7.2/§7.5/§7.7（历史 Debug 口径）
- `tests/perf/fixture-100k-tree.ps1`、`tests/perf/fixture-ab-compare.ps1`
- `tests/perf/run-ab-load.ps1`、`tests/perf/load-ab.py`
- `tests/perf/pg-stat-statements-top20.ps1`、`tests/perf/results/pgss-*.out`
- `.github/workflows/ci.yml`（含本机等价复现清单）
- `Dockerfile`（含 web/dist 缺口说明与 ffmpeg 路径决策）
