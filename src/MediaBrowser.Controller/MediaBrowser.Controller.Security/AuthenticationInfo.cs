using System;

namespace MediaBrowser.Controller.Security;

public class AuthenticationInfo
{
	public long Id { get; set; }

	public string AccessToken { get; set; }

	public string DeviceId { get; set; }

	public string AppName { get; set; }

	public string AppVersion { get; set; }

	public string DeviceName { get; set; }

	public Guid UserId { get; set; }

	public string UserName { get; set; }

	public DateTime DateCreated { get; set; }

	public DateTime DateLastActivity { get; set; }
}
