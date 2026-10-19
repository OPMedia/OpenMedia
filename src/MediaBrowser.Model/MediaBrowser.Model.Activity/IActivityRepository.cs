using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Model.Activity;

/// <summary>
/// 活动日志仓储。
///
/// 契约：<c>*Async</c> 为唯一真实实现（内部全异步）。
/// 同步方法仅为**尚未迁移的第三方插件/遗留调用方**保留的兼容桥接，
/// 新代码禁止使用（见 [Obsolete] 说明）。
/// </summary>
public interface IActivityRepository
{
	Task CreateAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default);

	Task<QueryResult<ActivityLogEntry>> GetActivityLogEntriesAsync(DateTime? minDate, bool? hasUserId, int? startIndex, int? limit, CancellationToken cancellationToken = default);

	// ---- 兼容桥接（默认实现 → 异步）----
	// 分析器豁免理由：这是唯一的同步入口，实现即「阻塞等待异步」；
	// 目的是让未迁移的第三方插件继续工作，属于有意为之的边界适配层。
	[Obsolete("改用 CreateAsync(...)。同步桥接仅为兼容未迁移插件保留。")]
#pragma warning disable VSTHRD002
	void Create(ActivityLogEntry entry) => CreateAsync(entry).GetAwaiter().GetResult();

	[Obsolete("改用 GetActivityLogEntriesAsync(...)。同步桥接仅为兼容未迁移插件保留。")]
	QueryResult<ActivityLogEntry> GetActivityLogEntries(DateTime? minDate, bool? hasUserId, int? startIndex, int? limit)
		=> GetActivityLogEntriesAsync(minDate, hasUserId, startIndex, limit).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
}
