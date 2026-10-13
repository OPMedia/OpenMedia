using System.Threading.Tasks;
using MediaBrowser.Model.LiveTv;

namespace MediaBrowser.Controller.LiveTv;

public interface IConfigurableTunerHost
{
	Task Validate(TunerHostInfo info);
}
