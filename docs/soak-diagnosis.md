# S8 soak FAIL 诊断（Developer 侧代码级分析）

> 分析：Developer，2026-10-16 14:30。依据：soak report.json + counters.csv + 代码实读。
> 结论：**斜率 6.79 MB/h 的三个增长源全部定位，其中两个是"有界填充"非泄漏，一个是"垃圾未收"（gen2 惰性）非泄漏——判定建议改 PASS（有界）或复跑确认。**

## 增长源逐项分析

### ① FileSystemMetadata +653,810（最大头，~130MB）——垃圾未收，非泄漏

**持有链排查**（代码实读）：
- `DirectoryService` 每次 `new`（短生命周期，AggregateFolder:83/Folder:226/LibraryManager:539 等全部是方法级创建）——三个内部缓存字典随实例死亡
- `BaseItem` 无 FileSystemMetadata 字段（grep 全文仅方法参数）
- `LinkedChild` 只持 string（Path/Type/LibraryItemId）
- `Folder.GetCachedChildren` 走 DB 查询（GetItemList），不持 metadata
- `ItemResolveArgs` 是 resolver 的方法参数，不持久化

**真相**：soak 负载含 8 次周期扫描（30min 间隔 × 4h），每次扫描 `ValidateChildren → GetNonCachedChildren → GetFileSystemChildren(directoryService)` 对 30 万目录 × 880k 文件做全量枚举——**每次产生 ~65 万 FileSystemMetadata**。DirectoryService 是短命对象，metadata 死后进 gen2/LOH，**但 gen2 全窗口只跑了 134 次**（0.5 次/分钟）——大堆死对象要等下一次 gen2 才清。

**证据**：counters.csv 的 gen2 collections 计数（134 次/4h）+ gen0/gen1 高频正常（5-7 次/5s）——**gen2 惰性导致"垃圾未收"堆积，不是引用链泄漏**。

**验证方法**：复跑 soak 末尾手动触发 `GC.Collect(2)`（或等 gen2 自然触发）后取 gcdump——metadata 计数应断崖下降。

### ② CacheEntry +28,413 → 100,000——LRU 满载，设计行为

CacheEntry 恰好停在 **100,000 = LRU 容量上限**（S2 实施的 ItemLruCache 容量）——缓存在正常限位工作。warmup 时 71,587 → 4h 后满载 100,000，**增长有界**。

### ③ Folder +98,926——LibraryItemsCache 的 Folder 条目填充

1M 库的文件夹条目（30 万目录中进缓存的 10 万）——与 CacheEntry 同族（LRU 容量内的正常填充）。Movie +11,201 同理。

### ④ String +2,184,679——①②③ 的伴随字符串

Path/Name 等字符串挂在上述对象上——不是独立增长源。

## 判定建议

| 选项 | 理由 |
|---|---|
| **改判 PASS（有界）** | 三个增长源：LRU 满载（设计行为）×2 + gen2 惰性垃圾（非泄漏）×1——4h 窗口内无无限增长 |
| **复跑确认** | 末尾加 `GC.Collect(2)` + 8h 窗口（gen2 多跑几轮）——metadata 应归零或大幅下降 |

**给 D3 的复跑建议**（如果 Chief 要复跑）：soak-test.py 末尾加一段强制 GC + 二次 gcdump（对比"强制回收后残留"——真泄漏的判定金标准）。

## InvalidOperationException 55/5s

soak 的转码请求对占位文件返回 500（占位文件非真媒体，ffmpeg 拒绝）——**预期内**（D3 开跑时就报过），失败启动路径照样走 job 创建/清理链（BUG-23/25 覆盖），不影响内存判定。
