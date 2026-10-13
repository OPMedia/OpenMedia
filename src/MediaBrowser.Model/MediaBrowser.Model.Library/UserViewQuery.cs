using System;

namespace MediaBrowser.Model.Library;

public class UserViewQuery
{
	public Guid UserId { get; set; }

	public bool IncludeExternalContent { get; set; }

	public bool IncludeHidden { get; set; }

	public string[] PresetViews { get; set; }

	public UserViewQuery()
	{
		IncludeExternalContent = true;
		PresetViews = Array.Empty<string>();
	}
}
