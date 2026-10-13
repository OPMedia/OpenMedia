using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;

namespace Emby.Server.MediaEncoding.Probing;

public class ProbeResultNormalizer
{
	private readonly CultureInfo _usCulture = new CultureInfo("en-US");

	private readonly ILogger _logger;

	private readonly IFileSystem _fileSystem;

	private string[] EmbeddedImageTypes = new string[4] { "mjpeg", "gif", "png", "bmp" };

	private readonly char[] _nameDelimiters = new char[4] { '/', '|', ';', '\\' };

	private const string ArtistReplaceValue = " | ";

	private string[] _splitWhiteList;

	private const int MaxSubtitleDescriptionExtractionLength = 100;

	public ProbeResultNormalizer(ILogger logger, IFileSystem fileSystem)
	{
		_logger = logger;
		_fileSystem = fileSystem;
	}

	public MediaInfo GetMediaInfo(InternalMediaInfoResult data, VideoType videoType, bool isAudio, string path, MediaProtocol protocol)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Expected Obj, but got Unknown
		//IL_04f7: Unknown result type (might be due to invalid IL or missing references)
		MediaInfo val = new MediaInfo
		{
			Path = path,
			Protocol = protocol
		};
		FFProbeHelpers.NormalizeFFProbeResult(data);
		SetSize(data, val);
		MediaStreamInfo[] source = data.streams ?? new MediaStreamInfo[0];
		((MediaSourceInfo)val).MediaStreams = (from s in source
			select GetMediaStream(isAudio, s, data.format) into i
			where i != null
			select i).Where((MediaStream i) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Invalid comparison between Unknown and I4
			return (int)i.Type != 2 || !string.IsNullOrWhiteSpace(i.Codec);
		}).ToList();
		if (data.format != null)
		{
			((MediaSourceInfo)val).Container = NormalizeFormat(data.format.format_name);
			if (!string.IsNullOrEmpty(data.format.bit_rate) && int.TryParse(data.format.bit_rate, NumberStyles.Any, _usCulture, out var result))
			{
				((MediaSourceInfo)val).Bitrate = result;
			}
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string tagStreamType = (isAudio ? "audio" : "video");
		if (data.streams != null)
		{
			MediaStreamInfo mediaStreamInfo = data.streams.FirstOrDefault((MediaStreamInfo i) => string.Equals(i.codec_type, tagStreamType, StringComparison.OrdinalIgnoreCase));
			if (mediaStreamInfo != null && mediaStreamInfo.tags != null)
			{
				foreach (KeyValuePair<string, string> tag in mediaStreamInfo.tags)
				{
					dictionary[tag.Key] = tag.Value;
				}
			}
		}
		if (data.format != null && data.format.tags != null)
		{
			foreach (KeyValuePair<string, string> tag2 in data.format.tags)
			{
				dictionary[tag2.Key] = tag2.Value;
			}
		}
		FetchGenres(val, dictionary);
		string dictionaryValue = FFProbeHelpers.GetDictionaryValue(dictionary, "synopsis");
		if (string.IsNullOrWhiteSpace(dictionaryValue))
		{
			dictionaryValue = FFProbeHelpers.GetDictionaryValue(dictionary, "description");
		}
		if (string.IsNullOrWhiteSpace(dictionaryValue))
		{
			dictionaryValue = FFProbeHelpers.GetDictionaryValue(dictionary, "desc");
		}
		if (!string.IsNullOrWhiteSpace(dictionaryValue))
		{
			val.Overview = dictionaryValue;
		}
		string dictionaryValue2 = FFProbeHelpers.GetDictionaryValue(dictionary, "title");
		if (!string.IsNullOrWhiteSpace(dictionaryValue2))
		{
			((MediaSourceInfo)val).Name = dictionaryValue2;
		}
		val.ProductionYear = FFProbeHelpers.GetDictionaryNumericValue(dictionary, "date");
		val.PremiereDate = FFProbeHelpers.GetDictionaryDateTime(dictionary, "retaildate") ?? FFProbeHelpers.GetDictionaryDateTime(dictionary, "retail date") ?? FFProbeHelpers.GetDictionaryDateTime(dictionary, "retail_date") ?? FFProbeHelpers.GetDictionaryDateTime(dictionary, "date");
		if (isAudio)
		{
			SetAudioRuntimeTicks(data, val);
			SetAudioInfoFromTags(val, dictionary);
			foreach (MediaStream mediaStream in ((MediaSourceInfo)val).MediaStreams)
			{
				if (!mediaStream.BitRate.HasValue)
				{
					mediaStream.BitRate = ((MediaSourceInfo)val).Bitrate;
				}
			}
		}
		else
		{
			string dictionaryValue3 = FFProbeHelpers.GetDictionaryValue(dictionary, "iTunEXTC");
			if (!string.IsNullOrWhiteSpace(dictionaryValue3))
			{
				string[] array = dictionaryValue3.Split(new char[1] { '|' }, StringSplitOptions.RemoveEmptyEntries);
				if (array.Length > 1)
				{
					val.OfficialRating = array[1];
					if (array.Length > 3)
					{
						val.OfficialRatingDescription = array[3];
					}
				}
			}
			string dictionaryValue4 = FFProbeHelpers.GetDictionaryValue(dictionary, "iTunMOVI");
			if (!string.IsNullOrWhiteSpace(dictionaryValue4))
			{
				FetchFromItunesInfo(dictionaryValue4, val);
			}
			if (val.Studios.Length == 0)
			{
				FetchStudios(val, dictionary, "copyright");
			}
			if (data.format != null && !string.IsNullOrEmpty(data.format.duration))
			{
				((MediaSourceInfo)val).RunTimeTicks = TimeSpan.FromSeconds(double.Parse(data.format.duration, _usCulture)).Ticks;
			}
			FetchWtvInfo(val, data);
			if (data.Chapters != null)
			{
				val.Chapters = data.Chapters.Select(GetChapterInfo).ToArray();
			}
			ExtractTimestamp(val);
			if (string.Equals(GetDictionaryValue(dictionary, "stereo_mode"), "left_right", StringComparison.OrdinalIgnoreCase))
			{
				((MediaSourceInfo)val).Video3DFormat = (Video3DFormat)1;
			}
			foreach (MediaStream mediaStream2 in ((MediaSourceInfo)val).MediaStreams)
			{
				if ((int)mediaStream2.Type == 0 && !mediaStream2.BitRate.HasValue)
				{
					mediaStream2.BitRate = GetEstimatedAudioBitrate(mediaStream2.Codec, mediaStream2.Channels);
				}
			}
			((MediaSourceInfo)val).InferTotalBitrate(false);
		}
		return val;
	}

	private string NormalizeFormat(string format)
	{
		if (string.IsNullOrWhiteSpace(format))
		{
			return null;
		}
		if (string.Equals(format, "mpegvideo", StringComparison.OrdinalIgnoreCase))
		{
			return "mpeg";
		}
		format = StringHelper.Replace(format, "matroska", "mkv", StringComparison.OrdinalIgnoreCase);
		return format;
	}

	private int? GetEstimatedAudioBitrate(string codec, int? channels)
	{
		if (!channels.HasValue)
		{
			return null;
		}
		int value = channels.Value;
		if (string.Equals(codec, "aac", StringComparison.OrdinalIgnoreCase) || string.Equals(codec, "mp3", StringComparison.OrdinalIgnoreCase))
		{
			if (value <= 2)
			{
				return 192000;
			}
			if (value >= 5)
			{
				return 320000;
			}
		}
		return null;
	}

	private void FetchFromItunesInfo(string xml, MediaInfo info)
	{
		int num = xml.IndexOf("<plist", StringComparison.OrdinalIgnoreCase);
		if (num != -1)
		{
			xml = xml.Substring(num);
		}
		xml = "<?xml version=\"1.0\"?>" + xml;
		using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
		using StreamReader input = new StreamReader(stream);
		try
		{
			using XmlReader xmlReader = XmlReader.Create(input);
			xmlReader.MoveToContent();
			xmlReader.Read();
			while (!xmlReader.EOF && xmlReader.ReadState == ReadState.Interactive)
			{
				if (xmlReader.NodeType == XmlNodeType.Element)
				{
					string name = xmlReader.Name;
					if (name == "dict")
					{
						if (xmlReader.IsEmptyElement)
						{
							xmlReader.Read();
							continue;
						}
						using XmlReader reader = xmlReader.ReadSubtree();
						ReadFromDictNode(reader, info);
					}
					else
					{
						xmlReader.Skip();
					}
				}
				else
				{
					xmlReader.Read();
				}
			}
		}
		catch (XmlException)
		{
		}
	}

	private void ReadFromDictNode(XmlReader reader, MediaInfo info)
	{
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected Obj, but got Unknown
		string text = null;
		List<NameValuePair> list = new List<NameValuePair>();
		reader.MoveToContent();
		reader.Read();
		while (!reader.EOF && reader.ReadState == ReadState.Interactive)
		{
			if (reader.NodeType == XmlNodeType.Element)
			{
				switch (reader.Name)
				{
				case "key":
					if (!string.IsNullOrWhiteSpace(text))
					{
						ProcessPairs(text, list, info);
					}
					text = reader.ReadElementContentAsString();
					list = new List<NameValuePair>();
					break;
				case "string":
				{
					string text2 = reader.ReadElementContentAsString();
					if (!string.IsNullOrWhiteSpace(text2))
					{
						list.Add(new NameValuePair
						{
							Name = text2,
							Value = text2
						});
					}
					break;
				}
				case "array":
				{
					if (reader.IsEmptyElement)
					{
						reader.Read();
						break;
					}
					using (XmlReader reader2 = reader.ReadSubtree())
					{
						if (!string.IsNullOrWhiteSpace(text))
						{
							list.AddRange(ReadValueArray(reader2));
						}
					}
					break;
				}
				default:
					reader.Skip();
					break;
				}
			}
			else
			{
				reader.Read();
			}
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			ProcessPairs(text, list, info);
		}
	}

	private List<NameValuePair> ReadValueArray(XmlReader reader)
	{
		List<NameValuePair> list = new List<NameValuePair>();
		reader.MoveToContent();
		reader.Read();
		while (!reader.EOF && reader.ReadState == ReadState.Interactive)
		{
			if (reader.NodeType == XmlNodeType.Element)
			{
				string name = reader.Name;
				if (name == "dict")
				{
					if (reader.IsEmptyElement)
					{
						reader.Read();
						continue;
					}
					using XmlReader reader2 = reader.ReadSubtree();
					NameValuePair nameValuePair = GetNameValuePair(reader2);
					if (nameValuePair != null)
					{
						list.Add(nameValuePair);
					}
				}
				else
				{
					reader.Skip();
				}
			}
			else
			{
				reader.Read();
			}
		}
		return list;
	}

	private void ProcessPairs(string key, List<NameValuePair> pairs, MediaInfo info)
	{
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Expected Obj, but got Unknown
		//IL_0151: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Unknown result type (might be due to invalid IL or missing references)
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_0173: Expected Obj, but got Unknown
		if (string.Equals(key, "studio", StringComparison.OrdinalIgnoreCase))
		{
			List<string> list = info.Studios.ToList();
			foreach (NameValuePair pair in pairs)
			{
				list.Add(pair.Value);
			}
			info.Studios = list.Where((string i) => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		}
		else
		{
			if (string.Equals(key, "screenwriters", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "producers", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (string.Equals(key, "directors", StringComparison.OrdinalIgnoreCase))
			{
				List<BaseItemPerson> list2 = info.People.ToList();
				foreach (NameValuePair pair2 in pairs)
				{
					list2.Add(new BaseItemPerson
					{
						Name = pair2.Value,
						Type = "Director"
					});
				}
				info.People = list2.ToArray();
			}
			else
			{
				if (!string.Equals(key, "cast", StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
				List<BaseItemPerson> list3 = info.People.ToList();
				foreach (NameValuePair pair3 in pairs)
				{
					list3.Add(new BaseItemPerson
					{
						Name = pair3.Value,
						Type = "Actor"
					});
				}
				info.People = list3.ToArray();
			}
		}
	}

	private NameValuePair GetNameValuePair(XmlReader reader)
	{
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Expected Obj, but got Unknown
		string text = null;
		string value = null;
		reader.MoveToContent();
		reader.Read();
		while (!reader.EOF && reader.ReadState == ReadState.Interactive)
		{
			if (reader.NodeType == XmlNodeType.Element)
			{
				string name = reader.Name;
				if (!(name == "key"))
				{
					if (name == "string")
					{
						value = reader.ReadElementContentAsString();
					}
					else
					{
						reader.Skip();
					}
				}
				else
				{
					text = reader.ReadElementContentAsString();
				}
			}
			else
			{
				reader.Read();
			}
		}
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		return new NameValuePair
		{
			Name = text,
			Value = value
		};
	}

	private string NormalizeSubtitleCodec(string codec)
	{
		if (string.Equals(codec, "dvb_subtitle", StringComparison.OrdinalIgnoreCase))
		{
			codec = "dvbsub";
		}
		else if ((codec ?? string.Empty).IndexOf("PGS", StringComparison.OrdinalIgnoreCase) != -1)
		{
			codec = "PGSSUB";
		}
		else if ((codec ?? string.Empty).IndexOf("DVD", StringComparison.OrdinalIgnoreCase) != -1)
		{
			codec = "DVDSUB";
		}
		return codec;
	}

	private MediaStream GetMediaStream(bool isAudio, MediaStreamInfo streamInfo, MediaFormatInfo formatInfo)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Expected Obj, but got Unknown
		//IL_0411: Unknown result type (might be due to invalid IL or missing references)
		//IL_0417: Invalid comparison between Unknown and I4
		string.Equals(streamInfo.codec_name, "mov_text", StringComparison.OrdinalIgnoreCase);
		MediaStream val = new MediaStream
		{
			Codec = streamInfo.codec_name,
			Profile = streamInfo.profile,
			Level = streamInfo.level,
			Index = streamInfo.index,
			PixelFormat = streamInfo.pix_fmt,
			NalLengthSize = streamInfo.nal_length_size,
			TimeBase = streamInfo.time_base,
			CodecTimeBase = streamInfo.codec_time_base,
			ColorPrimaries = streamInfo.color_primaries,
			ColorSpace = streamInfo.color_space,
			ColorTransfer = streamInfo.color_transfer
		};
		if (string.Equals(streamInfo.is_avc, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(streamInfo.is_avc, "1", StringComparison.OrdinalIgnoreCase))
		{
			val.IsAVC = true;
		}
		else if (string.Equals(streamInfo.is_avc, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(streamInfo.is_avc, "0", StringComparison.OrdinalIgnoreCase))
		{
			val.IsAVC = false;
		}
		if (!string.IsNullOrWhiteSpace(streamInfo.field_order) && !string.Equals(streamInfo.field_order, "progressive", StringComparison.OrdinalIgnoreCase))
		{
			val.IsInterlaced = true;
		}
		if (!string.IsNullOrWhiteSpace(streamInfo.codec_tag_string) && streamInfo.codec_tag_string.IndexOf("[0]", StringComparison.OrdinalIgnoreCase) == -1)
		{
			val.CodecTag = streamInfo.codec_tag_string;
		}
		if (streamInfo.tags != null)
		{
			val.Language = GetDictionaryValue(streamInfo.tags, "language");
			val.Comment = GetDictionaryValue(streamInfo.tags, "comment");
			val.Title = GetDictionaryValue(streamInfo.tags, "title");
		}
		if (string.Equals(streamInfo.codec_type, "audio", StringComparison.OrdinalIgnoreCase))
		{
			val.Type = (MediaStreamType)0;
			val.Channels = streamInfo.channels;
			if (!string.IsNullOrEmpty(streamInfo.sample_rate) && int.TryParse(streamInfo.sample_rate, NumberStyles.Any, _usCulture, out var result))
			{
				val.SampleRate = result;
			}
			val.ChannelLayout = ParseChannelLayout(streamInfo.channel_layout);
			if (streamInfo.bits_per_sample > 0)
			{
				val.BitDepth = streamInfo.bits_per_sample;
			}
			else if (streamInfo.bits_per_raw_sample > 0)
			{
				val.BitDepth = streamInfo.bits_per_raw_sample;
			}
		}
		else if (string.Equals(streamInfo.codec_type, "subtitle", StringComparison.OrdinalIgnoreCase))
		{
			val.Type = (MediaStreamType)2;
			val.Codec = NormalizeSubtitleCodec(val.Codec);
		}
		else
		{
			if (!string.Equals(streamInfo.codec_type, "video", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			val.Type = (MediaStreamType)((!isAudio && !EmbeddedImageTypes.Contains(val.Codec ?? string.Empty)) ? 1 : 3);
			val.AverageFrameRate = GetFrameRate(streamInfo.avg_frame_rate);
			val.RealFrameRate = GetFrameRate(streamInfo.r_frame_rate);
			if (string.Equals(val.Codec, "mjpeg", StringComparison.OrdinalIgnoreCase))
			{
				if (!string.IsNullOrWhiteSpace(val.CodecTag))
				{
					val.Type = (MediaStreamType)1;
				}
				else
				{
					val.Type = (MediaStreamType)3;
				}
			}
			if (streamInfo.width > 0 && streamInfo.height > 0)
			{
				val.Width = streamInfo.width;
				val.Height = streamInfo.height;
			}
			val.AspectRatio = GetAspectRatio(streamInfo);
			if (streamInfo.bits_per_sample > 0)
			{
				val.BitDepth = streamInfo.bits_per_sample;
			}
			else if (streamInfo.bits_per_raw_sample > 0)
			{
				val.BitDepth = streamInfo.bits_per_raw_sample;
			}
			val.IsAnamorphic = string.Equals(streamInfo.sample_aspect_ratio, "0:1", StringComparison.OrdinalIgnoreCase);
			if (streamInfo.refs > 0)
			{
				val.RefFrames = streamInfo.refs;
			}
		}
		int num = 0;
		if (!string.IsNullOrEmpty(streamInfo.bit_rate) && int.TryParse(streamInfo.bit_rate, NumberStyles.Any, _usCulture, out var result2))
		{
			num = result2;
		}
		if (num == 0 && formatInfo != null && !string.IsNullOrEmpty(formatInfo.bit_rate) && (int)val.Type == 1 && int.TryParse(formatInfo.bit_rate, NumberStyles.Any, _usCulture, out var result3))
		{
			num = result3;
		}
		if (num > 0)
		{
			val.BitRate = num;
		}
		if (streamInfo.disposition != null)
		{
			string dictionaryValue = GetDictionaryValue(streamInfo.disposition, "default");
			string dictionaryValue2 = GetDictionaryValue(streamInfo.disposition, "forced");
			val.IsDefault = string.Equals(dictionaryValue, "1", StringComparison.OrdinalIgnoreCase);
			val.IsForced = string.Equals(dictionaryValue2, "1", StringComparison.OrdinalIgnoreCase);
		}
		NormalizeStreamTitle(val);
		return val;
	}

	private void NormalizeStreamTitle(MediaStream stream)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Invalid comparison between Unknown and I4
		if (string.Equals(stream.Title, "cc", StringComparison.OrdinalIgnoreCase))
		{
			stream.Title = null;
		}
		if ((int)stream.Type == 3)
		{
			stream.Title = null;
		}
	}

	private string GetDictionaryValue(Dictionary<string, string> tags, string key)
	{
		if (tags == null)
		{
			return null;
		}
		tags.TryGetValue(key, out var value);
		return value;
	}

	private string ParseChannelLayout(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return input;
		}
		return input.Split('(').FirstOrDefault();
	}

	private string GetAspectRatio(MediaStreamInfo info)
	{
		string display_aspect_ratio = info.display_aspect_ratio;
		string[] array = (display_aspect_ratio ?? string.Empty).Split(':');
		if (array.Length != 2 || !int.TryParse(array[0], NumberStyles.Any, _usCulture, out var result) || !int.TryParse(array[1], NumberStyles.Any, _usCulture, out var result2) || result <= 0 || result2 <= 0)
		{
			result = info.width;
			result2 = info.height;
		}
		if (result > 0 && result2 > 0)
		{
			double num = result;
			num /= (double)result2;
			if (IsClose(num, 1.777777778, 0.03))
			{
				return "16:9";
			}
			if (IsClose(num, 1.3333333333, 0.05))
			{
				return "4:3";
			}
			if (IsClose(num, 1.41))
			{
				return "1.41:1";
			}
			if (IsClose(num, 1.5))
			{
				return "1.5:1";
			}
			if (IsClose(num, 1.6))
			{
				return "1.6:1";
			}
			if (IsClose(num, 1.66666666667))
			{
				return "5:3";
			}
			if (IsClose(num, 1.85, 0.02))
			{
				return "1.85:1";
			}
			if (IsClose(num, 2.35, 0.025))
			{
				return "2.35:1";
			}
			if (IsClose(num, 2.4, 0.025))
			{
				return "2.40:1";
			}
		}
		if (string.Equals(display_aspect_ratio, "0:0", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return display_aspect_ratio;
	}

	private bool IsClose(double d1, double d2, double variance = 0.005)
	{
		return Math.Abs(d1 - d2) <= variance;
	}

	private float? GetFrameRate(string value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			string[] array = value.Split('/');
			float num = ((array.Length != 2) ? float.Parse(array[0], _usCulture) : (float.Parse(array[0], _usCulture) / float.Parse(array[1], _usCulture)));
			if (float.IsNaN(num))
			{
				return null;
			}
			if (float.IsInfinity(num))
			{
				return null;
			}
			return num;
		}
		return null;
	}

	private void SetAudioRuntimeTicks(InternalMediaInfoResult result, MediaInfo data)
	{
		if (result.streams == null)
		{
			return;
		}
		MediaStreamInfo mediaStreamInfo = result.streams.FirstOrDefault((MediaStreamInfo s) => string.Equals(s.codec_type, "audio", StringComparison.OrdinalIgnoreCase));
		if (mediaStreamInfo != null)
		{
			string duration = mediaStreamInfo.duration;
			if (string.IsNullOrEmpty(duration))
			{
				duration = result.format.duration;
			}
			if (!string.IsNullOrEmpty(duration))
			{
				((MediaSourceInfo)data).RunTimeTicks = TimeSpan.FromSeconds(double.Parse(duration, _usCulture)).Ticks;
			}
		}
	}

	private void SetSize(InternalMediaInfoResult data, MediaInfo info)
	{
		if (data.format != null)
		{
			if (!string.IsNullOrEmpty(data.format.size))
			{
				((MediaSourceInfo)info).Size = long.Parse(data.format.size, _usCulture);
			}
			else
			{
				((MediaSourceInfo)info).Size = null;
			}
		}
	}

	private void SetAudioInfoFromTags(MediaInfo audio, Dictionary<string, string> tags)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Expected Obj, but got Unknown
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Expected Obj, but got Unknown
		List<BaseItemPerson> list = audio.People.ToList();
		string dictionaryValue = FFProbeHelpers.GetDictionaryValue(tags, "composer");
		if (!string.IsNullOrWhiteSpace(dictionaryValue))
		{
			foreach (string item in Split(dictionaryValue, allowCommaDelimiter: false))
			{
				list.Add(new BaseItemPerson
				{
					Name = item,
					Type = "Composer"
				});
			}
		}
		string dictionaryValue2 = FFProbeHelpers.GetDictionaryValue(tags, "writer");
		if (!string.IsNullOrWhiteSpace(dictionaryValue2))
		{
			foreach (string item2 in Split(dictionaryValue2, allowCommaDelimiter: false))
			{
				list.Add(new BaseItemPerson
				{
					Name = item2,
					Type = "Writer"
				});
			}
		}
		audio.People = list.ToArray();
		audio.Album = FFProbeHelpers.GetDictionaryValue(tags, "album");
		string dictionaryValue3 = FFProbeHelpers.GetDictionaryValue(tags, "artists");
		if (!string.IsNullOrWhiteSpace(dictionaryValue3))
		{
			audio.Artists = NameExtensions.DistinctNames(SplitArtists(dictionaryValue3, new char[2] { '/', ';' }, splitFeaturing: false)).ToArray();
		}
		else
		{
			string dictionaryValue4 = FFProbeHelpers.GetDictionaryValue(tags, "artist");
			if (string.IsNullOrWhiteSpace(dictionaryValue4))
			{
				audio.Artists = Array.Empty<string>();
			}
			else
			{
				audio.Artists = NameExtensions.DistinctNames(SplitArtists(dictionaryValue4, _nameDelimiters, splitFeaturing: true)).ToArray();
			}
		}
		string dictionaryValue5 = FFProbeHelpers.GetDictionaryValue(tags, "albumartist");
		if (string.IsNullOrWhiteSpace(dictionaryValue5))
		{
			dictionaryValue5 = FFProbeHelpers.GetDictionaryValue(tags, "album artist");
		}
		if (string.IsNullOrWhiteSpace(dictionaryValue5))
		{
			dictionaryValue5 = FFProbeHelpers.GetDictionaryValue(tags, "album_artist");
		}
		if (string.IsNullOrWhiteSpace(dictionaryValue5))
		{
			audio.AlbumArtists = Array.Empty<string>();
		}
		else
		{
			audio.AlbumArtists = NameExtensions.DistinctNames(SplitArtists(dictionaryValue5, _nameDelimiters, splitFeaturing: true)).ToArray();
		}
		if (audio.AlbumArtists.Length == 0)
		{
			audio.AlbumArtists = audio.Artists.Take(1).ToArray();
		}
		audio.IndexNumber = GetDictionaryDiscValue(tags, "track");
		audio.ParentIndexNumber = GetDictionaryDiscValue(tags, "disc");
		if (audio.PremiereDate.HasValue && !audio.ProductionYear.HasValue)
		{
			audio.ProductionYear = audio.PremiereDate.Value.ToLocalTime().Year;
		}
		FetchStudios(audio, tags, "organization");
		FetchStudios(audio, tags, "ensemble");
		FetchStudios(audio, tags, "publisher");
		FetchStudios(audio, tags, "label");
		string multipleMusicBrainzId = GetMultipleMusicBrainzId(FFProbeHelpers.GetDictionaryValue(tags, "MusicBrainz Album Artist Id"));
		if (multipleMusicBrainzId == null)
		{
			multipleMusicBrainzId = GetMultipleMusicBrainzId(FFProbeHelpers.GetDictionaryValue(tags, "MUSICBRAINZ_ALBUMARTISTID"));
		}
		ProviderIdsExtensions.SetProviderId((IHasProviderIds)(object)audio, (MetadataProviders)9, multipleMusicBrainzId);
		multipleMusicBrainzId = GetMultipleMusicBrainzId(FFProbeHelpers.GetDictionaryValue(tags, "MusicBrainz Artist Id"));
		if (multipleMusicBrainzId == null)
		{
			multipleMusicBrainzId = GetMultipleMusicBrainzId(FFProbeHelpers.GetDictionaryValue(tags, "MUSICBRAINZ_ARTISTID"));
		}
		ProviderIdsExtensions.SetProviderId((IHasProviderIds)(object)audio, (MetadataProviders)10, multipleMusicBrainzId);
		multipleMusicBrainzId = GetMultipleMusicBrainzId(FFProbeHelpers.GetDictionaryValue(tags, "MusicBrainz Album Id"));
		if (multipleMusicBrainzId == null)
		{
			multipleMusicBrainzId = GetMultipleMusicBrainzId(FFProbeHelpers.GetDictionaryValue(tags, "MUSICBRAINZ_ALBUMID"));
		}
		ProviderIdsExtensions.SetProviderId((IHasProviderIds)(object)audio, (MetadataProviders)8, multipleMusicBrainzId);
		multipleMusicBrainzId = GetMultipleMusicBrainzId(FFProbeHelpers.GetDictionaryValue(tags, "MusicBrainz Release Group Id"));
		if (multipleMusicBrainzId == null)
		{
			multipleMusicBrainzId = GetMultipleMusicBrainzId(FFProbeHelpers.GetDictionaryValue(tags, "MUSICBRAINZ_RELEASEGROUPID"));
		}
		ProviderIdsExtensions.SetProviderId((IHasProviderIds)(object)audio, (MetadataProviders)11, multipleMusicBrainzId);
		multipleMusicBrainzId = GetMultipleMusicBrainzId(FFProbeHelpers.GetDictionaryValue(tags, "MusicBrainz Release Track Id"));
		if (multipleMusicBrainzId == null)
		{
			multipleMusicBrainzId = GetMultipleMusicBrainzId(FFProbeHelpers.GetDictionaryValue(tags, "MUSICBRAINZ_RELEASETRACKID"));
		}
		ProviderIdsExtensions.SetProviderId((IHasProviderIds)(object)audio, (MetadataProviders)18, multipleMusicBrainzId);
	}

	private string GetMultipleMusicBrainzId(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		return (from i in value.Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries)
			select i.Trim()).FirstOrDefault((string i) => !string.IsNullOrWhiteSpace(i));
	}

	private IEnumerable<string> Split(string val, bool allowCommaDelimiter)
	{
		char[] separator = ((!allowCommaDelimiter || _nameDelimiters.Any((char i) => val.IndexOf(i) != -1)) ? _nameDelimiters : new char[1] { ',' });
		return from i in val.Split(separator, StringSplitOptions.RemoveEmptyEntries)
			where !string.IsNullOrWhiteSpace(i)
			select i.Trim();
	}

	private IEnumerable<string> SplitArtists(string val, char[] delimiters, bool splitFeaturing)
	{
		if (splitFeaturing)
		{
			val = StringHelper.Replace(StringHelper.Replace(val, " featuring ", " | ", StringComparison.OrdinalIgnoreCase), " feat. ", " | ", StringComparison.OrdinalIgnoreCase);
		}
		List<string> list = new List<string>();
		string[] splitWhitelist = GetSplitWhitelist();
		foreach (string text in splitWhitelist)
		{
			string a = val;
			val = StringHelper.Replace(val, text, "|", StringComparison.OrdinalIgnoreCase);
			if (!string.Equals(a, val, StringComparison.OrdinalIgnoreCase))
			{
				list.Add(text);
			}
		}
		IEnumerable<string> collection = from text2 in val.Split(delimiters, StringSplitOptions.RemoveEmptyEntries)
			where !string.IsNullOrWhiteSpace(text2)
			select text2.Trim();
		list.AddRange(collection);
		return list;
	}

	private string[] GetSplitWhitelist()
	{
		if (_splitWhiteList == null)
		{
			_splitWhiteList = new string[1] { "AC/DC" };
		}
		return _splitWhiteList;
	}

	private void FetchStudios(MediaInfo info, Dictionary<string, string> tags, string tagName)
	{
		string dictionaryValue = FFProbeHelpers.GetDictionaryValue(tags, tagName);
		if (string.IsNullOrEmpty(dictionaryValue))
		{
			return;
		}
		IEnumerable<string> enumerable = Split(dictionaryValue, allowCommaDelimiter: true);
		List<string> list = info.Studios.ToList();
		foreach (string item in enumerable)
		{
			if (!info.Artists.Contains(item, StringComparer.OrdinalIgnoreCase) && !info.AlbumArtists.Contains(item, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(item);
			}
		}
		info.Studios = list.Where((string i) => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private void FetchGenres(MediaInfo info, Dictionary<string, string> tags)
	{
		string dictionaryValue = FFProbeHelpers.GetDictionaryValue(tags, "genre");
		if (string.IsNullOrEmpty(dictionaryValue))
		{
			return;
		}
		List<string> list = info.Genres.ToList();
		foreach (string item in Split(dictionaryValue, allowCommaDelimiter: true))
		{
			list.Add(item);
		}
		info.Genres = list.Where((string i) => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private int? GetDictionaryDiscValue(Dictionary<string, string> tags, string tagName)
	{
		string dictionaryValue = FFProbeHelpers.GetDictionaryValue(tags, tagName);
		if (!string.IsNullOrEmpty(dictionaryValue))
		{
			dictionaryValue = dictionaryValue.Split('/')[0];
			if (int.TryParse(dictionaryValue, out var result))
			{
				return result;
			}
		}
		return null;
	}

	private ChapterInfo GetChapterInfo(MediaChapter chapter)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected Obj, but got Unknown
		ChapterInfo val = new ChapterInfo();
		if (chapter.tags != null && chapter.tags.TryGetValue("title", out var value))
		{
			val.Name = value;
		}
		if (double.TryParse(chapter.start_time, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
		{
			double value2 = Math.Round(TimeSpan.FromSeconds(result).TotalMilliseconds);
			val.StartPositionTicks = TimeSpan.FromMilliseconds(value2).Ticks;
		}
		return val;
	}

	private void FetchWtvInfo(MediaInfo video, InternalMediaInfoResult data)
	{
		if (data.format == null || data.format.tags == null)
		{
			return;
		}
		string dictionaryValue = FFProbeHelpers.GetDictionaryValue(data.format.tags, "WM/Genre");
		if (!string.IsNullOrWhiteSpace(dictionaryValue))
		{
			string[] array = (from i in dictionaryValue.Split(new char[3] { ';', '/', ',' }, StringSplitOptions.RemoveEmptyEntries)
				where !string.IsNullOrWhiteSpace(i)
				select i.Trim()).ToArray();
			if (array.Length != 0)
			{
				video.Genres = array;
			}
		}
		string dictionaryValue2 = FFProbeHelpers.GetDictionaryValue(data.format.tags, "WM/ParentalRating");
		if (!string.IsNullOrWhiteSpace(dictionaryValue2))
		{
			video.OfficialRating = dictionaryValue2;
		}
		string dictionaryValue3 = FFProbeHelpers.GetDictionaryValue(data.format.tags, "WM/MediaCredits");
		if (!string.IsNullOrEmpty(dictionaryValue3))
		{
			video.People = (from i in dictionaryValue3.Split(new char[2] { ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
				where !string.IsNullOrWhiteSpace(i)
				select i).Select((Func<string, BaseItemPerson>)((string i) =>
			{
				//IL_0000: Unknown result type (might be due to invalid IL or missing references)
				//IL_0005: Unknown result type (might be due to invalid IL or missing references)
				//IL_0011: Unknown result type (might be due to invalid IL or missing references)
				//IL_001d: Expected Obj, but got Unknown
				return new BaseItemPerson
				{
					Name = i.Trim(),
					Type = "Actor"
				};
			})).ToArray();
		}
		string dictionaryValue4 = FFProbeHelpers.GetDictionaryValue(data.format.tags, "WM/OriginalReleaseTime");
		if (!string.IsNullOrWhiteSpace(dictionaryValue4) && int.TryParse(dictionaryValue4, NumberStyles.Integer, _usCulture, out var result))
		{
			video.ProductionYear = result;
		}
		if (!string.IsNullOrWhiteSpace(FFProbeHelpers.GetDictionaryValue(data.format.tags, "WM/MediaOriginalBroadcastDateTime")) && DateTime.TryParse(dictionaryValue4, null, DateTimeStyles.None, out var result2))
		{
			video.PremiereDate = result2.ToUniversalTime();
		}
		string text = FFProbeHelpers.GetDictionaryValue(data.format.tags, "WM/SubTitleDescription");
		if (string.IsNullOrWhiteSpace(FFProbeHelpers.GetDictionaryValue(data.format.tags, "WM/SubTitle")) && !string.IsNullOrWhiteSpace(text) && text.Substring(0, Math.Min(text.Length, 100)).Contains(":"))
		{
			string[] array2 = text.Split(':');
			if (array2.Length != 0)
			{
				string text2 = array2[0];
				try
				{
					if (!text2.Contains("/"))
					{
						throw new Exception();
					}
					string[] array3 = text2.Split(' ');
					video.IndexNumber = int.Parse(array3[0].Replace(".", "").Split('/')[0]);
					text = string.Join(" ", array3, 1, array3.Length - 1).Trim();
				}
				catch
				{
					text = ((!text2.Contains(".")) ? text2.Trim() : string.Join(".", text2.Split('.'), 1, text2.Split('.').Length - 1).Trim());
				}
			}
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			video.Overview = text;
		}
	}

	private void ExtractTimestamp(MediaInfo video)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		VideoType? videoType = ((MediaSourceInfo)video).VideoType;
		VideoType val = (VideoType)0;
		if (videoType.GetValueOrDefault() == val && videoType.HasValue && (string.Equals(((MediaSourceInfo)video).Container, "mpeg2ts", StringComparison.OrdinalIgnoreCase) || string.Equals(((MediaSourceInfo)video).Container, "m2ts", StringComparison.OrdinalIgnoreCase) || string.Equals(((MediaSourceInfo)video).Container, "ts", StringComparison.OrdinalIgnoreCase)))
		{
			try
			{
				((MediaSourceInfo)video).Timestamp = GetMpegTimestamp(((MediaSourceInfo)video).Path);
				_logger.Debug("Video has {0} timestamp", new object[1] { ((MediaSourceInfo)video).Timestamp });
			}
			catch (Exception ex)
			{
				_logger.ErrorException("Error extracting timestamp info from {0}", ex, new object[1] { ((MediaSourceInfo)video).Path });
				((MediaSourceInfo)video).Timestamp = null;
			}
		}
	}

	private TransportStreamTimestamp GetMpegTimestamp(string path)
	{
		byte[] array = new byte[197];
		using (Stream stream = _fileSystem.GetFileStream(path, (FileOpenMode)3, (FileAccessMode)1, (FileShareMode)1, false))
		{
			stream.Read(array, 0, array.Length);
		}
		if (array[0] != 71)
		{
			if (array[4] == 71 && array[196] == 71)
			{
				if (array[0] == 0 && array[1] == 0 && array[2] == 0 && array[3] == 0)
				{
					return (TransportStreamTimestamp)1;
				}
				return (TransportStreamTimestamp)2;
			}
			return (TransportStreamTimestamp)0;
		}
		return (TransportStreamTimestamp)0;
	}
}
