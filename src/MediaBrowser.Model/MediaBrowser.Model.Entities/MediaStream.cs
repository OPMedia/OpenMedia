using System;
using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Model.Entities;

public class MediaStream
{
	public string Codec { get; set; }

	public string CodecTag { get; set; }

	public string Language { get; set; }

	public string ColorTransfer { get; set; }

	public string ColorPrimaries { get; set; }

	public string ColorSpace { get; set; }

	public string Comment { get; set; }

	public string TimeBase { get; set; }

	public string CodecTimeBase { get; set; }

	public string Title { get; set; }

	public string VideoRange
	{
		get
		{
			if (Type != MediaStreamType.Video)
			{
				return null;
			}
			if (string.Equals(ColorTransfer, "smpte2084", StringComparison.OrdinalIgnoreCase))
			{
				return "HDR";
			}
			return "SDR";
		}
	}

	public string DisplayTitle
	{
		get
		{
			if (Type == MediaStreamType.Audio)
			{
				List<string> list = new List<string>();
				if (!string.IsNullOrEmpty(Language))
				{
					list.Add(Language.FirstToUpper());
				}
				if (!string.IsNullOrEmpty(Codec) && !StringHelper.EqualsIgnoreCase(Codec, "dca"))
				{
					list.Add(AudioCodec.GetFriendlyName(Codec));
				}
				else if (!string.IsNullOrEmpty(Profile) && !StringHelper.EqualsIgnoreCase(Profile, "lc"))
				{
					list.Add(Profile);
				}
				if (!string.IsNullOrEmpty(ChannelLayout))
				{
					list.Add(ChannelLayout);
				}
				else if (Channels.HasValue)
				{
					list.Add(Channels.Value.ToString(CultureInfo.InvariantCulture) + " ch");
				}
				if (IsDefault)
				{
					list.Add("Default");
				}
				return string.Join(" ", list.ToArray(list.Count));
			}
			if (Type == MediaStreamType.Video)
			{
				List<string> list2 = new List<string>();
				string resolutionText = GetResolutionText();
				if (!string.IsNullOrEmpty(resolutionText))
				{
					list2.Add(resolutionText);
				}
				if (!string.IsNullOrEmpty(Codec))
				{
					list2.Add(Codec.ToUpper());
				}
				return string.Join(" ", list2.ToArray(list2.Count));
			}
			if (Type == MediaStreamType.Subtitle)
			{
				List<string> list3 = new List<string>();
				if (!string.IsNullOrEmpty(Language))
				{
					list3.Add(Language.FirstToUpper());
				}
				else
				{
					list3.Add("Und");
				}
				if (IsDefault)
				{
					list3.Add("Default");
				}
				if (IsForced)
				{
					list3.Add("Forced");
				}
				return string.Join(" ", list3.ToArray(list3.Count));
			}
			_ = Type;
			_ = 1;
			return null;
		}
	}

	public string NalLengthSize { get; set; }

	public bool IsInterlaced { get; set; }

	public bool? IsAVC { get; set; }

	public string ChannelLayout { get; set; }

	public int? BitRate { get; set; }

	public int? BitDepth { get; set; }

	public int? RefFrames { get; set; }

	public int? PacketLength { get; set; }

	public int? Channels { get; set; }

	public int? SampleRate { get; set; }

	public bool IsDefault { get; set; }

	public bool IsForced { get; set; }

	public int? Height { get; set; }

	public int? Width { get; set; }

	public float? AverageFrameRate { get; set; }

	public float? RealFrameRate { get; set; }

	public string Profile { get; set; }

	public MediaStreamType Type { get; set; }

	public string AspectRatio { get; set; }

	public int Index { get; set; }

	public int? Score { get; set; }

	public bool IsExternal { get; set; }

	public SubtitleDeliveryMethod? DeliveryMethod { get; set; }

	public string DeliveryUrl { get; set; }

	public bool? IsExternalUrl { get; set; }

	public bool IsTextSubtitleStream
	{
		get
		{
			if (Type != MediaStreamType.Subtitle)
			{
				return false;
			}
			if (string.IsNullOrEmpty(Codec) && !IsExternal)
			{
				return false;
			}
			return IsTextFormat(Codec);
		}
	}

	public bool SupportsExternalStream { get; set; }

	public string Path { get; set; }

	public string PixelFormat { get; set; }

	public double? Level { get; set; }

	public bool? IsAnamorphic { get; set; }

	private string GetResolutionText()
	{
		if (Width.HasValue && Height.HasValue)
		{
			int value = Width.Value;
			int value2 = Height.Value;
			if (value >= 3800 || value2 >= 2000)
			{
				return "4K";
			}
			if (value >= 2500)
			{
				if (IsInterlaced)
				{
					return "1440I";
				}
				return "1440P";
			}
			if (value >= 1900 || value2 >= 1000)
			{
				if (IsInterlaced)
				{
					return "1080I";
				}
				return "1080P";
			}
			if (value >= 1260 || value2 >= 700)
			{
				if (IsInterlaced)
				{
					return "720I";
				}
				return "720P";
			}
			if (value >= 700 || value2 >= 440)
			{
				if (IsInterlaced)
				{
					return "480I";
				}
				return "480P";
			}
			return "SD";
		}
		return null;
	}

	private string AddLanguageIfNeeded(string title)
	{
		if (!string.IsNullOrEmpty(Language) && !string.Equals(Language, "und", StringComparison.OrdinalIgnoreCase) && !IsLanguageInTitle(title, Language))
		{
			title = Language.FirstToUpper() + " " + title;
		}
		return title;
	}

	private bool IsLanguageInTitle(string title, string language)
	{
		if (title.IndexOf(Language, StringComparison.OrdinalIgnoreCase) != -1)
		{
			return true;
		}
		return false;
	}

	public static bool IsTextFormat(string format)
	{
		string text = format ?? string.Empty;
		if (text.IndexOf("pgs", StringComparison.OrdinalIgnoreCase) == -1 && text.IndexOf("dvd", StringComparison.OrdinalIgnoreCase) == -1 && text.IndexOf("dvbsub", StringComparison.OrdinalIgnoreCase) == -1 && !StringHelper.EqualsIgnoreCase(text, "sub"))
		{
			return !StringHelper.EqualsIgnoreCase(text, "dvb_subtitle");
		}
		return false;
	}

	public bool SupportsSubtitleConversionTo(string toCodec)
	{
		if (!IsTextSubtitleStream)
		{
			return false;
		}
		string codec = Codec;
		if (StringHelper.EqualsIgnoreCase(codec, "ass"))
		{
			return false;
		}
		if (StringHelper.EqualsIgnoreCase(codec, "ssa"))
		{
			return false;
		}
		if (StringHelper.EqualsIgnoreCase(toCodec, "ass"))
		{
			return false;
		}
		if (StringHelper.EqualsIgnoreCase(toCodec, "ssa"))
		{
			return false;
		}
		return true;
	}
}
