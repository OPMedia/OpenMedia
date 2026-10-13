using System.Collections.Generic;

namespace Emby.Naming.Video;

public class Format3DResult
{
	public bool Is3D { get; set; }

	public string Format3D { get; set; }

	public List<string> Tokens { get; set; }

	public Format3DResult()
	{
		Tokens = new List<string>();
	}
}
