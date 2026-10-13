using System.Collections.Generic;
using MediaBrowser.Model.Sync;

namespace MediaBrowser.Controller.Sync;

public interface ISyncProvider
{
	string Name { get; }

	List<SyncTarget> GetSyncTargets(string userId);

	List<SyncTarget> GetAllSyncTargets();
}
