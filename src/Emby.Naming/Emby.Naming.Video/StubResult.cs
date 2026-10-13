using System.Collections.Generic;

namespace Emby.Naming.Video;

public class StubResult
{
	public bool IsStub { get; set; }

	public string StubType { get; set; }

	public List<string> Tokens { get; set; }

	public StubResult()
	{
		Tokens = new List<string>();
	}
}
