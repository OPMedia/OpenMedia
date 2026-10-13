using MediaBrowser.Model.Services;

namespace MediaBrowser.Controller.Net;

public interface IHasResultFactory : IRequiresRequest
{
	IHttpResultFactory ResultFactory { get; set; }
}
