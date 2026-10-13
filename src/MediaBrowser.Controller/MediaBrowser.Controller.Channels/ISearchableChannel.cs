using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Channels;

public interface ISearchableChannel
{
	Task<IEnumerable<ChannelItemInfo>> Search(ChannelSearchInfo searchInfo, CancellationToken cancellationToken);
}
