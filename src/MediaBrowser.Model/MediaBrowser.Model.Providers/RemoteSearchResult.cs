using System;
using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Providers;

public class RemoteSearchResult : IHasProviderIds
{
	public string Name { get; set; }

	public Dictionary<string, string> ProviderIds { get; set; }

	public int? ProductionYear { get; set; }

	public int? IndexNumber { get; set; }

	public int? IndexNumberEnd { get; set; }

	public int? ParentIndexNumber { get; set; }

	public DateTime? PremiereDate { get; set; }

	public string ImageUrl { get; set; }

	public string SearchProviderName { get; set; }

	public string GameSystem { get; set; }

	public string Overview { get; set; }

	public RemoteSearchResult AlbumArtist { get; set; }

	public RemoteSearchResult[] Artists { get; set; }

	public RemoteSearchResult()
	{
		ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Artists = new RemoteSearchResult[0];
	}
}
