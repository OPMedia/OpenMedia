namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// P8 查询门 / 连接池内部观测点的测试侧访问垫片。
    /// 真正的实现在 Emby.Server.PgData.BasePgRepository（internal，经 InternalsVisibleTo 开放）。
    /// </summary>
    internal static class PgGateAccess
    {
        public static int QueryGateCapacity => Emby.Server.PgData.PgDataObservability.QueryGateCapacity;

        public static int QueryGateTimeout => Emby.Server.PgData.PgDataObservability.QueryGateTimeout;

        public static int QueryGateInUse => Emby.Server.PgData.PgDataObservability.QueryGateInUse;

        public static long QueryGateRejectedCount => Emby.Server.PgData.PgDataObservability.QueryGateRejectedCount;

        public static int PoolMaxSize => Emby.Server.PgData.PgDataObservability.PoolMaxSize;

        public static int PoolTimeoutSeconds => Emby.Server.PgData.PgDataObservability.PoolTimeoutSeconds;

        public static int PoolCommandTimeoutSeconds => Emby.Server.PgData.PgDataObservability.PoolCommandTimeoutSeconds;

        public static (int Active, int Idle, int Total) PoolSnapshot()
        {
            return Emby.Server.PgData.PgDataObservability.PoolSnapshot();
        }
    }

    internal static class BasePgAccess
    {
        public static string DefaultConnectionString => Emby.Server.PgData.BasePgRepository.DefaultConnectionString;
    }
}