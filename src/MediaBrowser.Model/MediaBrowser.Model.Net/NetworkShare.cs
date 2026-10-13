namespace MediaBrowser.Model.Net;

public class NetworkShare
{
	public string Server { get; set; }

	public string Name { get; set; }

	public string Path { get; set; }

	public NetworkShareType ShareType { get; set; }

	public string Remark { get; set; }
}
