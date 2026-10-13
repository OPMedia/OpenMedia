using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Library;

public interface IDirectStreamProvider
{
	Task CopyToAsync(Stream stream, CancellationToken cancellationToken);
}
