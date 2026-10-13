using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api.Progressive;

[Route("/Items/File", "GET", Summary = "Gets the original file of an item")]
[Authenticated(AllowLocalOnly = true)]
public class GetFile
{
	public string Id { get; set; }
}
