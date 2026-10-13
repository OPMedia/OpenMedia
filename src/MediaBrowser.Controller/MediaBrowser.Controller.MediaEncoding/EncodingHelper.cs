using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Reflection;

namespace MediaBrowser.Controller.MediaEncoding;

public class EncodingHelper
{
	private readonly CultureInfo _usCulture = new CultureInfo("en-US");

	private readonly IMediaEncoder _mediaEncoder;

	private readonly IFileSystem _fileSystem;

	private readonly ISubtitleEncoder _subtitleEncoder;

	private readonly IApplicationPaths _appPaths;

	private readonly IAssemblyInfo _assemblyInfo;

	public EncodingHelper(IMediaEncoder mediaEncoder, IFileSystem fileSystem, ISubtitleEncoder subtitleEncoder, IApplicationPaths appPaths, IAssemblyInfo assemblyInfo)
	{
		_mediaEncoder = mediaEncoder;
		_fileSystem = fileSystem;
		_subtitleEncoder = subtitleEncoder;
		_appPaths = appPaths;
		_assemblyInfo = assemblyInfo;
	}

	public string GetH264Encoder(EncodingJobInfo state, EncodingOptions encodingOptions)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		string result = "libx264";
		if ((int)state.VideoType == 0)
		{
			string hardwareAccelerationType = encodingOptions.HardwareAccelerationType;
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "qsv", "h264_qsv" },
				{ "h264_qsv", "h264_qsv" },
				{ "nvenc", "h264_nvenc" },
				{ "amf", "h264_amf" },
				{ "omx", "h264_omx" },
				{ "h264_v4l2m2m", "h264_v4l2m2m" },
				{ "mediacodec", "h264_mediacodec" },
				{ "vaapi", "h264_vaapi" }
			};
			if (!string.IsNullOrEmpty(hardwareAccelerationType) && encodingOptions.EnableHardwareEncoding && dictionary.ContainsKey(hardwareAccelerationType) && CheckVaapi(state, hardwareAccelerationType, encodingOptions))
			{
				string text = dictionary[hardwareAccelerationType];
				if (_mediaEncoder.SupportsEncoder(text))
				{
					return text;
				}
			}
		}
		encodingOptions.EnableHardwareEncoding = false;
		return result;
	}

	private bool CheckVaapi(EncodingJobInfo state, string hwType, EncodingOptions encodingOptions)
	{
		if (!string.Equals(hwType, "vaapi", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (string.IsNullOrEmpty(encodingOptions.VaapiDevice))
		{
			return true;
		}
		return IsVaapiSupported(state);
	}

	private bool IsVaapiSupported(EncodingJobInfo state)
	{
		MediaStream videoStream = state.VideoStream;
		if (videoStream != null && string.Equals(videoStream.Codec, "mpeg4", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return true;
	}

	public string GetVideoEncoder(EncodingJobInfo state, EncodingOptions encodingOptions)
	{
		string outputVideoCodec = state.OutputVideoCodec;
		if (!string.IsNullOrEmpty(outputVideoCodec))
		{
			if (string.Equals(outputVideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
			{
				return GetH264Encoder(state, encodingOptions);
			}
			if (string.Equals(outputVideoCodec, "vpx", StringComparison.OrdinalIgnoreCase))
			{
				return "libvpx";
			}
			if (string.Equals(outputVideoCodec, "wmv", StringComparison.OrdinalIgnoreCase))
			{
				return "wmv2";
			}
			if (string.Equals(outputVideoCodec, "theora", StringComparison.OrdinalIgnoreCase))
			{
				return "libtheora";
			}
			return outputVideoCodec.ToLower();
		}
		return "copy";
	}

	public string GetUserAgentParam(EncodingJobInfo state)
	{
		string value = null;
		state.RemoteHttpHeaders.TryGetValue("User-Agent", out value);
		if (!string.IsNullOrEmpty(value))
		{
			return "-user_agent \"" + value + "\"";
		}
		return string.Empty;
	}

	public string GetInputFormat(string container)
	{
		if (string.IsNullOrEmpty(container))
		{
			return null;
		}
		container = StringHelper.Replace(container, "mkv", "matroska", StringComparison.OrdinalIgnoreCase);
		if (string.Equals(container, "ts", StringComparison.OrdinalIgnoreCase))
		{
			return "mpegts";
		}
		if (string.Equals(container, "m2ts", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "wmv", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "mts", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "vob", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "mpg", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "mpeg", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "rec", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "dvr-ms", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "ogm", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "divx", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "tp", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "rmvb", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "rtp", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "m4v", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(container, "strm", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return container;
	}

	public string GetDecoderFromCodec(string codec)
	{
		if (string.Equals(codec, "mp2", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(codec, "aac_latm", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (string.Equals(codec, "eac3", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (_mediaEncoder.SupportsDecoder(codec))
		{
			return codec;
		}
		return null;
	}

	public string InferAudioCodec(string container)
	{
		string a = "." + (container ?? string.Empty);
		if (string.Equals(a, ".mp3", StringComparison.OrdinalIgnoreCase))
		{
			return "mp3";
		}
		if (string.Equals(a, ".aac", StringComparison.OrdinalIgnoreCase))
		{
			return "aac";
		}
		if (string.Equals(a, ".wma", StringComparison.OrdinalIgnoreCase))
		{
			return "wma";
		}
		if (string.Equals(a, ".ogg", StringComparison.OrdinalIgnoreCase))
		{
			return "vorbis";
		}
		if (string.Equals(a, ".oga", StringComparison.OrdinalIgnoreCase))
		{
			return "vorbis";
		}
		if (string.Equals(a, ".ogv", StringComparison.OrdinalIgnoreCase))
		{
			return "vorbis";
		}
		if (string.Equals(a, ".webm", StringComparison.OrdinalIgnoreCase))
		{
			return "vorbis";
		}
		if (string.Equals(a, ".webma", StringComparison.OrdinalIgnoreCase))
		{
			return "vorbis";
		}
		return "copy";
	}

	public string InferVideoCodec(string url)
	{
		string extension = Path.GetExtension(url);
		if (string.Equals(extension, ".asf", StringComparison.OrdinalIgnoreCase))
		{
			return "wmv";
		}
		if (string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase))
		{
			return "vpx";
		}
		if (string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".ogv", StringComparison.OrdinalIgnoreCase))
		{
			return "theora";
		}
		if (string.Equals(extension, ".m3u8", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".ts", StringComparison.OrdinalIgnoreCase))
		{
			return "h264";
		}
		return "copy";
	}

	public int GetVideoProfileScore(string profile)
	{
		return Array.FindIndex(new string[7] { "Constrained Baseline", "Baseline", "Extended", "Main", "High", "Progressive High", "Constrained High" }, (string t) => string.Equals(t.Replace(" ", ""), profile.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
	}

	public string GetInputPathArgument(EncodingJobInfo state)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Invalid comparison between Unknown and I4
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		MediaProtocol inputProtocol = state.InputProtocol;
		string text = state.MediaPath ?? string.Empty;
		string[] inputFiles = new string[1] { text };
		if (state.IsInputVideo && ((int)state.VideoType != 1 || state.IsoMount != null))
		{
			inputFiles = MediaEncoderHelpers.GetInputArgument(_fileSystem, text, state.InputProtocol, state.IsoMount, state.PlayableStreamFileNames);
		}
		return _mediaEncoder.GetInputArgument(inputFiles, inputProtocol);
	}

	public string GetAudioEncoder(EncodingJobInfo state)
	{
		string outputAudioCodec = state.OutputAudioCodec;
		if (string.Equals(outputAudioCodec, "aac", StringComparison.OrdinalIgnoreCase))
		{
			return "aac -strict experimental";
		}
		if (string.Equals(outputAudioCodec, "mp3", StringComparison.OrdinalIgnoreCase))
		{
			return "libmp3lame";
		}
		if (string.Equals(outputAudioCodec, "vorbis", StringComparison.OrdinalIgnoreCase))
		{
			return "libvorbis";
		}
		if (string.Equals(outputAudioCodec, "wma", StringComparison.OrdinalIgnoreCase))
		{
			return "wmav2";
		}
		if (string.Equals(outputAudioCodec, "opus", StringComparison.OrdinalIgnoreCase))
		{
			return "libopus";
		}
		return outputAudioCodec.ToLower();
	}

	public string GetInputArgument(EncodingJobInfo state, EncodingOptions encodingOptions)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Invalid comparison between Unknown and I4
		_ = state.BaseRequest;
		string text = $"-i {GetInputPathArgument(state)}";
		if (state.SubtitleStream != null && (int)state.SubtitleDeliveryMethod == 0 && state.SubtitleStream.IsExternal && !state.SubtitleStream.IsTextSubtitleStream)
		{
			if (state.VideoStream != null && state.VideoStream.Width.HasValue)
			{
				double num = state.VideoStream.Width.Value;
				num /= 16.0;
				num *= 9.0;
				text += $" -canvas_size {state.VideoStream.Width.Value.ToString(CultureInfo.InvariantCulture)}:{Convert.ToInt32(num).ToString(CultureInfo.InvariantCulture)}";
			}
			string text2 = state.SubtitleStream.Path;
			if (string.Equals(Path.GetExtension(text2), ".sub", StringComparison.OrdinalIgnoreCase))
			{
				string text3 = Path.ChangeExtension(text2, ".idx");
				if (_fileSystem.FileExists(text3))
				{
					text2 = text3;
				}
			}
			text = text + " -i \"" + text2 + "\"";
		}
		if (state.IsVideoRequest && GetVideoEncoder(state, encodingOptions).IndexOf("vaapi", StringComparison.OrdinalIgnoreCase) != -1)
		{
			bool num2 = state.SubtitleStream != null && !state.SubtitleStream.IsTextSubtitleStream && (int)state.SubtitleDeliveryMethod == 0;
			string text4 = "vaapi";
			if (num2)
			{
				text4 = "yuv420p";
			}
			text = "-hwaccel vaapi -hwaccel_output_format " + text4 + " -vaapi_device " + encodingOptions.VaapiDevice + " " + text;
		}
		return text.Trim();
	}

	public bool IsH264(MediaStream stream)
	{
		string text = stream.Codec ?? string.Empty;
		if (text.IndexOf("264", StringComparison.OrdinalIgnoreCase) == -1)
		{
			return text.IndexOf("avc", StringComparison.OrdinalIgnoreCase) != -1;
		}
		return true;
	}

	public string GetVideoBitrateParam(EncodingJobInfo state, string videoCodec)
	{
		int? outputVideoBitrate = state.OutputVideoBitrate;
		if (outputVideoBitrate.HasValue)
		{
			if (string.Equals(videoCodec, "libvpx", StringComparison.OrdinalIgnoreCase))
			{
				return string.Format(" -maxrate:v {0} -bufsize:v {1} -b:v {0}", outputVideoBitrate.Value.ToString(_usCulture), (outputVideoBitrate.Value * 2).ToString(_usCulture));
			}
			if (string.Equals(videoCodec, "msmpeg4", StringComparison.OrdinalIgnoreCase))
			{
				return $" -b:v {outputVideoBitrate.Value.ToString(_usCulture)}";
			}
			if (string.Equals(videoCodec, "libx264", StringComparison.OrdinalIgnoreCase))
			{
				return $" -maxrate {outputVideoBitrate.Value.ToString(_usCulture)} -bufsize {(outputVideoBitrate.Value * 2).ToString(_usCulture)}";
			}
			return string.Format(" -b:v {0} -maxrate {0} -bufsize {1}", outputVideoBitrate.Value.ToString(_usCulture), (outputVideoBitrate.Value * 2).ToString(_usCulture));
		}
		return string.Empty;
	}

	public string NormalizeTranscodingLevel(string videoCodec, string level)
	{
		if (double.TryParse(level, NumberStyles.Any, _usCulture, out var result) && string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase) && result > 41.0)
		{
			return "41";
		}
		return level;
	}

	public string GetTextSubtitleParam(EncodingJobInfo state)
	{
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		double num = Math.Round(TimeSpan.FromTicks(state.StartTimeTicks ?? 0).TotalSeconds);
		string text = ((state.CopyTimestamps || state.TranscodingType != TranscodingJobType.Progressive) ? string.Empty : $",setpts=PTS -{num.ToString(_usCulture)}/TB");
		string text2 = Path.Combine(_appPaths.ProgramDataPath, "fonts", "DroidSansFallback.ttf");
		string empty = string.Empty;
		if (!_fileSystem.FileExists(text2))
		{
			_fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(text2));
			using Stream stream = _assemblyInfo.GetManifestResourceStream(GetType(), GetType().Namespace + ".DroidSansFallback.ttf");
			using Stream destination = _fileSystem.GetFileStream(text2, (FileOpenMode)2, (FileAccessMode)2, (FileShareMode)1, false);
			stream.CopyTo(destination);
		}
		empty = $":force_style='FontName=Droid Sans Fallback':fontsdir='{_mediaEncoder.EscapeSubtitleFilterPath(_fileSystem.GetDirectoryName(text2))}'";
		if (state.SubtitleStream.IsExternal)
		{
			string path = state.SubtitleStream.Path;
			string text3 = string.Empty;
			if (!string.IsNullOrEmpty(state.SubtitleStream.Language))
			{
				string result = _subtitleEncoder.GetSubtitleFileCharacterSet(path, state.SubtitleStream.Language, state.MediaSource.Protocol, CancellationToken.None).Result;
				if (!string.IsNullOrEmpty(result))
				{
					text3 = ":charenc=" + result;
				}
			}
			return $"subtitles=filename='{_mediaEncoder.EscapeSubtitleFilterPath(path)}'{text3}{empty}{text}";
		}
		string path2 = state.MediaPath ?? string.Empty;
		return $"subtitles='{_mediaEncoder.EscapeSubtitleFilterPath(path2)}:si={state.InternalSubtitleStreamOffset.ToString(_usCulture)}'{empty}{text}";
	}

	public double? GetFramerateParam(EncodingJobInfo state)
	{
		BaseEncodingJobOptions baseRequest = state.BaseRequest;
		if (baseRequest.Framerate.HasValue)
		{
			return baseRequest.Framerate.Value;
		}
		float? maxFramerate = baseRequest.MaxFramerate;
		if (maxFramerate.HasValue && state.VideoStream != null)
		{
			float? num = state.VideoStream.AverageFrameRate ?? state.VideoStream.RealFrameRate;
			if (num.HasValue && num.Value > maxFramerate.Value)
			{
				return maxFramerate;
			}
		}
		return null;
	}

	public string GetVideoQualityParam(EncodingJobInfo state, string videoEncoder, EncodingOptions encodingOptions, string defaultH264Preset)
	{
		string text = string.Empty;
		bool flag = state.VideoStream != null && string.Equals(state.VideoStream.Codec, "vc1", StringComparison.OrdinalIgnoreCase);
		if (string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase))
		{
			text = (string.IsNullOrEmpty(encodingOptions.H264Preset) ? (text + "-preset " + defaultH264Preset) : (text + "-preset " + encodingOptions.H264Preset));
			text = ((encodingOptions.H264Crf < 0 || encodingOptions.H264Crf > 51) ? (text + " -crf 23") : (text + " -crf " + encodingOptions.H264Crf.ToString(CultureInfo.InvariantCulture)));
		}
		else if (string.Equals(videoEncoder, "libx265", StringComparison.OrdinalIgnoreCase))
		{
			text += "-preset fast";
			text += " -crf 28";
		}
		else if (string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase))
		{
			text = ((!new string[7] { "veryslow", "slower", "slow", "medium", "fast", "faster", "veryfast" }.Contains(encodingOptions.H264Preset, StringComparer.OrdinalIgnoreCase)) ? (text + "-preset 7") : (text + "-preset " + encodingOptions.H264Preset));
			text += " -look_ahead 0";
		}
		else if (string.Equals(videoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase))
		{
			switch (encodingOptions.H264Preset)
			{
			case "veryslow":
				text += "-preset slow";
				break;
			case "slow":
			case "slower":
				text += "-preset slow";
				break;
			case "medium":
				text += "-preset medium";
				break;
			case "fast":
			case "faster":
			case "veryfast":
			case "superfast":
			case "ultrafast":
				text += "-preset fast";
				break;
			default:
				text += "-preset default";
				break;
			}
		}
		else if (string.Equals(videoEncoder, "libvpx", StringComparison.OrdinalIgnoreCase))
		{
			int num = 0;
			string text2 = "0";
			string text3 = "50";
			string text4 = "10";
			if (flag)
			{
				num++;
			}
			num = Math.Min(num, 2);
			text += $"-speed 16 -quality good -profile:v {num.ToString(_usCulture)} -slices 8 -crf {text4} -qmin {text2} -qmax {text3}";
		}
		else if (string.Equals(videoEncoder, "mpeg4", StringComparison.OrdinalIgnoreCase))
		{
			text += "-mbd rd -flags +mv4+aic -trellis 2 -cmp 2 -subcmp 2 -bf 2";
		}
		else if (string.Equals(videoEncoder, "wmv2", StringComparison.OrdinalIgnoreCase))
		{
			text += "-qmin 2";
		}
		else if (string.Equals(videoEncoder, "msmpeg4", StringComparison.OrdinalIgnoreCase))
		{
			text += "-mbd 2";
		}
		text += GetVideoBitrateParam(state, videoEncoder);
		double? framerateParam = GetFramerateParam(state);
		if (framerateParam.HasValue)
		{
			text += $" -r {framerateParam.Value.ToString(_usCulture)}";
		}
		string actualOutputVideoCodec = state.ActualOutputVideoCodec;
		_ = state.BaseRequest;
		string text5 = state.GetRequestedProfiles(actualOutputVideoCodec).FirstOrDefault();
		if (string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase))
		{
			text += " -profile:v 578";
		}
		else if (!string.IsNullOrEmpty(text5) && !string.Equals(videoEncoder, "h264_omx", StringComparison.OrdinalIgnoreCase) && !string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase) && !string.Equals(videoEncoder, "h264_v4l2m2m", StringComparison.OrdinalIgnoreCase))
		{
			text = text + " -profile:v " + text5;
		}
		string requestedLevel = state.GetRequestedLevel(actualOutputVideoCodec);
		if (!string.IsNullOrEmpty(requestedLevel))
		{
			requestedLevel = NormalizeTranscodingLevel(state.OutputVideoCodec, requestedLevel);
			if (string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase) || string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase))
			{
				text = requestedLevel switch
				{
					"30" => text + " -level 3.0", 
					"31" => text + " -level 3.1", 
					"32" => text + " -level 3.2", 
					"40" => text + " -level 4.0", 
					"41" => text + " -level 4.1", 
					"42" => text + " -level 4.2", 
					"50" => text + " -level 5.0", 
					"51" => text + " -level 5.1", 
					"52" => text + " -level 5.2", 
					_ => text + " -level " + requestedLevel, 
				};
			}
			else if (!string.Equals(videoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase) && !string.Equals(videoEncoder, "h264_omx", StringComparison.OrdinalIgnoreCase))
			{
				text = text + " -level " + requestedLevel;
			}
		}
		if (string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase))
		{
			text += " -x264opts:0 subme=0:me_range=4:rc_lookahead=10:me=dia:no_chroma_me:8x8dct=0:partitions=none";
		}
		if (!string.Equals(videoEncoder, "h264_omx", StringComparison.OrdinalIgnoreCase) && !string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase) && !string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase) && !string.Equals(videoEncoder, "h264_v4l2m2m", StringComparison.OrdinalIgnoreCase))
		{
			text = "-pix_fmt yuv420p " + text;
		}
		if (string.Equals(videoEncoder, "h264_v4l2m2m", StringComparison.OrdinalIgnoreCase))
		{
			text = "-pix_fmt nv21 " + text;
		}
		return text;
	}

	public bool CanStreamCopyVideo(EncodingJobInfo state, MediaStream videoStream)
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		BaseEncodingJobOptions baseRequest = state.BaseRequest;
		if (!baseRequest.AllowVideoStreamCopy)
		{
			return false;
		}
		if (videoStream.IsInterlaced && state.DeInterlace(videoStream.Codec, forceDeinterlaceIfSourceIsInterlaced: false))
		{
			return false;
		}
		bool? isAnamorphic = videoStream.IsAnamorphic;
		if (isAnamorphic.HasValue && isAnamorphic == true && baseRequest.RequireNonAnamorphic)
		{
			return false;
		}
		if (baseRequest.SubtitleStreamIndex.HasValue && (int)state.SubtitleDeliveryMethod == 0)
		{
			return false;
		}
		if (string.Equals("h264", videoStream.Codec, StringComparison.OrdinalIgnoreCase) && videoStream.IsAVC.HasValue && !videoStream.IsAVC.Value && baseRequest.RequireAvc)
		{
			return false;
		}
		if (string.IsNullOrEmpty(videoStream.Codec) || !state.SupportedVideoCodecs.Contains(videoStream.Codec, StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		string[] requestedProfiles = state.GetRequestedProfiles(videoStream.Codec);
		if (requestedProfiles.Length != 0)
		{
			string.IsNullOrEmpty(videoStream.Profile);
			string profile = requestedProfiles[0];
			if (!string.IsNullOrEmpty(videoStream.Profile) && !requestedProfiles.Contains(videoStream.Profile.Replace(" ", ""), StringComparer.OrdinalIgnoreCase))
			{
				int videoProfileScore = GetVideoProfileScore(videoStream.Profile);
				int videoProfileScore2 = GetVideoProfileScore(profile);
				if (videoProfileScore == -1 || videoProfileScore > videoProfileScore2)
				{
					return false;
				}
			}
		}
		if (baseRequest.MaxWidth.HasValue && (!videoStream.Width.HasValue || videoStream.Width.Value > baseRequest.MaxWidth.Value))
		{
			return false;
		}
		if (baseRequest.MaxHeight.HasValue && (!videoStream.Height.HasValue || videoStream.Height.Value > baseRequest.MaxHeight.Value))
		{
			return false;
		}
		float? num = baseRequest.MaxFramerate ?? baseRequest.Framerate;
		if (num.HasValue)
		{
			float? num2 = videoStream.AverageFrameRate ?? videoStream.RealFrameRate;
			if (!num2.HasValue || num2.Value > num.Value)
			{
				return false;
			}
		}
		if (baseRequest.VideoBitRate.HasValue && (!videoStream.BitRate.HasValue || videoStream.BitRate.Value > baseRequest.VideoBitRate.Value))
		{
			return false;
		}
		int? requestedVideoBitDepth = state.GetRequestedVideoBitDepth(videoStream.Codec);
		if (requestedVideoBitDepth.HasValue && videoStream.BitDepth.HasValue && videoStream.BitDepth.Value > requestedVideoBitDepth.Value)
		{
			return false;
		}
		int? requestedMaxRefFrames = state.GetRequestedMaxRefFrames(videoStream.Codec);
		if (requestedMaxRefFrames.HasValue && videoStream.RefFrames.HasValue && videoStream.RefFrames.Value > requestedMaxRefFrames.Value)
		{
			return false;
		}
		string requestedLevel = state.GetRequestedLevel(videoStream.Codec);
		if (!string.IsNullOrEmpty(requestedLevel) && double.TryParse(requestedLevel, NumberStyles.Any, _usCulture, out var result))
		{
			_ = videoStream.Level.HasValue;
			if (videoStream.Level.HasValue && videoStream.Level.Value > result)
			{
				return false;
			}
		}
		if (string.Equals(state.InputContainer, "avi", StringComparison.OrdinalIgnoreCase) && string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase) && ((!videoStream.IsAVC) ?? true))
		{
			return false;
		}
		return baseRequest.EnableAutoStreamCopy;
	}

	public bool CanStreamCopyAudio(EncodingJobInfo state, MediaStream audioStream, string[] supportedAudioCodecs)
	{
		BaseEncodingJobOptions baseRequest = state.BaseRequest;
		if (!baseRequest.AllowAudioStreamCopy)
		{
			return false;
		}
		int? requestedAudioBitDepth = state.GetRequestedAudioBitDepth(audioStream.Codec);
		if (requestedAudioBitDepth.HasValue && audioStream.BitDepth.HasValue && audioStream.BitDepth.Value > requestedAudioBitDepth.Value)
		{
			return false;
		}
		if (string.IsNullOrEmpty(audioStream.Codec) || !supportedAudioCodecs.Contains(audioStream.Codec, StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		int? requestedAudioChannels = state.GetRequestedAudioChannels(audioStream.Codec);
		if (requestedAudioChannels.HasValue)
		{
			if (!audioStream.Channels.HasValue || audioStream.Channels.Value <= 0)
			{
				return false;
			}
			if (audioStream.Channels.Value > requestedAudioChannels.Value)
			{
				return false;
			}
		}
		if (baseRequest.AudioSampleRate.HasValue)
		{
			if (!audioStream.SampleRate.HasValue || audioStream.SampleRate.Value <= 0)
			{
				return false;
			}
			if (audioStream.SampleRate.Value > baseRequest.AudioSampleRate.Value)
			{
				return false;
			}
		}
		if (baseRequest.AudioBitRate.HasValue)
		{
			if (!audioStream.BitRate.HasValue || audioStream.BitRate.Value <= 0)
			{
				return false;
			}
			if (audioStream.BitRate.Value > baseRequest.AudioBitRate.Value)
			{
				return false;
			}
		}
		return baseRequest.EnableAutoStreamCopy;
	}

	public int? GetVideoBitrateParamValue(BaseEncodingJobOptions request, MediaStream videoStream, string outputVideoCodec)
	{
		int? result = request.VideoBitRate;
		if (request.Height.HasValue && request.Width.HasValue)
		{
			return result;
		}
		if (videoStream != null)
		{
			if (result.HasValue && videoStream.BitRate.HasValue)
			{
				result = Math.Min(videoStream.BitRate.Value, result.Value);
			}
			if (result.HasValue)
			{
				string codec = videoStream.Codec;
				result = ScaleBitrate(result.Value, codec, outputVideoCodec);
				if (request.VideoBitRate.HasValue)
				{
					result = Math.Min(result.Value, request.VideoBitRate.Value);
				}
			}
		}
		return result;
	}

	private static double GetVideoBitrateScaleFactor(string codec)
	{
		if (StringHelper.EqualsIgnoreCase(codec, "h265") || StringHelper.EqualsIgnoreCase(codec, "hevc") || StringHelper.EqualsIgnoreCase(codec, "vp9"))
		{
			return 0.5;
		}
		return 1.0;
	}

	private static int ScaleBitrate(int bitrate, string inputVideoCodec, string outputVideoCodec)
	{
		double videoBitrateScaleFactor = GetVideoBitrateScaleFactor(inputVideoCodec);
		double num = GetVideoBitrateScaleFactor(outputVideoCodec) / videoBitrateScaleFactor;
		if (bitrate <= 500000)
		{
			num = Math.Max(num, 4.0);
		}
		else if (bitrate <= 1000000)
		{
			num = Math.Max(num, 3.0);
		}
		else if (bitrate <= 2000000)
		{
			num = Math.Max(num, 2.5);
		}
		else if (bitrate <= 3000000)
		{
			num = Math.Max(num, 2.0);
		}
		return Convert.ToInt32(num * (double)bitrate);
	}

	public int? GetAudioBitrateParam(BaseEncodingJobOptions request, MediaStream audioStream)
	{
		if (request.AudioBitRate.HasValue)
		{
			if (audioStream != null)
			{
				int? bitRate = audioStream.BitRate;
				if (!bitRate.HasValue)
				{
					_ = request.AudioBitRate.Value;
				}
				else
				{
					bitRate.GetValueOrDefault();
				}
			}
			else
			{
				_ = request.AudioBitRate.Value;
			}
			return Math.Min(384000, request.AudioBitRate.Value);
		}
		return null;
	}

	public string GetAudioFilterParam(EncodingJobInfo state, EncodingOptions encodingOptions, bool isHls)
	{
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		int? outputAudioChannels = state.OutputAudioChannels;
		List<string> list = new List<string>();
		if (outputAudioChannels.HasValue && outputAudioChannels.Value <= 2 && state.AudioStream != null && state.AudioStream.Channels.HasValue && state.AudioStream.Channels.Value > 5 && !encodingOptions.DownMixAudioBoost.Equals(1.0))
		{
			list.Add("volume=" + encodingOptions.DownMixAudioBoost.ToString(_usCulture));
		}
		bool flag = state.CopyTimestamps || state.TranscodingType != TranscodingJobType.Progressive;
		if (state.SubtitleStream != null && state.SubtitleStream.IsTextSubtitleStream && (int)state.SubtitleDeliveryMethod == 0 && !flag)
		{
			double totalSeconds = TimeSpan.FromTicks(state.StartTimeTicks ?? 0).TotalSeconds;
			list.Add($"asetpts=PTS-{Math.Round(totalSeconds).ToString(_usCulture)}/TB");
		}
		if (list.Count > 0)
		{
			return "-af \"" + string.Join(",", list.ToArray()) + "\"";
		}
		return string.Empty;
	}

	public int? GetNumAudioChannelsParam(EncodingJobInfo state, MediaStream audioStream, string outputAudioCodec)
	{
		BaseEncodingJobOptions baseRequest = state.BaseRequest;
		int? num = ((audioStream == null) ? ((int?)null) : audioStream.Channels);
		if (num <= 0)
		{
			num = null;
		}
		int? num2 = null;
		string text = outputAudioCodec ?? string.Empty;
		if (text.IndexOf("wma", StringComparison.OrdinalIgnoreCase) != -1)
		{
			num2 = 2;
		}
		else
		{
			num2 = ((text.IndexOf("mp3", StringComparison.OrdinalIgnoreCase) == -1) ? new int?(6) : new int?(2));
		}
		bool flag = !string.Equals(text, "copy", StringComparison.OrdinalIgnoreCase);
		int? num3 = state.GetRequestedAudioChannels(text);
		if (flag)
		{
			num3 = GetMinValue(baseRequest.TranscodingMaxAudioChannels, num3);
		}
		if (num.HasValue)
		{
			num3 = (num3.HasValue ? Math.Min(num3.Value, num.Value) : num.Value);
		}
		if (flag && num2.HasValue)
		{
			num3 = (num3.HasValue ? Math.Min(num3.Value, num2.Value) : num2.Value);
		}
		return num3;
	}

	private int? GetMinValue(int? val1, int? val2)
	{
		if (!val1.HasValue)
		{
			return val2;
		}
		if (!val2.HasValue)
		{
			return val1;
		}
		return Math.Min(val1.Value, val2.Value);
	}

	public void EnforceResolutionLimit(EncodingJobInfo state)
	{
		BaseEncodingJobOptions baseRequest = state.BaseRequest;
		baseRequest.MaxWidth = baseRequest.MaxWidth ?? baseRequest.Width;
		baseRequest.MaxHeight = baseRequest.MaxHeight ?? baseRequest.Height;
		baseRequest.Width = null;
		baseRequest.Height = null;
	}

	public string GetFastSeekCommandLineParameter(BaseEncodingJobOptions request)
	{
		long num = request.StartTimeTicks ?? 0;
		if (num > 0)
		{
			return $"-ss {_mediaEncoder.GetTimeParameter(num)}";
		}
		return string.Empty;
	}

	public string GetMapArgs(EncodingJobInfo state)
	{
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Invalid comparison between Unknown and I4
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Invalid comparison between Unknown and I4
		if (state.VideoStream == null && state.AudioStream == null)
		{
			if (!state.IsInputVideo)
			{
				return string.Empty;
			}
			return "-sn";
		}
		if (state.VideoStream != null && state.VideoStream.Index == -1)
		{
			return "-sn";
		}
		if (state.AudioStream != null && state.AudioStream.Index == -1)
		{
			if (!state.IsInputVideo)
			{
				return string.Empty;
			}
			return "-sn";
		}
		string empty = string.Empty;
		empty = ((state.VideoStream == null) ? (empty + "-vn") : (empty + $"-map 0:{state.VideoStream.Index}"));
		empty = ((state.AudioStream == null) ? (empty + " -map -0:a") : (empty + $" -map 0:{state.AudioStream.Index}"));
		SubtitleDeliveryMethod subtitleDeliveryMethod = state.SubtitleDeliveryMethod;
		if (state.SubtitleStream == null || (int)subtitleDeliveryMethod == 3)
		{
			empty += " -map -0:s";
		}
		else if ((int)subtitleDeliveryMethod == 1)
		{
			empty += $" -map 0:{state.SubtitleStream.Index}";
		}
		else if (state.SubtitleStream.IsExternal && !state.SubtitleStream.IsTextSubtitleStream)
		{
			empty += " -map 1:0 -sn";
		}
		return empty;
	}

	public MediaStream GetMediaStream(IEnumerable<MediaStream> allStream, int? desiredIndex, MediaStreamType type, bool returnFirstIfNoIndex = true)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		List<MediaStream> source = (from i in allStream.Where((MediaStream s) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				return s.Type == type;
			})
			orderby i.Index
			select i).ToList();
		if (desiredIndex.HasValue)
		{
			MediaStream val = source.FirstOrDefault((MediaStream s) => s.Index == desiredIndex.Value);
			if (val != null)
			{
				return val;
			}
		}
		if (returnFirstIfNoIndex && (int)type == 0)
		{
			return source.FirstOrDefault((MediaStream i) => i.Channels.HasValue && i.Channels.Value > 0) ?? source.FirstOrDefault();
		}
		if (!returnFirstIfNoIndex)
		{
			return null;
		}
		return source.FirstOrDefault();
	}

	public string GetGraphicalSubtitleParam(EncodingJobInfo state, EncodingOptions options, string outputVideoCodec)
	{
		string text = string.Empty;
		BaseEncodingJobOptions baseRequest = state.BaseRequest;
		if (baseRequest.Width.HasValue || baseRequest.Height.HasValue || baseRequest.MaxHeight.HasValue || baseRequest.MaxWidth.HasValue)
		{
			text = GetOutputSizeParam(state, options, outputVideoCodec).TrimEnd(new char[1] { '"' });
			if (string.Equals(outputVideoCodec, "h264_vaapi", StringComparison.OrdinalIgnoreCase))
			{
				int num = text.IndexOf("format", StringComparison.OrdinalIgnoreCase);
				if (num != -1)
				{
					text = "," + text.Substring(num);
				}
			}
			else
			{
				int num2 = text.IndexOf("scale", StringComparison.OrdinalIgnoreCase);
				if (num2 != -1)
				{
					text = "," + text.Substring(num2);
				}
			}
		}
		if (string.Equals(outputVideoCodec, "h264_vaapi", StringComparison.OrdinalIgnoreCase) && text.Length == 0)
		{
			text = ",format=nv12|vaapi,hwupload";
		}
		string text2 = string.Empty;
		if (state.VideoStream != null && state.VideoStream.Width.HasValue && state.VideoStream.Height.HasValue)
		{
			text2 = $"scale={state.VideoStream.Width.Value.ToString(_usCulture)}:{state.VideoStream.Height.Value.ToString(_usCulture)}";
			text2 += ":force_original_aspect_ratio=decrease";
		}
		int num3 = (state.SubtitleStream.IsExternal ? 1 : 0);
		int num4 = ((!state.SubtitleStream.IsExternal) ? state.SubtitleStream.Index : 0);
		return string.Format(" -filter_complex \"[{0}:{1}]{4}[sub];[0:{2}][sub]overlay{3}\"", num3.ToString(_usCulture), num4.ToString(_usCulture), state.VideoStream.Index.ToString(_usCulture), text, text2);
	}

	private (int?, int?) GetFixedOutputSize(int? videoWidth, int? videoHeight, int? requestedWidth, int? requestedHeight, int? requestedMaxWidth, int? requestedMaxHeight)
	{
		if (!videoWidth.HasValue && !requestedWidth.HasValue)
		{
			return (null, null);
		}
		if (!videoHeight.HasValue && !requestedHeight.HasValue)
		{
			return (null, null);
		}
		decimal num = Convert.ToDecimal(videoWidth ?? requestedWidth);
		decimal num2 = Convert.ToDecimal(videoHeight ?? requestedHeight);
		decimal num3 = (requestedWidth.HasValue ? Convert.ToDecimal(requestedWidth.Value) : num);
		decimal num4 = (requestedHeight.HasValue ? Convert.ToDecimal(requestedHeight.Value) : num2);
		decimal num5 = (requestedMaxWidth.HasValue ? Convert.ToDecimal(requestedMaxWidth.Value) : num3);
		decimal num6 = (requestedMaxHeight.HasValue ? Convert.ToDecimal(requestedMaxHeight.Value) : num4);
		if (num3 > num5 || num4 > num6)
		{
			decimal num7 = Math.Min(num5 / num3, num6 / num4);
			num3 = Math.Min(num5, Math.Truncate(num3 * num7));
			num4 = Math.Min(num6, Math.Truncate(num4 * num7));
		}
		num3 = 2m * Math.Truncate(num3 / 2m);
		num4 = 2m * Math.Truncate(num4 / 2m);
		return (Convert.ToInt32(num3), Convert.ToInt32(num4));
	}

	public List<string> GetScalingFilters(int? videoWidth, int? videoHeight, Video3DFormat? threedFormat, string videoDecoder, string videoEncoder, int? requestedWidth, int? requestedHeight, int? requestedMaxWidth, int? requestedMaxHeight)
	{
		List<string> list = new List<string>();
		(int?, int?) fixedOutputSize = GetFixedOutputSize(videoWidth, videoHeight, requestedWidth, requestedHeight, requestedMaxWidth, requestedMaxHeight);
		if (string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase) && fixedOutputSize.Item1.HasValue && fixedOutputSize.Item2.HasValue)
		{
			string arg = "scale_vaapi";
			int value = fixedOutputSize.Item1.Value;
			int value2 = fixedOutputSize.Item2.Value;
			if (!videoWidth.HasValue || value != videoWidth.Value || !videoHeight.HasValue || value2 != videoHeight.Value)
			{
				list.Add($"{arg}=w={value.ToString(_usCulture)}:h={value2.ToString(_usCulture)}");
			}
		}
		else if ((videoDecoder ?? string.Empty).IndexOf("_cuvid", StringComparison.OrdinalIgnoreCase) == -1 || !fixedOutputSize.Item1.HasValue || !fixedOutputSize.Item2.HasValue)
		{
			bool flag = string.Equals(videoEncoder, "h264_v4l2m2m", StringComparison.OrdinalIgnoreCase);
			if (requestedWidth.HasValue && requestedHeight.HasValue)
			{
				if (flag)
				{
					string arg2 = requestedWidth.Value.ToString(_usCulture);
					string arg3 = requestedHeight.Value.ToString(_usCulture);
					list.Add($"scale=trunc({arg2}/64)*64:trunc({arg3}/2)*2");
				}
				else
				{
					list.Add(GetFixedSizeScalingFilter(threedFormat, requestedWidth.Value, requestedHeight.Value));
				}
			}
			else if (requestedMaxWidth.HasValue && requestedMaxHeight.HasValue)
			{
				string arg4 = requestedMaxWidth.Value.ToString(_usCulture);
				string arg5 = requestedMaxHeight.Value.ToString(_usCulture);
				if (flag)
				{
					list.Add(string.Format("scale=trunc(min(max(iw\\,ih*dar)\\,min({0}\\,{1}*dar))/64)*64:trunc(min(max(iw/dar\\,ih)\\,min({0}/dar\\,{1}))/2)*2", arg4, arg5));
				}
				else
				{
					list.Add(string.Format("scale=trunc(min(max(iw\\,ih*dar)\\,min({0}\\,{1}*dar))/2)*2:trunc(min(max(iw/dar\\,ih)\\,min({0}/dar\\,{1}))/2)*2", arg4, arg5));
				}
			}
			else if (requestedWidth.HasValue)
			{
				if (threedFormat.HasValue)
				{
					list.Add(GetFixedSizeScalingFilter(threedFormat, requestedWidth.Value, 0));
				}
				else
				{
					string arg6 = requestedWidth.Value.ToString(_usCulture);
					list.Add($"scale={arg6}:trunc(ow/a/2)*2");
				}
			}
			else if (requestedHeight.HasValue)
			{
				string arg7 = requestedHeight.Value.ToString(_usCulture);
				if (flag)
				{
					list.Add($"scale=trunc(oh*a/64)*64:{arg7}");
				}
				else
				{
					list.Add($"scale=trunc(oh*a/2)*2:{arg7}");
				}
			}
			else if (requestedMaxWidth.HasValue)
			{
				string arg8 = requestedMaxWidth.Value.ToString(_usCulture);
				if (flag)
				{
					list.Add($"scale=trunc(min(max(iw\\,ih*dar)\\,{arg8})/64)*64:trunc(ow/dar/2)*2");
				}
				else
				{
					list.Add($"scale=trunc(min(max(iw\\,ih*dar)\\,{arg8})/2)*2:trunc(ow/dar/2)*2");
				}
			}
			else if (requestedMaxHeight.HasValue)
			{
				string arg9 = requestedMaxHeight.Value.ToString(_usCulture);
				if (flag)
				{
					list.Add($"scale=trunc(oh*a/64)*64:min(max(iw/dar\\,ih)\\,{arg9})");
				}
				else
				{
					list.Add($"scale=trunc(oh*a/2)*2:min(max(iw/dar\\,ih)\\,{arg9})");
				}
			}
		}
		return list;
	}

	private string GetFixedSizeScalingFilter(Video3DFormat? threedFormat, int requestedWidth, int requestedHeight)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Expected I4, but got Unknown
		string arg = requestedWidth.ToString(_usCulture);
		string arg2 = requestedHeight.ToString(_usCulture);
		string text = null;
		if (threedFormat.HasValue)
		{
			Video3DFormat value = threedFormat.Value;
			switch ((int)value)
			{
			case 0:
				text = "crop=iw/2:ih:0:0,scale=(iw*2):ih,setdar=dar=a,crop=min(iw\\,ih*dar):min(ih\\,iw/dar):(iw-min(iw\\,iw*sar))/2:(ih - min (ih\\,ih/sar))/2,setsar=sar=1,scale={0}:trunc({0}/dar/2)*2";
				break;
			case 1:
				text = "crop=iw/2:ih:0:0,setdar=dar=a,crop=min(iw\\,ih*dar):min(ih\\,iw/dar):(iw-min(iw\\,iw*sar))/2:(ih - min (ih\\,ih/sar))/2,setsar=sar=1,scale={0}:trunc({0}/dar/2)*2";
				break;
			case 3:
				text = "crop=iw:ih/2:0:0,scale=(iw*2):ih),setdar=dar=a,crop=min(iw\\,ih*dar):min(ih\\,iw/dar):(iw-min(iw\\,iw*sar))/2:(ih - min (ih\\,ih/sar))/2,setsar=sar=1,scale={0}:trunc({0}/dar/2)*2";
				break;
			case 2:
				text = "crop=iw:ih/2:0:0,setdar=dar=a,crop=min(iw\\,ih*dar):min(ih\\,iw/dar):(iw-min(iw\\,iw*sar))/2:(ih - min (ih\\,ih/sar))/2,setsar=sar=1,scale={0}:trunc({0}/dar/2)*2";
				break;
			}
		}
		if (text == null)
		{
			text = ((requestedHeight <= 0) ? "scale={0}:trunc({0}/dar/2)*2" : "scale=trunc({0}/2)*2:trunc({1}/2)*2");
		}
		return string.Format(text, arg, arg2);
	}

	public string GetOutputSizeParam(EncodingJobInfo state, EncodingOptions options, string outputVideoCodec, bool allowTimeStampCopy = true)
	{
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		BaseEncodingJobOptions baseRequest = state.BaseRequest;
		List<string> list = new List<string>();
		if (string.Equals(outputVideoCodec, "h264_vaapi", StringComparison.OrdinalIgnoreCase))
		{
			list.Add("format=nv12|vaapi");
			list.Add("hwupload");
		}
		if (state.DeInterlace("h264", forceDeinterlaceIfSourceIsInterlaced: true) && string.Equals(outputVideoCodec, "h264_vaapi", StringComparison.OrdinalIgnoreCase))
		{
			list.Add($"deinterlace_vaapi");
		}
		MediaStream videoStream = state.VideoStream;
		if (state.DeInterlace("h264", forceDeinterlaceIfSourceIsInterlaced: true) && !string.Equals(outputVideoCodec, "h264_vaapi", StringComparison.OrdinalIgnoreCase))
		{
			float? num = ((videoStream == null) ? ((float?)null) : videoStream.RealFrameRate);
			if (string.Equals(options.DeinterlaceMethod, "bobandweave", StringComparison.OrdinalIgnoreCase) && (num ?? 60f) <= 30f)
			{
				list.Add("yadif=1:-1:0");
			}
			else
			{
				list.Add("yadif=0:-1:0");
			}
		}
		int? videoWidth = ((videoStream == null) ? ((int?)null) : videoStream.Width);
		int? videoHeight = ((videoStream == null) ? ((int?)null) : videoStream.Height);
		Video3DFormat? video3DFormat = state.MediaSource.Video3DFormat;
		string hardwareAcceleratedVideoDecoder = GetHardwareAcceleratedVideoDecoder(state, options);
		list.AddRange(GetScalingFilters(videoWidth, videoHeight, video3DFormat, hardwareAcceleratedVideoDecoder, outputVideoCodec, baseRequest.Width, baseRequest.Height, baseRequest.MaxWidth, baseRequest.MaxHeight));
		string text = string.Empty;
		if (state.SubtitleStream != null && state.SubtitleStream.IsTextSubtitleStream && (int)state.SubtitleDeliveryMethod == 0)
		{
			string textSubtitleParam = GetTextSubtitleParam(state);
			list.Add(textSubtitleParam);
			if (allowTimeStampCopy)
			{
				text += " -copyts";
			}
		}
		if (list.Count > 0)
		{
			text += string.Format(" -vf \"{0}\"", string.Join(",", list.ToArray()));
		}
		return text;
	}

	public int GetNumberOfThreads(EncodingJobInfo state, EncodingOptions encodingOptions, string outputVideoCodec)
	{
		if (string.Equals(outputVideoCodec, "libvpx", StringComparison.OrdinalIgnoreCase))
		{
			return Math.Max(Environment.ProcessorCount - 1, 1);
		}
		int num = state.BaseRequest.CpuCoreLimit ?? encodingOptions.EncodingThreadCount;
		if (num <= 0 || num >= Environment.ProcessorCount)
		{
			return 0;
		}
		return num;
	}

	public void TryStreamCopy(EncodingJobInfo state)
	{
		if (state.VideoStream != null && CanStreamCopyVideo(state, state.VideoStream))
		{
			state.OutputVideoCodec = "copy";
		}
		else
		{
			User user = state.User;
			if (user != null && !user.Policy.EnableVideoPlaybackTranscoding)
			{
				state.OutputVideoCodec = "copy";
			}
		}
		if (state.AudioStream != null && CanStreamCopyAudio(state, state.AudioStream, state.SupportedAudioCodecs))
		{
			state.OutputAudioCodec = "copy";
			return;
		}
		User user2 = state.User;
		if (user2 != null && !user2.Policy.EnableAudioPlaybackTranscoding)
		{
			state.OutputAudioCodec = "copy";
		}
	}

	public static string GetProbeSizeArgument(int numInputFiles)
	{
		if (numInputFiles <= 1)
		{
			return "";
		}
		return "-probesize 1G";
	}

	public static string GetAnalyzeDurationArgument(int numInputFiles)
	{
		if (numInputFiles <= 1)
		{
			return "";
		}
		return "-analyzeduration 200M";
	}

	public string GetInputModifier(EncodingJobInfo state, EncodingOptions encodingOptions)
	{
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Invalid comparison between Unknown and I4
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Invalid comparison between Unknown and I4
		//IL_034b: Unknown result type (might be due to invalid IL or missing references)
		string text = string.Empty;
		int numInputFiles = ((state.PlayableStreamFileNames.Length == 0) ? 1 : state.PlayableStreamFileNames.Length);
		string probeSizeArgument = GetProbeSizeArgument(numInputFiles);
		string text2 = ((!state.MediaSource.AnalyzeDurationMs.HasValue) ? GetAnalyzeDurationArgument(numInputFiles) : ("-analyzeduration " + (state.MediaSource.AnalyzeDurationMs.Value * 1000).ToString(CultureInfo.InvariantCulture)));
		if (!string.IsNullOrEmpty(probeSizeArgument))
		{
			text = text + " " + probeSizeArgument;
		}
		if (!string.IsNullOrEmpty(text2))
		{
			text = text + " " + text2;
		}
		text = text.Trim();
		string userAgentParam = GetUserAgentParam(state);
		if (!string.IsNullOrEmpty(userAgentParam))
		{
			text = text + " " + userAgentParam;
		}
		text = text.Trim();
		text = text + " " + GetFastSeekCommandLineParameter(state.BaseRequest);
		text = text.Trim();
		if ((int)state.InputProtocol == 3)
		{
			text += " -rtsp_transport tcp -rtsp_transport udp -rtsp_flags prefer_tcp";
		}
		if (!string.IsNullOrEmpty(state.InputAudioSync))
		{
			text = text + " -async " + state.InputAudioSync;
		}
		if (!string.IsNullOrEmpty(state.InputVideoSync))
		{
			text = text + " -vsync " + state.InputVideoSync;
		}
		if (state.ReadInputAtNativeFramerate && (int)state.InputProtocol != 3)
		{
			text += " -re";
		}
		List<string> list = new List<string>();
		if (state.IgnoreInputDts)
		{
			list.Add("+igndts");
		}
		if (state.IgnoreInputIndex)
		{
			list.Add("+ignidx");
		}
		if (state.GenPtsInput)
		{
			list.Add("+genpts");
		}
		if (state.DiscardCorruptFramesInput)
		{
			list.Add("+discardcorrupt");
		}
		if (state.EnableFastSeekInput)
		{
			list.Add("+fastseek");
		}
		if (list.Count > 0)
		{
			text = text + " -fflags " + string.Join("", list.ToArray());
		}
		string hardwareAcceleratedVideoDecoder = GetHardwareAcceleratedVideoDecoder(state, encodingOptions);
		if (!string.IsNullOrEmpty(hardwareAcceleratedVideoDecoder))
		{
			text = text + " " + hardwareAcceleratedVideoDecoder;
			MediaStream videoStream = state.VideoStream;
			int? videoWidth = ((videoStream == null) ? ((int?)null) : videoStream.Width);
			int? videoHeight = ((videoStream == null) ? ((int?)null) : videoStream.Height);
			BaseEncodingJobOptions baseRequest = state.BaseRequest;
			(int?, int?) fixedOutputSize = GetFixedOutputSize(videoWidth, videoHeight, baseRequest.Width, baseRequest.Height, baseRequest.MaxWidth, baseRequest.MaxHeight);
			if ((hardwareAcceleratedVideoDecoder ?? string.Empty).IndexOf("_cuvid", StringComparison.OrdinalIgnoreCase) != -1 && fixedOutputSize.Item1.HasValue && fixedOutputSize.Item2.HasValue)
			{
				text += $" -resize {fixedOutputSize.Item1.Value.ToString(_usCulture)}x{fixedOutputSize.Item2.Value.ToString(_usCulture)}";
			}
		}
		if (state.IsVideoRequest)
		{
			string videoEncoder = GetVideoEncoder(state, encodingOptions);
			if (!string.Equals(state.InputContainer, "wtv", StringComparison.OrdinalIgnoreCase) && state.TranscodingType != TranscodingJobType.Progressive && state.EnableBreakOnNonKeyFrames(videoEncoder))
			{
				text += " -noaccurate_seek";
			}
			if (!string.IsNullOrEmpty(state.InputContainer) && (int)state.VideoType == 0 && string.IsNullOrEmpty(encodingOptions.HardwareAccelerationType))
			{
				string inputFormat = GetInputFormat(state.InputContainer);
				if (!string.IsNullOrEmpty(inputFormat))
				{
					text = text + " -f " + inputFormat;
				}
			}
		}
		if (state.MediaSource.RequiresLooping)
		{
			text += " -stream_loop -1 -reconnect_at_eof 1 -reconnect_streamed 1 -reconnect_delay_max 2";
		}
		return text;
	}

	public void AttachMediaSourceInfo(EncodingJobInfo state, MediaSourceInfo mediaSource, string requestedUrl)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Invalid comparison between Unknown and I4
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Invalid comparison between Unknown and I4
		//IL_0215: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Invalid comparison between Unknown and I4
		//IL_024c: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Invalid comparison between Unknown and I4
		//IL_0140: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_032d: Unknown result type (might be due to invalid IL or missing references)
		if (state == null)
		{
			throw new ArgumentNullException("state");
		}
		if (mediaSource == null)
		{
			throw new ArgumentNullException("mediaSource");
		}
		string mediaPath = mediaSource.Path;
		MediaProtocol inputProtocol = mediaSource.Protocol;
		if (!string.IsNullOrEmpty(mediaSource.EncoderPath) && mediaSource.EncoderProtocol.HasValue)
		{
			mediaPath = mediaSource.EncoderPath;
			inputProtocol = mediaSource.EncoderProtocol.Value;
		}
		state.MediaPath = mediaPath;
		state.InputProtocol = inputProtocol;
		state.InputContainer = mediaSource.Container;
		state.RunTimeTicks = mediaSource.RunTimeTicks;
		state.RemoteHttpHeaders = mediaSource.RequiredHttpHeaders;
		state.IsoType = mediaSource.IsoType;
		if (mediaSource.VideoType.HasValue)
		{
			state.VideoType = mediaSource.VideoType.Value;
			if ((int)mediaSource.VideoType.Value == 3 || (int)mediaSource.VideoType.Value == 2)
			{
				state.PlayableStreamFileNames = Video.QueryPlayableStreamFiles(state.MediaPath, mediaSource.VideoType.Value).Select(Path.GetFileName).ToArray();
			}
			else if ((int)mediaSource.VideoType.Value == 1 && state.IsoType == (IsoType?)1)
			{
				state.PlayableStreamFileNames = Video.QueryPlayableStreamFiles(state.MediaPath, (VideoType)3).Select(Path.GetFileName).ToArray();
			}
			else if ((int)mediaSource.VideoType.Value == 1 && state.IsoType == (IsoType?)0)
			{
				state.PlayableStreamFileNames = Video.QueryPlayableStreamFiles(state.MediaPath, (VideoType)2).Select(Path.GetFileName).ToArray();
			}
			else
			{
				state.PlayableStreamFileNames = Array.Empty<string>();
			}
		}
		else
		{
			state.PlayableStreamFileNames = Array.Empty<string>();
		}
		if (mediaSource.Timestamp.HasValue)
		{
			state.InputTimestamp = mediaSource.Timestamp.Value;
		}
		state.RunTimeTicks = mediaSource.RunTimeTicks;
		state.RemoteHttpHeaders = mediaSource.RequiredHttpHeaders;
		state.ReadInputAtNativeFramerate = mediaSource.ReadAtNativeFramerate;
		if (state.ReadInputAtNativeFramerate || ((int)mediaSource.Protocol == 0 && string.Equals(mediaSource.Container, "wtv", StringComparison.OrdinalIgnoreCase)))
		{
			state.InputVideoSync = "-1";
			state.InputAudioSync = "1";
		}
		if (string.Equals(mediaSource.Container, "wma", StringComparison.OrdinalIgnoreCase) || string.Equals(mediaSource.Container, "asf", StringComparison.OrdinalIgnoreCase))
		{
			state.InputAudioSync = "1";
		}
		List<MediaStream> mediaStreams = mediaSource.MediaStreams;
		if (state.IsVideoRequest)
		{
			BaseEncodingJobOptions baseRequest = state.BaseRequest;
			if (string.IsNullOrEmpty(baseRequest.VideoCodec))
			{
				if (string.IsNullOrEmpty(requestedUrl))
				{
					requestedUrl = "test." + baseRequest.Container;
				}
				baseRequest.VideoCodec = InferVideoCodec(requestedUrl);
			}
			state.VideoStream = GetMediaStream(mediaStreams, baseRequest.VideoStreamIndex, (MediaStreamType)1);
			state.SubtitleStream = GetMediaStream(mediaStreams, baseRequest.SubtitleStreamIndex, (MediaStreamType)2, returnFirstIfNoIndex: false);
			state.SubtitleDeliveryMethod = baseRequest.SubtitleMethod;
			state.AudioStream = GetMediaStream(mediaStreams, baseRequest.AudioStreamIndex, (MediaStreamType)0);
			if (state.SubtitleStream != null && !state.SubtitleStream.IsExternal)
			{
				state.InternalSubtitleStreamOffset = mediaStreams.Where((MediaStream i) =>
				{
					//IL_0001: Unknown result type (might be due to invalid IL or missing references)
					//IL_0007: Invalid comparison between Unknown and I4
					return (int)i.Type == 2 && !i.IsExternal;
				}).ToList().IndexOf(state.SubtitleStream);
			}
			EnforceResolutionLimit(state);
			NormalizeSubtitleEmbed(state);
		}
		else
		{
			state.AudioStream = GetMediaStream(mediaStreams, null, (MediaStreamType)0);
		}
		state.MediaSource = mediaSource;
		BaseEncodingJobOptions baseRequest2 = state.BaseRequest;
		if (!string.IsNullOrWhiteSpace(baseRequest2.AudioCodec))
		{
			List<string> list = baseRequest2.AudioCodec.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
			ShiftAudioCodecsIfNeeded(list, state.AudioStream);
			state.SupportedAudioCodecs = list.ToArray();
			baseRequest2.AudioCodec = state.SupportedAudioCodecs.FirstOrDefault((string i) => ((ITranscoderSupport)_mediaEncoder).CanEncodeToAudioCodec(i)) ?? state.SupportedAudioCodecs.FirstOrDefault();
		}
	}

	private void ShiftAudioCodecsIfNeeded(List<string> audioCodecs, MediaStream audioStream)
	{
		if (audioCodecs.Count < 2 || ((audioStream == null) ? 6 : (audioStream.Channels ?? 6)) >= 6)
		{
			return;
		}
		string[] shiftAudioCodecs = new string[2] { "ac3", "eac3" };
		if (!audioCodecs.All((string i) => shiftAudioCodecs.Contains(i, StringComparer.OrdinalIgnoreCase)))
		{
			while (shiftAudioCodecs.Contains(audioCodecs[0], StringComparer.OrdinalIgnoreCase))
			{
				string item = shiftAudioCodecs[0];
				audioCodecs.RemoveAt(0);
				audioCodecs.Add(item);
			}
		}
	}

	private void NormalizeSubtitleEmbed(EncodingJobInfo state)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Invalid comparison between Unknown and I4
		if (state.SubtitleStream != null && (int)state.SubtitleDeliveryMethod == 1 && string.Equals(state.SubtitleStream.Codec, "DVBSUB", StringComparison.OrdinalIgnoreCase))
		{
			state.SubtitleDeliveryMethod = (SubtitleDeliveryMethod)0;
		}
	}

	protected string GetHardwareAcceleratedVideoDecoder(EncodingJobInfo state, EncodingOptions encodingOptions)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		if (string.Equals(state.OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return GetHardwareAcceleratedVideoDecoder((VideoType)(((int?)state.MediaSource.VideoType) ?? 0), state.VideoStream, encodingOptions);
	}

	public string GetHardwareAcceleratedVideoDecoder(VideoType videoType, MediaStream videoStream, EncodingOptions encodingOptions)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		if ((int)videoType != 0)
		{
			return null;
		}
		if (videoStream != null && !string.IsNullOrEmpty(videoStream.Codec) && !string.IsNullOrEmpty(encodingOptions.HardwareAccelerationType))
		{
			if (string.Equals(encodingOptions.HardwareAccelerationType, "qsv", StringComparison.OrdinalIgnoreCase))
			{
				switch (videoStream.Codec.ToLower())
				{
				case "avc":
				case "h264":
					if (_mediaEncoder.SupportsDecoder("h264_qsv") && encodingOptions.HardwareDecodingCodecs.Contains("h264", StringComparer.OrdinalIgnoreCase))
					{
						if ((videoStream.BitDepth ?? 8) > 8)
						{
							encodingOptions.HardwareDecodingCodecs = Array.Empty<string>();
							return null;
						}
						return "-c:v h264_qsv ";
					}
					break;
				case "hevc":
				case "h265":
					if (_mediaEncoder.SupportsDecoder("hevc_qsv") && encodingOptions.HardwareDecodingCodecs.Contains("hevc", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v hevc_qsv ";
					}
					break;
				case "mpeg2video":
					if (_mediaEncoder.SupportsDecoder("mpeg2_qsv") && encodingOptions.HardwareDecodingCodecs.Contains("mpeg2video", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v mpeg2_qsv ";
					}
					break;
				case "vc1":
					if (_mediaEncoder.SupportsDecoder("vc1_qsv") && encodingOptions.HardwareDecodingCodecs.Contains("vc1", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v vc1_qsv ";
					}
					break;
				}
			}
			else if (string.Equals(encodingOptions.HardwareAccelerationType, "nvenc", StringComparison.OrdinalIgnoreCase))
			{
				switch (videoStream.Codec.ToLower())
				{
				case "avc":
				case "h264":
					if (_mediaEncoder.SupportsDecoder("h264_cuvid") && encodingOptions.HardwareDecodingCodecs.Contains("h264", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v h264_cuvid ";
					}
					break;
				case "hevc":
				case "h265":
					if (_mediaEncoder.SupportsDecoder("hevc_cuvid") && encodingOptions.HardwareDecodingCodecs.Contains("hevc", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v hevc_cuvid ";
					}
					break;
				case "mpeg2video":
					if (_mediaEncoder.SupportsDecoder("mpeg2_cuvid") && encodingOptions.HardwareDecodingCodecs.Contains("mpeg2video", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v mpeg2_cuvid ";
					}
					break;
				case "vc1":
					if (_mediaEncoder.SupportsDecoder("vc1_cuvid") && encodingOptions.HardwareDecodingCodecs.Contains("vc1", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v vc1_cuvid ";
					}
					break;
				case "mpeg4":
					if (_mediaEncoder.SupportsDecoder("mpeg4_cuvid") && encodingOptions.HardwareDecodingCodecs.Contains("mpeg4", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v mpeg4_cuvid ";
					}
					break;
				}
			}
			else if (string.Equals(encodingOptions.HardwareAccelerationType, "mediacodec", StringComparison.OrdinalIgnoreCase))
			{
				switch (videoStream.Codec.ToLower())
				{
				case "avc":
				case "h264":
					if (_mediaEncoder.SupportsDecoder("h264_mediacodec") && encodingOptions.HardwareDecodingCodecs.Contains("h264", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v h264_mediacodec ";
					}
					break;
				case "hevc":
				case "h265":
					if (_mediaEncoder.SupportsDecoder("hevc_mediacodec") && encodingOptions.HardwareDecodingCodecs.Contains("hevc", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v hevc_mediacodec ";
					}
					break;
				case "mpeg2video":
					if (_mediaEncoder.SupportsDecoder("mpeg2_mediacodec") && encodingOptions.HardwareDecodingCodecs.Contains("mpeg2video", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v mpeg2_mediacodec ";
					}
					break;
				case "mpeg4":
					if (_mediaEncoder.SupportsDecoder("mpeg4_mediacodec") && encodingOptions.HardwareDecodingCodecs.Contains("mpeg4", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v mpeg4_mediacodec ";
					}
					break;
				case "vp8":
					if (_mediaEncoder.SupportsDecoder("vp8_mediacodec") && encodingOptions.HardwareDecodingCodecs.Contains("vp8", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v vp8_mediacodec ";
					}
					break;
				case "vp9":
					if (_mediaEncoder.SupportsDecoder("vp9_mediacodec") && encodingOptions.HardwareDecodingCodecs.Contains("vp9", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v vp9_mediacodec ";
					}
					break;
				}
			}
			else if (string.Equals(encodingOptions.HardwareAccelerationType, "omx", StringComparison.OrdinalIgnoreCase))
			{
				switch (videoStream.Codec.ToLower())
				{
				case "avc":
				case "h264":
					if (_mediaEncoder.SupportsDecoder("h264_mmal") && encodingOptions.HardwareDecodingCodecs.Contains("h264", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v h264_mmal";
					}
					break;
				case "mpeg2video":
					if (_mediaEncoder.SupportsDecoder("mpeg2_mmal") && encodingOptions.HardwareDecodingCodecs.Contains("mpeg2video", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v mpeg2_mmal";
					}
					break;
				}
			}
			else if (string.Equals(encodingOptions.HardwareAccelerationType, "amf", StringComparison.OrdinalIgnoreCase))
			{
				if (Environment.OSVersion.Platform == PlatformID.Win32NT)
				{
					return "-hwaccel dxva2";
				}
				switch (videoStream.Codec.ToLower())
				{
				case "avc":
				case "h264":
					if (_mediaEncoder.SupportsDecoder("h264_amf") && encodingOptions.HardwareDecodingCodecs.Contains("h264", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v h264_amf";
					}
					break;
				case "mpeg2video":
					if (_mediaEncoder.SupportsDecoder("hevc_amf") && encodingOptions.HardwareDecodingCodecs.Contains("mpeg2video", StringComparer.OrdinalIgnoreCase))
					{
						return "-c:v mpeg2_mmal";
					}
					break;
				}
			}
		}
		encodingOptions.HardwareDecodingCodecs = Array.Empty<string>();
		return null;
	}

	public string GetSubtitleEmbedArguments(EncodingJobInfo state)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Invalid comparison between Unknown and I4
		if (state.SubtitleStream == null || (int)state.SubtitleDeliveryMethod != 1)
		{
			return string.Empty;
		}
		string text = state.SupportedSubtitleCodecs.FirstOrDefault();
		string text2 = ((!string.IsNullOrEmpty(text) && !string.Equals(text, state.SubtitleStream.Codec, StringComparison.OrdinalIgnoreCase)) ? text : "copy");
		return string.Concat(" -codec:s:0 " + text2, " -disposition:s:0 default");
	}

	public string GetProgressiveVideoFullCommandLine(EncodingJobInfo state, EncodingOptions encodingOptions, string outputPath, string defaultH264Preset)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		string videoEncoder = GetVideoEncoder(state, encodingOptions);
		string text = string.Empty;
		string empty = string.Empty;
		if (string.Equals(Path.GetExtension(outputPath), ".mp4", StringComparison.OrdinalIgnoreCase) && (int)state.BaseRequest.Context == 0)
		{
			text = " -f mp4 -movflags frag_keyframe+empty_moov";
		}
		int numberOfThreads = GetNumberOfThreads(state, encodingOptions, videoEncoder);
		string inputModifier = GetInputModifier(state, encodingOptions);
		return $"{inputModifier} {GetInputArgument(state, encodingOptions)}{empty} {GetMapArgs(state)} {GetProgressiveVideoArguments(state, encodingOptions, videoEncoder, defaultH264Preset)} -map_metadata -1 -map_chapters -1 -threads {numberOfThreads} {GetProgressiveVideoAudioArguments(state, encodingOptions)}{GetSubtitleEmbedArguments(state)}{text} -y \"{outputPath}\"".Trim();
	}

	public string GetOutputFFlags(EncodingJobInfo state)
	{
		List<string> list = new List<string>();
		if (state.GenPtsOutput)
		{
			list.Add("+genpts");
		}
		if (list.Count > 0)
		{
			return " -fflags " + string.Join("", list.ToArray());
		}
		return string.Empty;
	}

	public string GetProgressiveVideoArguments(EncodingJobInfo state, EncodingOptions encodingOptions, string videoCodec, string defaultH264Preset)
	{
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Invalid comparison between Unknown and I4
		string text = "-codec:v:0 " + videoCodec;
		if (state.BaseRequest.EnableMpegtsM2TsMode)
		{
			text += " -mpegts_m2ts_mode 1";
		}
		if (string.Equals(videoCodec, "copy", StringComparison.OrdinalIgnoreCase))
		{
			if (state.VideoStream != null && IsH264(state.VideoStream) && string.Equals(state.OutputContainer, "ts", StringComparison.OrdinalIgnoreCase) && !string.Equals(state.VideoStream.NalLengthSize, "0", StringComparison.OrdinalIgnoreCase))
			{
				text += " -bsf:v h264_mp4toannexb";
			}
			if (state.RunTimeTicks.HasValue && state.BaseRequest.CopyTimestamps)
			{
				text += " -copyts -avoid_negative_ts disabled -start_at_zero";
			}
			if (!state.RunTimeTicks.HasValue)
			{
				text += " -flags -global_header -fflags +genpts";
			}
		}
		else
		{
			string text2 = $" -force_key_frames \"expr:gte(t,n_forced*{5.ToString(_usCulture)})\"";
			text += text2;
			bool num = state.SubtitleStream != null && !state.SubtitleStream.IsTextSubtitleStream && (int)state.SubtitleDeliveryMethod == 0;
			bool flag = false;
			if (!num)
			{
				string outputSizeParam = GetOutputSizeParam(state, encodingOptions, videoCodec);
				text += outputSizeParam;
				flag = outputSizeParam.IndexOf("copyts", StringComparison.OrdinalIgnoreCase) != -1;
			}
			if (state.RunTimeTicks.HasValue && state.BaseRequest.CopyTimestamps)
			{
				if (!flag)
				{
					text += " -copyts";
				}
				text += " -avoid_negative_ts disabled -start_at_zero";
			}
			if (num)
			{
				text += GetGraphicalSubtitleParam(state, encodingOptions, videoCodec);
			}
			string videoQualityParam = GetVideoQualityParam(state, videoCodec, encodingOptions, defaultH264Preset);
			if (!string.IsNullOrEmpty(videoQualityParam))
			{
				text = text + " " + videoQualityParam.Trim();
			}
			if (!state.RunTimeTicks.HasValue)
			{
				text += " -flags -global_header";
			}
		}
		if (!string.IsNullOrEmpty(state.OutputVideoSync))
		{
			text = text + " -vsync " + state.OutputVideoSync;
		}
		return text + GetOutputFFlags(state);
	}

	public string GetProgressiveVideoAudioArguments(EncodingJobInfo state, EncodingOptions encodingOptions)
	{
		if (state.AudioStream == null && state.VideoStream != null)
		{
			return string.Empty;
		}
		string audioEncoder = GetAudioEncoder(state);
		string text = "-codec:a:0 " + audioEncoder;
		if (string.Equals(audioEncoder, "copy", StringComparison.OrdinalIgnoreCase))
		{
			return text;
		}
		int? outputAudioChannels = state.OutputAudioChannels;
		if (outputAudioChannels.HasValue)
		{
			text = text + " -ac " + outputAudioChannels.Value;
		}
		int? outputAudioBitrate = state.OutputAudioBitrate;
		if (outputAudioBitrate.HasValue)
		{
			text = text + " -ab " + outputAudioBitrate.Value.ToString(_usCulture);
		}
		if (state.OutputAudioSampleRate.HasValue)
		{
			text = text + " -ar " + state.OutputAudioSampleRate.Value.ToString(_usCulture);
		}
		return text + " " + GetAudioFilterParam(state, encodingOptions, isHls: false);
	}

	public string GetProgressiveAudioFullCommandLine(EncodingJobInfo state, EncodingOptions encodingOptions, string outputPath)
	{
		List<string> list = new List<string>();
		int? outputAudioBitrate = state.OutputAudioBitrate;
		if (outputAudioBitrate.HasValue)
		{
			list.Add("-ab " + outputAudioBitrate.Value.ToString(_usCulture));
		}
		if (state.OutputAudioChannels.HasValue)
		{
			list.Add("-ac " + state.OutputAudioChannels.Value.ToString(_usCulture));
		}
		if (!string.Equals(state.OutputAudioCodec, "opus", StringComparison.OrdinalIgnoreCase) && state.OutputAudioSampleRate.HasValue)
		{
			list.Add("-ar " + state.OutputAudioSampleRate.Value.ToString(_usCulture));
		}
		string text = string.Empty;
		string text2 = string.Empty;
		string text3 = string.Empty;
		string text4 = string.Empty;
		string.IsNullOrEmpty(state.AlbumCoverPath);
		if (false)
		{
			text = " -i \"" + state.AlbumCoverPath + "\"";
			text2 = " -map 0:a -map 1:v -c:1:v copy";
			text3 = " -metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover(Front)\"";
		}
		else
		{
			text4 = " -vn";
		}
		int numberOfThreads = GetNumberOfThreads(state, encodingOptions, null);
		string inputModifier = GetInputModifier(state, encodingOptions);
		return string.Format("{0} {1}{7}{8} -threads {2}{3} {4} -id3v2_version 3 -write_id3v1 1{6} -y \"{5}\"", inputModifier, GetInputArgument(state, encodingOptions), numberOfThreads, text4, string.Join(" ", list.ToArray()), outputPath, text3, text, text2).Trim();
	}
}
