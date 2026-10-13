using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Model.Net;

public interface ISocket : IDisposable
{
	IpAddressInfo LocalIPAddress { get; }

	Task<SocketReceiveResult> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken);

	int Receive(byte[] buffer, int offset, int count);

	IAsyncResult BeginReceive(byte[] buffer, int offset, int count, AsyncCallback callback);

	SocketReceiveResult EndReceive(IAsyncResult result);

	Task SendToAsync(byte[] buffer, int offset, int bytes, IpEndPointInfo endPoint, CancellationToken cancellationToken);
}
