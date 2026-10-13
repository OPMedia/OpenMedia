using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Sync;

namespace MediaBrowser.Controller.Sync;

public interface ISupportsDirectCopy
{
	Task<SyncedFileInfo> SendFile(SyncJob syncJob, string originalMediaPath, string inputPath, bool isMedia, string[] outputPathParts, SyncTarget target, IProgress<double> progress, CancellationToken cancellationToken);
}
