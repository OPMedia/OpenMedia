using System.Threading;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Library;

public interface IMetadataSaver
{
	string Name { get; }

	bool IsEnabledFor(BaseItem item, ItemUpdateType updateType);

	void Save(BaseItem item, CancellationToken cancellationToken);
}
