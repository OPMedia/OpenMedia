namespace MediaBrowser.Model.MediaInfo;

public class AudioCodec
{
	public const string AAC = "aac";

	public const string MP3 = "mp3";

	public const string AC3 = "ac3";

	public static string GetFriendlyName(string codec)
	{
		if (string.IsNullOrEmpty(codec))
		{
			return "";
		}
		return codec.ToLower() switch
		{
			"ac3" => "Dolby Digital", 
			"eac3" => "Dolby Digital+", 
			"dca" => "DTS", 
			_ => codec.ToUpper(), 
		};
	}
}
