using System;

namespace MediaBrowser.Model.Devices;

public class DeviceQuery
{
	public bool? SupportsSync { get; set; }

	public Guid UserId { get; set; }
}
