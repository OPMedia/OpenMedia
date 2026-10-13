using System.IO;

namespace Emby.Naming.Video;

public class VideoFileInfo
{
	public string Path { get; set; }

	public string Container { get; set; }

	public string Name { get; set; }

	public int? Year { get; set; }

	public string ExtraType { get; set; }

	public ExtraRule ExtraRule { get; set; }

	public string Format3D { get; set; }

	public bool Is3D { get; set; }

	public bool IsStub { get; set; }

	public string StubType { get; set; }

	public bool IsDirectory { get; set; }

	public string FileNameWithoutExtension
	{
		get
		{
			if (IsDirectory)
			{
				return System.IO.Path.GetFileName(Path);
			}
			return System.IO.Path.GetFileNameWithoutExtension(Path);
		}
	}

	public override string ToString()
	{
		return Name ?? base.ToString();
	}
}
