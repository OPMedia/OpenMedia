using System.Net;

namespace MediaBrowser.Model.Services;

public interface IHttpResponse : IResponse
{
	void SetCookie(Cookie cookie);

	void ClearCookies();
}
