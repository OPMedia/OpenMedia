using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Model.Activity;

/// <summary>
/// 活动日志仓储。异步方法为真实实现，同步方法为遗留门面桥接。
/// </summary>
public interface IActivityRepository
{
	Task CreateAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default);

	Task<QueryResult<ActivityLogEntry>> GetActivityLogEntriesAsync(DateTime? minDate, bool? hasUserId, int? startIndex, int? limit, CancellationToken cancellationToken = default);

	void Create(ActivityLogEntry entry) => CreateAsync(entry).GetAwaiter().GetResult();

	QueryResult<ActivityLogEntry> GetActivityLogEntries(DateTime? minDate, bool? hasUserId, int? startIndex, int? limit)
		=> GetActivityLogEntriesAsync(minDate, hasUserId, startIndex, limit).GetAwaiter().GetResult();
}
