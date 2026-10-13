using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Sync;

namespace MediaBrowser.Controller.Sync;

public interface IHasDynamicAccess
{
	Task<SyncedFileInfo> GetSyncedFileInfo(string id, SyncTarget target, CancellationToken cancellationToken);
}
