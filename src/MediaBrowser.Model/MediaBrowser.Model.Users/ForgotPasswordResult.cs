using System;

namespace MediaBrowser.Model.Users;

public class ForgotPasswordResult
{
	public ForgotPasswordAction Action { get; set; }

	public string PinFile { get; set; }

	public DateTime? PinExpirationDate { get; set; }
}
