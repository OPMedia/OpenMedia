using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Controller.Entities;

public class GameSystem : Folder, IHasLookupInfo<GameSystemInfo>
{
	[IgnoreDataMember]
	public override Guid DisplayPreferencesId => Id;

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => false;

	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => false;

	public string GameSystemName { get; set; }

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 16.0 / 9.0;
	}

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		if (!string.IsNullOrEmpty(GameSystemName))
		{
			userDataKeys.Insert(0, "GameSystem-" + GameSystemName);
		}
		return userDataKeys;
	}

	protected override bool GetBlockUnratedValue(UserPolicy config)
	{
		return false;
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)4;
	}

	public GameSystemInfo GetLookupInfo()
	{
		GameSystemInfo itemLookupInfo = GetItemLookupInfo<GameSystemInfo>();
		itemLookupInfo.Path = Path;
		return itemLookupInfo;
	}
}
