namespace MediaBrowser.Model.Querying;

public class ItemCountsQuery
{
	public string UserId { get; set; }

	public bool? IsFavorite { get; set; }
}
