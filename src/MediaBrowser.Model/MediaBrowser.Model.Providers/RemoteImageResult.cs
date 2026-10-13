namespace MediaBrowser.Model.Providers;

public class RemoteImageResult
{
	public RemoteImageInfo[] Images { get; set; }

	public int TotalRecordCount { get; set; }

	public string[] Providers { get; set; }
}
