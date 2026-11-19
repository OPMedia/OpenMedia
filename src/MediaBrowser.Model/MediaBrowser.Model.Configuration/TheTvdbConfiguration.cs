namespace MediaBrowser.Model.Configuration;

/// <summary>
/// TheTVDB v4 API credentials. Both values are user supplied; the server never ships a key.
/// </summary>
public class TheTvdbConfiguration
{
	/// <summary>
	/// API key created in the user's thetvdb.com dashboard (free key).
	/// Empty means the TVDB provider and its post-scan task are disabled.
	/// </summary>
	public string ApiKey { get; set; } = string.Empty;

	/// <summary>
	/// Optional subscriber PIN for user-supported API keys.
	/// </summary>
	public string Pin { get; set; } = string.Empty;
}
