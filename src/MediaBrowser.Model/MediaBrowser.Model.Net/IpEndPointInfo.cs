using System.Globalization;

namespace MediaBrowser.Model.Net;

public class IpEndPointInfo
{
	public IpAddressInfo IpAddress { get; set; }

	public int Port { get; set; }

	public IpEndPointInfo()
	{
	}

	public IpEndPointInfo(IpAddressInfo address, int port)
	{
		IpAddress = address;
		Port = port;
	}

	public override string ToString()
	{
		return ((IpAddress == null) ? string.Empty : IpAddress.ToString()) + ":" + Port.ToString(CultureInfo.InvariantCulture);
	}
}
