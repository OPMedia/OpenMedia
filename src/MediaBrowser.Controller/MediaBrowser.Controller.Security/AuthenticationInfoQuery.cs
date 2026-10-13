using System;

namespace MediaBrowser.Controller.Security;

public class AuthenticationInfoQuery
{
	public string DeviceId { get; set; }

	public Guid UserId { get; set; }

	public string AccessToken { get; set; }

	public bool? HasUser { get; set; }

	public int? StartIndex { get; set; }

	public int? Limit { get; set; }
}
