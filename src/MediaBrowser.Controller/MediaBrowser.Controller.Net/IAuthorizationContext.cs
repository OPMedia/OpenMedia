using MediaBrowser.Model.Services;

namespace MediaBrowser.Controller.Net;

public interface IAuthorizationContext
{
	AuthorizationInfo GetAuthorizationInfo(object requestContext);

	AuthorizationInfo GetAuthorizationInfo(IRequest requestContext);
}
