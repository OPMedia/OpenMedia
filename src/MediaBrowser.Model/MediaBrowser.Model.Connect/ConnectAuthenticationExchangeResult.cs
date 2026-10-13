using System;

namespace MediaBrowser.Model.Connect;

public class ConnectAuthenticationExchangeResult
{
	public Guid LocalUserId { get; set; }

	public string AccessToken { get; set; }
}
