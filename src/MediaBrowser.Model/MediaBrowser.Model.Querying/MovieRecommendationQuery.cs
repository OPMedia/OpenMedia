namespace MediaBrowser.Model.Querying;

public class MovieRecommendationQuery
{
	public string UserId { get; set; }

	public string ParentId { get; set; }

	public int ItemLimit { get; set; }

	public int CategoryLimit { get; set; }

	public ItemFields[] Fields { get; set; }

	public MovieRecommendationQuery()
	{
		ItemLimit = 10;
		CategoryLimit = 6;
		Fields = new ItemFields[0];
	}
}
