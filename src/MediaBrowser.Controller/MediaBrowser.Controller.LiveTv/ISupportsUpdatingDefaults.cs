using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.LiveTv;

public interface ISupportsUpdatingDefaults
{
	Task UpdateTimerDefaults(SeriesTimerInfo info, CancellationToken cancellationToken);
}
