namespace MediaBrowser.Model.Net;

public sealed class SocketReceiveResult
{
	public byte[] Buffer { get; set; }

	public int ReceivedBytes { get; set; }

	public IpEndPointInfo RemoteEndPoint { get; set; }

	public IpAddressInfo LocalIPAddress { get; set; }
}
