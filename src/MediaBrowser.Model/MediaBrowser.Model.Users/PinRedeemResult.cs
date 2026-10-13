namespace MediaBrowser.Model.Users;

public class PinRedeemResult
{
	public bool Success { get; set; }

	public string[] UsersReset { get; set; }
}
