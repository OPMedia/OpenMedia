using System;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Devices;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Session;

namespace MediaBrowser.Controller.Devices;

public interface IDeviceManager
{
	event EventHandler<GenericEventArgs<CameraImageUploadInfo>> CameraImageUploaded;

	event EventHandler<GenericEventArgs<Tuple<string, DeviceOptions>>> DeviceOptionsUpdated;

	void SaveCapabilities(string reportedId, ClientCapabilities capabilities);

	ClientCapabilities GetCapabilities(string reportedId);

	QueryResult<DeviceInfo> GetDevices(DeviceQuery query);

	DeviceInfo GetDevice(string id);

	ContentUploadHistory GetCameraUploadHistory(string deviceId);

	Task AcceptCameraUpload(string deviceId, Stream stream, LocalFileInfo file);

	bool CanAccessDevice(User user, string deviceId);

	void UpdateDeviceOptions(string deviceId, DeviceOptions options);

	DeviceOptions GetDeviceOptions(string deviceId);
}
