using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Persistence;

/// <summary>
/// 用户数据仓储。
/// 异步方法（*Async）为唯一真实实现；同步方法仅为兼容既有上层门面的过渡桥接，
/// 由默认接口实现转发到异步版本（不在实现类内重复同步 I/O）。
/// </summary>
public interface IUserDataRepository : IRepository, IDisposable
{
	Task SaveUserDataAsync(long internalUserId, string key, UserItemData userData, CancellationToken cancellationToken);

	Task<UserItemData> GetUserDataAsync(long internalUserId, string key, CancellationToken cancellationToken = default);

	Task<UserItemData> GetUserDataAsync(long internalUserId, List<string> keys, CancellationToken cancellationToken = default);

	Task<List<UserItemData>> GetAllUserDataAsync(long internalUserId, CancellationToken cancellationToken = default);

	Task SaveAllUserDataAsync(long internalUserId, UserItemData[] userData, CancellationToken cancellationToken);

	// ---- 兼容桥接（默认实现 → 异步）----
	// 分析器豁免：这是有意为之的同步适配边界，供未迁移的第三方插件调用；
	// 实现即「阻塞等待异步」，新代码一律使用 *Async 版本。
#pragma warning disable VSTHRD002
	void SaveUserData(long internalUserId, string key, UserItemData userData, CancellationToken cancellationToken)
		=> SaveUserDataAsync(internalUserId, key, userData, cancellationToken).GetAwaiter().GetResult();

	UserItemData GetUserData(long internalUserId, string key)
		=> GetUserDataAsync(internalUserId, key).GetAwaiter().GetResult();

	UserItemData GetUserData(long internalUserId, List<string> keys)
		=> GetUserDataAsync(internalUserId, keys).GetAwaiter().GetResult();

	List<UserItemData> GetAllUserData(long internalUserId)
		=> GetAllUserDataAsync(internalUserId).GetAwaiter().GetResult();

	void SaveAllUserData(long internalUserId, UserItemData[] userData, CancellationToken cancellationToken)
		=> SaveAllUserDataAsync(internalUserId, userData, cancellationToken).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
}
