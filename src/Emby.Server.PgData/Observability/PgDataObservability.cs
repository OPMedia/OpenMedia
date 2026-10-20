using System;

namespace Emby.Server.PgData
{
    /// <summary>
    /// DB 侧观测点（Task D / docs/p8-analysis.md）+ P8 查询门状态的只读读出。
    ///
    /// 存在理由：docs/spec/acceptance.md §4 要求 /metrics 暴露「连接池占用」等指标，
    /// 但 Prometheus 端点是别人的任务；仓储层先把可读的数字做出来，让
    /// "池被打穿 / 门被拒"不再只能像 U2 当年那样靠人肉 dotnet-counters 事后反推。
    ///
    /// 约束（评审）：不引新依赖、不用 OpenTelemetry——只用现有静态计数器 + ILogger。
    /// Npgsql 10 起池内部计数（PoolingDataSource._numConnectors/_idleCount）不再公开，
    /// 这里给出的是应用层可读口径；Npgsql 侧的精确数字请用 NpgsqlDataSource.MetricsReporter
    /// （需启用 OpenTelemetry）或 PG 的 pg_stat_activity。
    /// </summary>
    public static class PgDataObservability
    {
        /// <summary>
        /// P8 查询门容量（默认 50，可被 OPENMEDIA_QUERY_GATE_CAPACITY 覆盖）。
        /// </summary>
        public static int QueryGateCapacity => BasePgRepository.QueryGateCapacityValue;

        /// <summary>
        /// P8 查询门等待上限（毫秒，默认 3000，OPENMEDIA_QUERY_GATE_TIMEOUT_MS 可覆盖）。
        /// </summary>
        public static int QueryGateTimeout => BasePgRepository.QueryGateTimeoutValue;

        /// <summary>
        /// 当前占用中的查询门槽位数。等于容量即门已饱和（后续请求 503）。
        /// </summary>
        public static int QueryGateInUse => BasePgRepository.QueryGateInUse;

        /// <summary>
        /// 查询门拒绝累计次数（门等待超时 → PoolExhaustedException → 503）。
        /// 这是 U2 雪崩止损层的直接命中计数：非 0 即说明防护实际在动作。
        /// </summary>
        public static long QueryGateRejectedCount => BasePgRepository.QueryGateRejectedCount;

        /// <summary>
        /// 连接池上限（MaxPoolSize，默认 100，OPENMEDIA_PG_MAX_POOL_SIZE 可覆盖）。
        /// </summary>
        public static int PoolMaxSize => BasePgRepository.PoolMaxSize;

        /// <summary>
        /// 连接池获取等待（Timeout，秒，默认 3）。
        /// </summary>
        public static int PoolTimeoutSeconds => BasePgRepository.PoolTimeoutSeconds;

        /// <summary>
        /// 单命令超时（CommandTimeout，秒，默认 30）。
        /// </summary>
        public static int PoolCommandTimeoutSeconds => BasePgRepository.PoolCommandTimeoutSeconds;

        /// <summary>
        /// 连接池占用快照：(active, idle, total)。
        /// active = 借出未归还；total = 进程内累计借出（峰值上界估计）；idle = total - active 的下界估计。
        /// </summary>
        public static (int Active, int Idle, int Total) PoolSnapshot()
        {
            return BasePgRepository.PoolSnapshot();
        }

        /// <summary>
        /// 打点一行 DB 池/门状态（ILogger.Info，供 /metrics 就绪前的临时观测）。
        /// 建议由宿主按固定间隔调用（当前无内置定时器，避免后台线程横跨仓储生命周期）。
        /// </summary>
        public static string FormatSnapshot()
        {
            var (active, idle, total) = PoolSnapshot();
            return "pg.pool active=" + active + " idle=" + idle + " total=" + total
                + " max=" + PoolMaxSize
                + " gate.inUse=" + QueryGateInUse + "/" + QueryGateCapacity
                + " gate.rejected=" + QueryGateRejectedCount;
        }
    }
}