using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Controller.Entities;

public class User : BaseItem
{
	private string _name;

	private volatile UserConfiguration _config;

	private readonly object _configSyncLock = new object();

	private volatile UserPolicy _policy;

	private readonly object _policySyncLock = new object();

	public static IUserManager UserManager { get; set; }

	public bool UsesIdForConfigurationPath { get; set; }

	public string Password { get; set; }

	public string EasyPassword { get; set; }

	public string Salt { get; set; }





	public override ItemImageInfo[] ImageInfos
	{
		get
		{
			return base.ImageInfos;
		}
		set
		{
			base.ImageInfos = value;
		}
	}

	[IgnoreDataMember]
	public long InternalId { get; set; }

	[IgnoreDataMember]
	public override string Path
	{
		get
		{
			return ConfigurationDirectoryPath;
		}
		set
		{
			base.Path = value;
		}
	}

	public override string Name
	{
		get
		{
			return _name;
		}
		set
		{
			_name = value;
			SortName = null;
		}
	}

	[IgnoreDataMember]
	public override string ContainingFolderPath => Path;

	[IgnoreDataMember]
	public Folder RootFolder => BaseItem.LibraryManager.GetUserRootFolder();

	public DateTime? LastLoginDate { get; set; }

	public DateTime? LastActivityDate { get; set; }

	[IgnoreDataMember]
	public UserConfiguration Configuration
	{
		get
		{
			if (_config == null)
			{
				lock (_configSyncLock)
				{
					if (_config == null)
					{
						_config = UserManager.GetUserConfiguration(this);
					}
				}
			}
			return _config;
		}
		set
		{
			_config = value;
		}
	}

	[IgnoreDataMember]
	public UserPolicy Policy
	{
		get
		{
			if (_policy == null)
			{
				lock (_policySyncLock)
				{
					if (_policy == null)
					{
						_policy = UserManager.GetUserPolicy(this);
					}
				}
			}
			return _policy;
		}
		set
		{
			_policy = value;
		}
	}

	[IgnoreDataMember]
	public string ConfigurationDirectoryPath => GetConfigurationDirectoryPath(Name);

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	public Task Rename(string newName)
	{
		if (string.IsNullOrEmpty(newName))
		{
			throw new ArgumentNullException("newName");
		}
		if (!UsesIdForConfigurationPath && !string.Equals(newName, Name, StringComparison.OrdinalIgnoreCase))
		{
			UsesIdForConfigurationPath = true;
			string configurationDirectoryPath = GetConfigurationDirectoryPath(newName);
			string configurationDirectoryPath2 = ConfigurationDirectoryPath;
			if (BaseItem.FileSystem.DirectoryExists(configurationDirectoryPath))
			{
				BaseItem.FileSystem.DeleteDirectory(configurationDirectoryPath, true);
			}
			if (BaseItem.FileSystem.DirectoryExists(configurationDirectoryPath2))
			{
				BaseItem.FileSystem.MoveDirectory(configurationDirectoryPath2, configurationDirectoryPath);
			}
			else
			{
				BaseItem.FileSystem.CreateDirectory(configurationDirectoryPath);
			}
		}
		Name = newName;
		return RefreshMetadata(new MetadataRefreshOptions((IDirectoryService)new DirectoryService(BaseItem.Logger, BaseItem.FileSystem))
		{
			ReplaceAllMetadata = true,
			ImageRefreshMode = MetadataRefreshMode.FullRefresh,
			MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
			ForceSave = true
		}, CancellationToken.None);
	}

	public override void UpdateToRepository(ItemUpdateType updateReason, CancellationToken cancellationToken)
	{
		UserManager.UpdateUser(this);
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 1.0;
	}

	private string GetConfigurationDirectoryPath(string username)
	{
		string userConfigurationDirectoryPath = BaseItem.ConfigurationManager.ApplicationPaths.UserConfigurationDirectoryPath;
		if (!UsesIdForConfigurationPath)
		{
			if (string.IsNullOrEmpty(username))
			{
				throw new ArgumentNullException("username");
			}
			string validFilename = BaseItem.FileSystem.GetValidFilename(username);
			return System.IO.Path.Combine(BaseItem.ConfigurationManager.ApplicationPaths.UserConfigurationDirectoryPath, validFilename);
		}
		return System.IO.Path.Combine(userConfigurationDirectoryPath, Id.ToString("N"));
	}

	public bool IsParentalScheduleAllowed()
	{
		return IsParentalScheduleAllowed(DateTime.UtcNow);
	}

	public bool IsParentalScheduleAllowed(DateTime date)
	{
		AccessSchedule[] accessSchedules = Policy.AccessSchedules;
		if (accessSchedules.Length == 0)
		{
			return true;
		}
		AccessSchedule[] array = accessSchedules;
		foreach (AccessSchedule schedule in array)
		{
			if (IsParentalScheduleAllowed(schedule, date))
			{
				return true;
			}
		}
		return false;
	}

	private bool IsParentalScheduleAllowed(AccessSchedule schedule, DateTime date)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		if (date.Kind != DateTimeKind.Utc)
		{
			throw new ArgumentException("Utc date expected");
		}
		DateTime localTime = date.ToLocalTime();
		if (DayOfWeekHelper.GetDaysOfWeek(schedule.DayOfWeek).Contains(localTime.DayOfWeek))
		{
			return IsWithinTime(schedule, localTime);
		}
		return false;
	}

	private bool IsWithinTime(AccessSchedule schedule, DateTime localTime)
	{
		double totalHours = localTime.TimeOfDay.TotalHours;
		if (totalHours >= schedule.StartHour)
		{
			return totalHours <= schedule.EndHour;
		}
		return false;
	}

	public bool IsFolderGrouped(Guid id)
	{
		string[] groupedFolders = Configuration.GroupedFolders;
		for (int i = 0; i < groupedFolders.Length; i++)
		{
			if (new Guid(groupedFolders[i]) == id)
			{
				return true;
			}
		}
		return false;
	}
}
