using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Playback/BitrateTest", "GET")]
public class GetBitrateTestBytes
{
	[ApiMember(Name = "Size", Description = "Size", IsRequired = true, DataType = "int", ParameterType = "query", Verb = "GET")]
	public long Size { get; set; }

	public GetBitrateTestBytes()
	{
		Size = 102400L;
	}
}
