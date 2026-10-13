namespace MediaBrowser.Model.Updates;

public class CheckForUpdateResult
{
	public bool IsUpdateAvailable { get; set; }

	public string AvailableVersion
	{
		get
		{
			if (Package == null)
			{
				return "0.0.0.1";
			}
			return Package.versionStr;
		}
		set
		{
		}
	}

	public PackageVersionInfo Package { get; set; }
}
