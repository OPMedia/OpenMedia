using System.Collections.Generic;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Model.Dlna;

namespace MediaBrowser.Controller.Dlna;

public interface IDlnaManager
{
	IEnumerable<DeviceProfileInfo> GetProfileInfos();

	DeviceProfile GetProfile(IDictionary<string, string> headers);

	DeviceProfile GetDefaultProfile();

	void CreateProfile(DeviceProfile profile);

	void UpdateProfile(DeviceProfile profile);

	void DeleteProfile(string id);

	DeviceProfile GetProfile(string id);

	DeviceProfile GetProfile(DeviceIdentification deviceInfo);

	string GetServerDescriptionXml(IDictionary<string, string> headers, string serverUuId, string serverAddress);

	ImageStream GetIcon(string filename);
}
