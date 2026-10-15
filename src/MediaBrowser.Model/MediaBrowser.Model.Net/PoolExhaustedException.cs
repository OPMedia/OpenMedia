using System;

namespace MediaBrowser.Model.Net;

/// <summary>
/// P8 并发防护：查询门满（全局单门 50 并发长查询在跑）——服务过载快速失败不雪崩。
/// 中间件映射 503 Service Unavailable（U2 池耗尽雪崩的止损层，docs/p8-analysis.md ③）。
/// </summary>
public class PoolExhaustedException : Exception
{
	public int Capacity { get; private set; }

	public PoolExhaustedException(int capacity)
		: base(string.Format("Server is handling too many concurrent queries (limit {0}). Please retry.", capacity))
	{
		Capacity = capacity;
	}
}
