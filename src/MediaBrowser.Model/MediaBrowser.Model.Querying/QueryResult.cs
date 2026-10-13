namespace MediaBrowser.Model.Querying;

public class QueryResult<T>
{
	public T[] Items { get; set; }

	public int TotalRecordCount { get; set; }

	public QueryResult()
	{
		Items = new T[0];
	}
}
