namespace MediaBrowser.Model.Querying;

public class SimilarItemsQuery
{
	public string UserId { get; set; }

	public string Id { get; set; }

	public int? Limit { get; set; }

	public ItemFields[] Fields { get; set; }
}
