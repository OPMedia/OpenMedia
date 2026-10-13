using System.IO;

namespace MediaBrowser.Model.Net;

public interface ISocketFactory
{
	ISocket CreateUdpSocket(int localPort);

	ISocket CreateUdpBroadcastSocket(int localPort);

	ISocket CreateTcpSocket(IpAddressInfo remoteAddress, int remotePort);

	ISocket CreateSsdpUdpSocket(IpAddressInfo localIp, int localPort);

	ISocket CreateUdpMulticastSocket(string ipAddress, int multicastTimeToLive, int localPort);

	Stream CreateNetworkStream(ISocket socket, bool ownsSocket);
}
