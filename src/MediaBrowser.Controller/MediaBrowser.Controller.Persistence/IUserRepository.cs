using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Persistence;

/// <summary>
/// 用户仓储。异步方法为真实实现，同步方法为遗留门面桥接。
/// </summary>
public interface IUserRepository : IRepository, IDisposable
{
	Task CreateUserAsync(User user, CancellationToken cancellationToken = default);

	Task UpdateUserAsync(User user, CancellationToken cancellationToken = default);

	Task<List<User>> RetrieveAllUsersAsync(CancellationToken cancellationToken = default);

	Task DeleteUserAsync(User user, CancellationToken cancellationToken = default);

	Task<User> GetUserAsync(Guid guid, CancellationToken cancellationToken = default);

	Task<long?> GetInternalIdAsync(Guid guid, CancellationToken cancellationToken = default);

	// ---- 兼容桥接（默认实现 → 异步）----
	// 分析器豁免：这是有意为之的同步适配边界，供未迁移的第三方插件调用；
	// 实现即「阻塞等待异步」，新代码一律使用 *Async 版本。
#pragma warning disable VSTHRD002
	void CreateUser(User user) => CreateUserAsync(user).GetAwaiter().GetResult();

	void UpdateUser(User user) => UpdateUserAsync(user).GetAwaiter().GetResult();

	List<User> RetrieveAllUsers() => RetrieveAllUsersAsync().GetAwaiter().GetResult();

	void DeleteUser(User user) => DeleteUserAsync(user).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
}
