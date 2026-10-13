using System;

namespace MediaBrowser.Model.Search;

public class SearchQuery
{
	public Guid UserId { get; set; }

	public string SearchTerm { get; set; }

	public int? StartIndex { get; set; }

	public int? Limit { get; set; }

	public bool IncludePeople { get; set; }

	public bool IncludeMedia { get; set; }

	public bool IncludeGenres { get; set; }

	public bool IncludeStudios { get; set; }

	public bool IncludeArtists { get; set; }

	public string[] MediaTypes { get; set; }

	public string[] IncludeItemTypes { get; set; }

	public string[] ExcludeItemTypes { get; set; }

	public string ParentId { get; set; }

	public bool? IsMovie { get; set; }

	public bool? IsSeries { get; set; }

	public bool? IsNews { get; set; }

	public bool? IsKids { get; set; }

	public bool? IsSports { get; set; }

	public SearchQuery()
	{
		IncludeArtists = true;
		IncludeGenres = true;
		IncludeMedia = true;
		IncludePeople = true;
		IncludeStudios = true;
		MediaTypes = Array.Empty<string>();
		IncludeItemTypes = Array.Empty<string>();
		ExcludeItemTypes = Array.Empty<string>();
	}
}
