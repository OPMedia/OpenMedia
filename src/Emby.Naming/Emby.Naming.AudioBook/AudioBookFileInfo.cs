using System;

namespace Emby.Naming.AudioBook;

public class AudioBookFileInfo : IComparable<AudioBookFileInfo>
{
	public string Path { get; set; }

	public string Container { get; set; }

	public int? PartNumber { get; set; }

	public int? ChapterNumber { get; set; }

	public bool IsDirectory { get; set; }

	public int CompareTo(AudioBookFileInfo other)
	{
		if (this == other)
		{
			return 0;
		}
		if (other == null)
		{
			return 1;
		}
		int num = Nullable.Compare(ChapterNumber, other.ChapterNumber);
		if (num != 0)
		{
			return num;
		}
		int num2 = Nullable.Compare(PartNumber, other.PartNumber);
		if (num2 != 0)
		{
			return num2;
		}
		return string.Compare(Path, other.Path, StringComparison.Ordinal);
	}
}
