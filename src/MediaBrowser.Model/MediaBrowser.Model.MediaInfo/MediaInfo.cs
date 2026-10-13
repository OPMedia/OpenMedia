using System;
using System.Collections.Generic;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.MediaInfo;

public class MediaInfo : MediaSourceInfo, IHasProviderIds
{
	private static readonly string[] EmptyStringArray = Array.Empty<string>();

	public ChapterInfo[] Chapters { get; set; }

	public string Album { get; set; }

	public string[] Artists { get; set; }

	public string[] AlbumArtists { get; set; }

	public string[] Studios { get; set; }

	public string[] Genres { get; set; }

	public int? IndexNumber { get; set; }

	public int? ParentIndexNumber { get; set; }

	public int? ProductionYear { get; set; }

	public DateTime? PremiereDate { get; set; }

	public BaseItemPerson[] People { get; set; }

	public Dictionary<string, string> ProviderIds { get; set; }

	public string OfficialRating { get; set; }

	public string OfficialRatingDescription { get; set; }

	public string Overview { get; set; }

	public MediaInfo()
	{
		Chapters = new ChapterInfo[0];
		Artists = Array.Empty<string>();
		AlbumArtists = EmptyStringArray;
		Studios = Array.Empty<string>();
		Genres = Array.Empty<string>();
		People = new BaseItemPerson[0];
		ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}
}
