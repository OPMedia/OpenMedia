using System;
using System.Collections.Generic;

namespace MediaBrowser.Controller.Providers;

public class AlbumInfo : ItemLookupInfo
{
	public string[] AlbumArtists { get; set; }

	public Dictionary<string, string> ArtistProviderIds { get; set; }

	public List<SongInfo> SongInfos { get; set; }

	public AlbumInfo()
	{
		ArtistProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		SongInfos = new List<SongInfo>();
		AlbumArtists = ItemLookupInfo.EmptyStringArray;
	}
}
