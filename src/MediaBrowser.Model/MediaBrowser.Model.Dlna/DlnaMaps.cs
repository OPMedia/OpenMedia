namespace MediaBrowser.Model.Dlna;

public class DlnaMaps
{
	private static readonly string DefaultStreaming = FlagsToString(DlnaFlags.BackgroundTransferMode | DlnaFlags.ByteBasedSeek | DlnaFlags.ConnectionStall | DlnaFlags.DlnaV15 | DlnaFlags.StreamingTransferMode);

	private static readonly string DefaultInteractive = FlagsToString(DlnaFlags.BackgroundTransferMode | DlnaFlags.ByteBasedSeek | DlnaFlags.ConnectionStall | DlnaFlags.DlnaV15 | DlnaFlags.InteractiveTransferMode);

	public static string FlagsToString(DlnaFlags flags)
	{
		return $"{(ulong)flags:X8}{0:D24}";
	}

	public static string GetOrgOpValue(bool hasKnownRuntime, bool isDirectStream, TranscodeSeekInfo profileTranscodeSeekInfo)
	{
		if (hasKnownRuntime)
		{
			return string.Concat(string.Empty + (isDirectStream ? "0" : "1"), (isDirectStream || profileTranscodeSeekInfo == TranscodeSeekInfo.Bytes) ? "1" : "0");
		}
		return "00";
	}

	public static string GetImageOrgOpValue()
	{
		return string.Concat(string.Empty + "0", "1");
	}
}
