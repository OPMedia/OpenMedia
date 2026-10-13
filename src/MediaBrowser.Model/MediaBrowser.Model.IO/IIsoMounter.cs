using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Model.IO;

public interface IIsoMounter : IDisposable
{
	string Name { get; }

	Task<IIsoMount> Mount(string isoPath, CancellationToken cancellationToken);

	bool CanMount(string path);
}
