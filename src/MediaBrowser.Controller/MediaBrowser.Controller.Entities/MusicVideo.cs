using System;
using System.IO;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class MusicVideo : Video, IHasArtist, IHasMusicGenres, IHasLookupInfo<MusicVideoInfo>
{
	[IgnoreDataMember]
	public string[] Artists { get; set; }

	[IgnoreDataMember]
	public string[] AllArtists => Artists;

	public MusicVideo()
	{
		Artists = Array.Empty<string>();
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)3;
	}

	public MusicVideoInfo GetLookupInfo()
	{
		MusicVideoInfo itemLookupInfo = GetItemLookupInfo<MusicVideoInfo>();
		itemLookupInfo.Artists = Artists;
		return itemLookupInfo;
	}

	public override bool BeforeMetadataRefresh(bool replaceAllMetdata)
	{
		bool result = base.BeforeMetadataRefresh(replaceAllMetdata);
		if (!ProductionYear.HasValue)
		{
			int? year = BaseItem.LibraryManager.ParseName(Name).Year;
			if (year.HasValue)
			{
				ProductionYear = year;
				result = true;
			}
			else if (!IsInMixedFolder)
			{
				year = BaseItem.LibraryManager.ParseName(System.IO.Path.GetFileName(ContainingFolderPath)).Year;
				if (year.HasValue)
				{
					ProductionYear = year;
					result = true;
				}
			}
		}
		return result;
	}
}
