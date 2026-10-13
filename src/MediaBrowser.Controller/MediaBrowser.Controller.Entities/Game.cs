using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class Game : BaseItem, IHasScreenshots, ISupportsPlaceHolders, IHasLookupInfo<GameInfo>
{
	[IgnoreDataMember]
	public override bool SupportsExternalTransfer => CanDownloadAsSingleMedia();

	[IgnoreDataMember]
	public override bool SupportsThemeMedia => true;

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	[IgnoreDataMember]
	public override bool SupportsLocalTrailers => true;

	[IgnoreDataMember]
	public override string MediaType => "Game";

	public int? PlayersSupported { get; set; }

	public bool IsPlaceHolder { get; set; }

	public string GameSystem { get; set; }

	public bool IsMultiPart { get; set; }

	public string[] MultiPartGameFiles { get; set; }

	public Game()
	{
		MultiPartGameFiles = Array.Empty<string>();
	}

	public override bool CanDownload()
	{
		return CanDownloadAsSingleMedia();
	}

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		string providerId = ProviderIdsExtensions.GetProviderId((IHasProviderIds)(object)this, (MetadataProviders)1);
		if (!string.IsNullOrEmpty(providerId))
		{
			userDataKeys.Insert(0, "Game-Gamesdb-" + providerId);
		}
		return userDataKeys;
	}

	public override IEnumerable<FileSystemMetadata> GetDeletePaths()
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected Obj, but got Unknown
		if (!IsInMixedFolder)
		{
			return new FileSystemMetadata[1]
			{
				new FileSystemMetadata
				{
					FullName = BaseItem.FileSystem.GetDirectoryName(Path),
					IsDirectory = true
				}
			};
		}
		return base.GetDeletePaths();
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)4;
	}

	public GameInfo GetLookupInfo()
	{
		GameInfo itemLookupInfo = GetItemLookupInfo<GameInfo>();
		itemLookupInfo.GameSystem = GameSystem;
		return itemLookupInfo;
	}
}
