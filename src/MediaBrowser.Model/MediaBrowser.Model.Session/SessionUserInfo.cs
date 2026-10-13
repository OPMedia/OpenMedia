using System;

namespace MediaBrowser.Model.Session;

public class SessionUserInfo
{
	public Guid UserId { get; set; }

	public string UserName { get; set; }
}
