using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Sync;

namespace MediaBrowser.Controller.Sync;

public interface IServerSyncProvider : ISyncProvider
{
	Task<SyncedFileInfo> SendFile(SyncJob syncJob, string originalMediaPath, Stream inputStream, bool isMedia, string[] outputPathParts, SyncTarget target, IProgress<double> progress, CancellationToken cancellationToken);

	Task<QueryResult<FileSystemMetadata>> GetFiles(string[] directoryPathParts, SyncTarget target, CancellationToken cancellationToken);
}
