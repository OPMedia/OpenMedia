using System;
using System.Collections.Generic;
using MediaBrowser.Model.LiveTv;

namespace MediaBrowser.Controller.LiveTv;

public class ProgramInfo
{
	public string Id { get; set; }

	public string ChannelId { get; set; }

	public string Name { get; set; }

	public string OfficialRating { get; set; }

	public string Overview { get; set; }

	public string ShortOverview { get; set; }

	public DateTime StartDate { get; set; }

	public DateTime EndDate { get; set; }

	public List<string> Genres { get; set; }

	public DateTime? OriginalAirDate { get; set; }

	public bool? IsHD { get; set; }

	public bool? Is3D { get; set; }

	public ProgramAudio? Audio { get; set; }

	public float? CommunityRating { get; set; }

	public bool IsRepeat { get; set; }

	public bool IsSubjectToBlackout { get; set; }

	public string EpisodeTitle { get; set; }

	public string ImagePath { get; set; }

	public string ImageUrl { get; set; }

	public string ThumbImageUrl { get; set; }

	public string LogoImageUrl { get; set; }

	public string BackdropImageUrl { get; set; }

	public bool? HasImage { get; set; }

	public bool IsMovie { get; set; }

	public bool IsSports { get; set; }

	public bool IsSeries { get; set; }

	public bool IsLive { get; set; }

	public bool IsNews { get; set; }

	public bool IsKids { get; set; }

	public bool IsEducational { get; set; }

	public bool IsPremiere { get; set; }

	public int? ProductionYear { get; set; }

	public string HomePageUrl { get; set; }

	public string SeriesId { get; set; }

	public string ShowId { get; set; }

	public int? SeasonNumber { get; set; }

	public int? EpisodeNumber { get; set; }

	public string Etag { get; set; }

	public Dictionary<string, string> ProviderIds { get; set; }

	public Dictionary<string, string> SeriesProviderIds { get; set; }

	public ProgramInfo()
	{
		Genres = new List<string>();
		ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		SeriesProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}
}
