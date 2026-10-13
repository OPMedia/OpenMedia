using MediaBrowser.Model.Querying;

namespace MediaBrowser.Model.Playlists;

public class PlaylistItemQuery
{
	public string Id { get; set; }

	public string UserId { get; set; }

	public int? StartIndex { get; set; }

	public int? Limit { get; set; }

	public ItemFields[] Fields { get; set; }
}
