using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.LiveTv;

public interface ISupportsNewTimerIds
{
	Task<string> CreateTimer(TimerInfo info, CancellationToken cancellationToken);

	Task<string> CreateSeriesTimer(SeriesTimerInfo info, CancellationToken cancellationToken);
}
