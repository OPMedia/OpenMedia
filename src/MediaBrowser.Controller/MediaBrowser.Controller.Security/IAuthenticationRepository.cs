using MediaBrowser.Model.Devices;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Security;

public interface IAuthenticationRepository
{
	void Create(AuthenticationInfo info);

	void Update(AuthenticationInfo info);

	void Delete(AuthenticationInfo existing);

	QueryResult<AuthenticationInfo> Get(AuthenticationInfoQuery query);

	DeviceOptions GetDeviceOptions(string deviceId);

	void UpdateDeviceOptions(string deviceId, DeviceOptions options);
}
