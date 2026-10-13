using System;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Net;

public class AuthorizationInfo
{
	public Guid UserId
	{
		get
		{
			if (User != null)
			{
				return User.Id;
			}
			return Guid.Empty;
		}
	}

	public User User { get; set; }

	public string DeviceId { get; set; }

	public string Device { get; set; }

	public string Client { get; set; }

	public string Version { get; set; }

	public string Token { get; set; }
}
