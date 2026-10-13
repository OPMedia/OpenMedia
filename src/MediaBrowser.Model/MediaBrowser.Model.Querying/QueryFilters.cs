using System;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.Querying;

public class QueryFilters
{
	public NameGuidPair[] Genres { get; set; }

	public string[] Tags { get; set; }

	public QueryFilters()
	{
		Tags = Array.Empty<string>();
		Genres = new NameGuidPair[0];
	}
}
