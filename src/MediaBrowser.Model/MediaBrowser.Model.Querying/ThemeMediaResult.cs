using System;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.Querying;

public class ThemeMediaResult : QueryResult<BaseItemDto>
{
	public Guid OwnerId { get; set; }
}
