namespace MediaBrowser.Model.Configuration;

public class BaseApplicationConfiguration
{
	public bool EnableDebugLevelLogging { get; set; }

	public bool EnableAutoUpdate { get; set; }

	public int LogFileRetentionDays { get; set; }

	public bool RunAtStartup { get; set; }

	public bool IsStartupWizardCompleted { get; set; }

	public string CachePath { get; set; }

	public BaseApplicationConfiguration()
	{
		EnableAutoUpdate = true;
		LogFileRetentionDays = 3;
		RunAtStartup = true;
	}
}
