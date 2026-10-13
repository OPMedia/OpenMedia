using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Model.IO;

public interface IIsoManager : IDisposable
{
	Task<IIsoMount> Mount(string isoPath, CancellationToken cancellationToken);

	bool CanMount(string path);

	void AddParts(IEnumerable<IIsoMounter> mounters);
}
