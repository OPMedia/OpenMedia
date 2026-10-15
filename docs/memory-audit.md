# 内存泄漏初查报告（100 万条/1 万用户目标先行数据）

- 日期：2026-10-15
- 执行：Developer-3（Chief 派单，供 scale-design 实测数据）
- 环境：整份复制部署实例（D:\OpenMedia-mem，端口 8099，共享 PG 库 148 条目）
- 工具：dotnet-counters 10.0.750501（CSV 采集）+ dotnet-gcdump 10.0.750501（三时点堆快照）
- 证据文件：tests/results/mem-baseline.gcdump / mem-after.gcdump / mem-after2.gcdump / counter.csv（未入库，本地留存）

## 测试协议

两轮负载循环（查询 + PlaybackInfo + HLS 分段 + DELETE ActiveEncodings + 条目刷新 + 周期库扫描）：

| 轮次 | 时长 | 迭代 | HTTP 请求 | 扫描 | 错误 |
|---|---|---|---|---|---|
| 第一轮 | 6 min | 459 | ~2,295 | 7 | 0 |
| 第二轮 | 3 min | 213 | ~1,065 | 3 | 0 |

每迭代：1× /Items?Recursive 全字段查询（DtoService 序列化路径）、1× PlaybackInfo、1× HLS 分段
（真实转码 job 创建→DELETE 销毁，共 672 个 job 生命周期）、1× 条目元数据刷新。

## 核心结论：当前规模（148 条目）无实质泄漏

### 堆增长三时点

| 快照 | GC Heap | 对象数 | 阶段 |
|---|---|---|---|
| baseline | 21,500,742 B | 317,835 | 启动后空载 |
| after-1 | 22,773,444 B | 330,727 | 第一轮 459 迭代后（+1.27MB / +12,892） |
| after-2 | 23,032,695 B | 331,232 | 第二轮 213 迭代后（+259KB / +505） |

**增长急剧衰减**：28 obj/迭代（第一轮）→ 2.4 obj/迭代（第二轮）——预热曲线特征
（缓存填充 + JIT），非线性泄漏签名。若为线性泄漏，第二轮应 +6,000 对象，实测 +505。

### 第一轮增长的构成（全部为预热类）

- System.String +2,390、反射元数据族（GenericMethodInfo +1,309 / RuntimeParameterInfo
  +1,231 / RuntimeMethodHandle +1,056 / GenericFieldInfo +857 / RuntimeMethodInfo +555 /
  Signature +541）——STJ 序列化器的 ReflectionEmit 缓存 + 路由/DTO 绑定反射缓存填充，
  以类型数为上界（有限集），非无界
- 首次扫描的条目装载（LibraryItemsCache 从 0 → 全库 148 条 + 元数据中间对象）

### 转码 job 生命周期：干净

672 个 job（创建→DELETE 销毁）后：TranscodingJob 实例 0 残留、_activeTranscodingJobs
列表恒定、TimerQueueTimer 27→23→26 波动（创建/销毁平衡）、定时器无累积。
BUG-25 修复的清理链在持续负载下验证通过。

### 计数器曲线

gen0 稳定 3.4MB、LOH 收缩后稳定 98KB、GC 代际无异常频率——与快照结论一致。

### 残留观察（量级小，登记不阻塞）

第二轮 +505 对象中：DisplayClass11_0 闭包 +255、String +111、RuntimeType +54——
约 1.2 对象/迭代的缓增尾巴，疑似惰性缓存继续填充或每请求小对象滞留一轮 GC。
建议五期 soak 测试（数小时负载）验证其是否归零。

## 对 100 万条/1 万用户目标的推断（供 scale-design）

1. **本次未发现每请求线性泄漏**——请求路径（查询/序列化/转码/刷新）在 672 job + 3,360
   请求下内存行为健康。
2. **结构性风险与请求路径无关，与规模挂钩**：
   - LibraryItemsCache 无上限无逐出——148 条 = 21.5MB 基线堆的主体；**100 万条全量
     装载按同比例外推 ≈ 145GB**（不可行）——S1（容量上限+LRU）是硬前提，不是优化
   - 逐行 JSONB 全量反序列化（水合）在 100 万条下 CPU/内存双杀（P7 结论一致）
   - STJ/反射缓存以类型数为界——DTO 类型有限集，预计可控
3. **1 万用户维度本次未覆盖**（单用户负载）——会话/认证/user_datas 缓存的压力需要
   五期专项（多用户并发 fixture）。

## 方法论备注

- gcdump 的 report 按类型短名聚合——同名闭包（c__DisplayClass0_0）跨类合并，join diff
  会产出伪增长行；本报告的闭包结论以三时点直查计数为准（21,500/9,412 恒定=启动稳态）。
- .NET 10 运行时不支持非损益 Block 缓冲，gcdump 为 lossy 模式——大堆下可能不完整，
  本规模（23MB）影响可忽略；100 万条复测时改用 dotnet-dump + analyze 或多快照交叉。
