using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Providers;

public class RemoteImageInfo
{
	public string ProviderName { get; set; }

	public string Url { get; set; }

	public string ThumbnailUrl { get; set; }

	public int? Height { get; set; }

	public int? Width { get; set; }

	public double? CommunityRating { get; set; }

	public int? VoteCount { get; set; }

	public string Language { get; set; }

	public ImageType Type { get; set; }

	public RatingType RatingType { get; set; }
}
