namespace Emby.Server.MediaEncoding.Probing;

public class InternalMediaInfoResult
{
	public MediaStreamInfo[] streams { get; set; }

	public MediaFormatInfo format { get; set; }

	public MediaChapter[] Chapters { get; set; }
}
