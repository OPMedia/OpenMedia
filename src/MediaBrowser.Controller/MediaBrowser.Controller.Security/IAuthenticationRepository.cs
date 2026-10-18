using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Devices;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Security;

/// <summary>
/// 认证仓储。异步方法为真实实现，同步方法为遗留门面桥接。
/// </summary>
public interface IAuthenticationRepository
{
	Task CreateAsync(AuthenticationInfo info, CancellationToken cancellationToken = default);

	Task UpdateAsync(AuthenticationInfo info, CancellationToken cancellationToken = default);

	Task DeleteAsync(AuthenticationInfo existing, CancellationToken cancellationToken = default);

	Task<QueryResult<AuthenticationInfo>> GetAsync(AuthenticationInfoQuery query, CancellationToken cancellationToken = default);

	Task<DeviceOptions> GetDeviceOptionsAsync(string deviceId, CancellationToken cancellationToken = default);

	Task UpdateDeviceOptionsAsync(string deviceId, DeviceOptions options, CancellationToken cancellationToken = default);

	// ---- 兼容桥接（默认实现 → 异步）----
	void Create(AuthenticationInfo info) => CreateAsync(info).GetAwaiter().GetResult();

	void Update(AuthenticationInfo info) => UpdateAsync(info).GetAwaiter().GetResult();

	void Delete(AuthenticationInfo existing) => DeleteAsync(existing).GetAwaiter().GetResult();

	QueryResult<AuthenticationInfo> Get(AuthenticationInfoQuery query) => GetAsync(query).GetAwaiter().GetResult();

	DeviceOptions GetDeviceOptions(string deviceId) => GetDeviceOptionsAsync(deviceId).GetAwaiter().GetResult();

	void UpdateDeviceOptions(string deviceId, DeviceOptions options) => UpdateDeviceOptionsAsync(deviceId, options).GetAwaiter().GetResult();
}
