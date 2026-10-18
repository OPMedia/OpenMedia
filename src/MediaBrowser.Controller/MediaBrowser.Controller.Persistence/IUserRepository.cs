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
	void CreateUser(User user) => CreateUserAsync(user).GetAwaiter().GetResult();

	void UpdateUser(User user) => UpdateUserAsync(user).GetAwaiter().GetResult();

	List<User> RetrieveAllUsers() => RetrieveAllUsersAsync().GetAwaiter().GetResult();

	void DeleteUser(User user) => DeleteUserAsync(user).GetAwaiter().GetResult();
}
