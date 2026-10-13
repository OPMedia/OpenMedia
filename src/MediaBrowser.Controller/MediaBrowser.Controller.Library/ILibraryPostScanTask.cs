using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Library;

public interface ILibraryPostScanTask
{
	Task Run(IProgress<double> progress, CancellationToken cancellationToken);
}
