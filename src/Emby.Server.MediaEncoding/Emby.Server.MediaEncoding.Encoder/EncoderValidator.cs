using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Common.MediaEncoding;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.Logging;

namespace Emby.Server.MediaEncoding.Encoder;

public class EncoderValidator
{
	private static readonly Regex VersionRegex = new Regex(
		@"^(?:ffmpeg|ffprobe) version n?(?<version>\d+(?:\.\d+)*)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	// -filters 形如 " .. libplacebo   N->V ..." / " .S tonemap   V->V ..."：
	// 前导空格 + 2~3 个标志位（. / T / S / A / |）+ 名称 + 输入->输出。
	private static readonly Regex FilterLineRegex = new Regex(
		@"^\s+[.TSA|]{2,3}\s+(?<name>[\w-]+)\s+[NAV]->[NAV]",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	// 滤镜帮助写作 "   tonemap   <int> ..."，bsf 帮助写作 "  -remove_dovi  <boolean> ..."。
	private static readonly Regex OptionLineRegex = new Regex(
		@"^\s+-?[\w-]+\s+<[^>]+>",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	private readonly ILogger _logger;

	private readonly IProcessFactory _processFactory;

	private readonly ConcurrentDictionary<string, string> _filterHelp = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly ConcurrentDictionary<string, string> _bitStreamFilterHelp = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private string _encoderAppPath = string.Empty;

	private Version? _version;

	private bool _versionFetched;

	private HashSet<string> _filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private HashSet<string> _hwaccels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private HashSet<string> _bitStreamFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public EncoderValidator(ILogger logger, IProcessFactory processFactory)
	{
		_logger = logger;
		_processFactory = processFactory;
	}

	public static Version MinimumEncoderVersion => FFmpegConstants.MinimumEncoderVersion;

	/// <summary>Validate() 探测到的能力集合；参考 Jellyfin 的 filter/hwaccel/bsf 分类。</summary>
	public IReadOnlyCollection<string> Filters => _filters;

	public IReadOnlyCollection<string> Hwaccels => _hwaccels;

	public IReadOnlyCollection<string> BitStreamFilters => _bitStreamFilters;

	public Tuple<List<string>, List<string>, List<string>> Validate(string encoderPath)
	{
		_logger.Info("Validating media encoder at {0}", new object[1] { encoderPath });
		_encoderAppPath = encoderPath ?? string.Empty;
		_version = GetMediaEncoderVersion(encoderPath);
		_versionFetched = true;
		List<string> decoders = GetDecoders(encoderPath);
		List<string> encoders = GetEncoders(encoderPath);
		List<string> inputProtocols = GetInputProtocols(encoderPath);
		_filters = GetFilters(encoderPath);
		_hwaccels = GetHwaccels(encoderPath);
		_bitStreamFilters = GetBitStreamFilters(encoderPath);
		_logger.Info("Encoder validation complete", Array.Empty<object>());
		return new Tuple<List<string>, List<string>, List<string>>(decoders, encoders, inputProtocols);
	}

	/// <summary>返回探测到的版本；未 Validate() 过则按需探测一次。</summary>
	public Version? GetMediaEncoderVersion()
	{
		if (!_versionFetched && !string.IsNullOrWhiteSpace(_encoderAppPath))
		{
			_version = GetMediaEncoderVersion(_encoderAppPath);
			_versionFetched = true;
		}

		return _version;
	}

	/// <summary>解析 "ffmpeg version n?X.Y.Z..." 首行；无法解析时返回 null。</summary>
	public Version? GetMediaEncoderVersion(string encoderAppPath)
	{
		string text;
		try
		{
			text = GetProcessOutput(encoderAppPath, "-version");
		}
		catch (Exception ex)
		{
			_logger.ErrorException("Error reading encoder version", ex, Array.Empty<object>());
			return null;
		}

		return ParseVersion(text);
	}

	/// <summary>版本未知或低于下限都直接抛错，禁止调用方静默降级到旧 ffmpeg。</summary>
	public void EnsureEncoderVersion()
	{
		Version? version = GetMediaEncoderVersion();
		if (version is null)
		{
			throw new InvalidOperationException($"Cannot determine ffmpeg version at '{_encoderAppPath}'. ffmpeg {MinimumEncoderVersion} or newer is required.");
		}

		if (version < MinimumEncoderVersion)
		{
			throw new InvalidOperationException($"ffmpeg version {version} at '{_encoderAppPath}' is below the minimum supported version {MinimumEncoderVersion}.");
		}
	}

	public bool ValidateVersion(string encoderAppPath, bool logOutput)
	{
		string text = string.Empty;
		try
		{
			text = GetProcessOutput(encoderAppPath, "-version");
		}
		catch (Exception ex)
		{
			if (logOutput)
			{
				_logger.ErrorException("Error validating encoder", ex, Array.Empty<object>());
			}
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		_logger.Info("ffmpeg info: {0}", new object[1] { text });
		if (text.IndexOf("Libav developers", StringComparison.OrdinalIgnoreCase) != -1)
		{
			return false;
		}

		Version? version = ParseVersion(text);
		if (version is null)
		{
			if (logOutput)
			{
				_logger.Error("ffmpeg validation: cannot parse version from {0}", new object[1] { text.Split('\n')[0] });
			}

			return false;
		}

		if (version < MinimumEncoderVersion)
		{
			if (logOutput)
			{
				_logger.Error("ffmpeg validation: version {0} is below the minimum supported version {1}", new object[2] { version, MinimumEncoderVersion });
			}

			return false;
		}

		return true;
	}

	public bool SupportsFilter(string filter)
	{
		return !string.IsNullOrWhiteSpace(filter) && _filters.Contains(filter);
	}

	public bool SupportsFilterWithOption(string filter, string option, string value = null)
	{
		if (!SupportsFilter(filter) || string.IsNullOrWhiteSpace(option))
		{
			return false;
		}

		string help = _filterHelp.GetOrAdd(filter, key => ReadHelp("-h filter=" + key));
		return HasOption(help, "Filter " + filter, option, value);
	}

	public bool SupportsHwaccel(string hwaccel)
	{
		return !string.IsNullOrWhiteSpace(hwaccel) && _hwaccels.Contains(hwaccel);
	}

	public bool SupportsBitStreamFilter(string name)
	{
		return !string.IsNullOrWhiteSpace(name) && _bitStreamFilters.Contains(name);
	}

	public bool SupportsBitStreamFilterWithOption(string name, string option, string value = null)
	{
		if (!SupportsBitStreamFilter(name) || string.IsNullOrWhiteSpace(option))
		{
			return false;
		}

		string help = _bitStreamFilterHelp.GetOrAdd(name, key => ReadHelp("-h bsf=" + key));
		return HasOption(help, "Bit stream filter " + name, option, value);
	}

	private static Version? ParseVersion(string output)
	{
		if (string.IsNullOrWhiteSpace(output))
		{
			return null;
		}

		Match match = VersionRegex.Match(output);
		return match.Success && Version.TryParse(match.Groups["version"].Value, out Version? version) ? version : null;
	}

	/// <summary>
	/// 在 -h filter/bsf 输出中查找选项。非空 value 只有在帮助文本列出固定枚举值时才校验取值，
	/// 字符串/数值选项（帮助里没有枚举列表）只校验选项名，避免把任意合法字符串误判为不支持。
	/// </summary>
	private static bool HasOption(string help, string expectedHeader, string option, string? value)
	{
		if (string.IsNullOrWhiteSpace(help)
			|| help.IndexOf(expectedHeader, StringComparison.OrdinalIgnoreCase) == -1)
		{
			return false;
		}

		string[] lines = help.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		Regex optionRegex = new Regex(@"^\s+-?" + Regex.Escape(option) + @"\s+<", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		int optionLine = -1;
		for (int i = 0; i < lines.Length; i++)
		{
			if (optionRegex.IsMatch(lines[i]))
			{
				optionLine = i;
				break;
			}
		}

		if (optionLine == -1)
		{
			return false;
		}

		if (string.IsNullOrWhiteSpace(value))
		{
			return true;
		}

		if (lines[optionLine].IndexOf("<boolean>", StringComparison.OrdinalIgnoreCase) != -1)
		{
			return bool.TryParse(value, out _);
		}

		var candidates = new List<string> { lines[optionLine] };
		for (int i = optionLine + 1; i < lines.Length; i++)
		{
			if (OptionLineRegex.IsMatch(lines[i]))
			{
				break;
			}

			candidates.Add(lines[i]);
		}

		// 没有枚举行 => 字符串/数值选项，取值由 ffmpeg 自行决定。
		if (candidates.Count == 1)
		{
			return true;
		}

		Regex valueRegex = new Regex(@"(?<![\w-])" + Regex.Escape(value) + @"(?![\w-])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		return candidates.Any(line => valueRegex.IsMatch(line));
	}

	private string ReadHelp(string arguments)
	{
		if (string.IsNullOrWhiteSpace(_encoderAppPath))
		{
			return string.Empty;
		}

		try
		{
			return GetProcessOutput(_encoderAppPath, arguments);
		}
		catch (Exception ex)
		{
			_logger.ErrorException("Error probing encoder capability: {0}", ex, new object[1] { arguments });
			return string.Empty;
		}
	}

	private HashSet<string> GetFilters(string encoderAppPath)
	{
		string text = string.Empty;
		try
		{
			text = GetProcessOutput(encoderAppPath, "-filters");
		}
		catch (Exception ex)
		{
			_logger.ErrorException("Error probing ffmpeg filters", ex, Array.Empty<object>());
		}

		var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
		{
			Match match = FilterLineRegex.Match(line);
			if (match.Success)
			{
				string name = match.Groups["name"].Value;
				filters.Add(name);
				_logger.Info("Filter available: {0}", new object[1] { name });
			}
		}

		return filters;
	}

	private HashSet<string> GetHwaccels(string encoderAppPath)
	{
		string text = string.Empty;
		try
		{
			text = GetProcessOutput(encoderAppPath, "-hwaccels");
		}
		catch (Exception ex)
		{
			_logger.ErrorException("Error probing ffmpeg hwaccels", ex, Array.Empty<object>());
		}

		var hwaccels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] lines = text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		int start = Array.FindIndex(lines, line => line.IndexOf("Hardware acceleration methods:", StringComparison.OrdinalIgnoreCase) != -1);
		if (start == -1)
		{
			return hwaccels;
		}

		for (int i = start + 1; i < lines.Length; i++)
		{
			string name = lines[i].Trim();
			if (name.Length > 0)
			{
				hwaccels.Add(name);
				_logger.Info("Hwaccel available: {0}", new object[1] { name });
			}
		}

		return hwaccels;
	}

	private HashSet<string> GetBitStreamFilters(string encoderAppPath)
	{
		string text = string.Empty;
		try
		{
			text = GetProcessOutput(encoderAppPath, "-bsfs");
		}
		catch (Exception ex)
		{
			_logger.ErrorException("Error probing ffmpeg bitstream filters", ex, Array.Empty<object>());
		}

		var bitStreamFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string name = line.Trim();
			if (name.Length == 0 || name.IndexOf("Bitstream filters:", StringComparison.OrdinalIgnoreCase) != -1)
			{
				continue;
			}

			bitStreamFilters.Add(name);
		}

		return bitStreamFilters;
	}

	private List<string> GetDecoders(string encoderAppPath)
	{
		string text = string.Empty;
		try
		{
			text = GetProcessOutput(encoderAppPath, "-decoders");
		}
		catch (Exception)
		{
		}
		// 旧列表仅维持既有调用方的行为；新的 HDR/硬解判断请用 SupportsFilter / SupportsHwaccel。
		List<string> list = new List<string>();
		string[] array = new string[38]
		{
			"mpeg2video", "h264_qsv", "hevc_qsv", "mpeg2_qsv", "vc1_qsv", "h264_cuvid", "hevc_cuvid", "mpeg2_cuvid", "mpeg4_cuvid", "vc1_cuvid",
			"vp8_cuvid", "vp9_cuvid", "h264_mediacodec", "hevc_mediacodec", "mpeg2_mediacodec", "mpeg4_mediacodec", "vp8_mediacodec", "vp9_mediacodec", "dts", "ac3",
			"aac", "mp3", "h264", "hevc", "h264_mmal", "mpeg2_mmal", "h264_amf", "hevc_amf", "h264_vaapi", "hevc_vaapi",
			"mpeg2_vaapi", "vp8_vaapi", "dca", "av1", "av1_qsv", "av1_cuvid", "av1_amf", "av1_rkmpp"
		};
		foreach (string text2 in array)
		{
			string value = " " + text2 + "  ";
			if (text.IndexOf(value, StringComparison.OrdinalIgnoreCase) != -1)
			{
				_logger.Info("Decoder available: " + text2, Array.Empty<object>());
				list.Add(text2);
			}
		}
		return list;
	}

	private List<string> GetEncoders(string encoderAppPath)
	{
		string text = null;
		try
		{
			text = GetProcessOutput(encoderAppPath, "-encoders");
		}
		catch
		{
		}
		List<string> list = new List<string>();
		string[] array = new string[32]
		{
			"libx264", "libx265", "mpeg4", "msmpeg4", "libvpx", "libvpx-vp9", "aac", "libmp3lame", "libopus", "libvorbis",
			"srt", "h264_nvenc", "hevc_nvenc", "h264_qsv", "hevc_qsv", "h264_omx", "hevc_omx", "h264_mediacodec", "h264_mediacodecndk", "hevc_mediacodec",
			"h264_vaapi", "hevc_vaapi", "h264_v4l2m2m", "libwebp", "ac3", "h264_amf",
			"hevc_amf", "libsvtav1", "av1_nvenc", "av1_qsv", "av1_vaapi", "av1_amf"
		};
		text = text ?? string.Empty;
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			string value = " " + text2 + "  ";
			if (text.IndexOf(value, StringComparison.OrdinalIgnoreCase) != -1)
			{
				_logger.Info("Encoder available: " + text2, Array.Empty<object>());
				list.Add(text2);
			}
		}
		return list;
	}

	private List<string> GetInputProtocols(string encoderAppPath)
	{
		string text = null;
		try
		{
			text = GetProcessOutput(encoderAppPath, "-protocols");
		}
		catch
		{
		}
		text = text ?? string.Empty;
		int num = text.IndexOf("Input:", StringComparison.OrdinalIgnoreCase);
		if (num != -1)
		{
			text = text.Substring(num + 6);
		}
		num = text.IndexOf("Output:", StringComparison.OrdinalIgnoreCase);
		if (num != -1)
		{
			text = text.Substring(0, num);
		}
		text = text.Replace(Environment.NewLine, string.Empty);
		return (from i in text.Split(',', ' ')
			select i.Trim() into i
			where !string.IsNullOrWhiteSpace(i)
			select i).ToList();
	}

	private string GetProcessOutput(string path, string arguments)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected Obj, but got Unknown
		IProcess val = _processFactory.Create(new ProcessOptions
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			FileName = path,
			Arguments = arguments,
			IsHidden = true,
			ErrorDialog = false,
			RedirectStandardOutput = true
		});
		_logger.Info("Running {0} {1}", new object[2] { path, arguments });
		IProcess val2 = val;
		try
		{
			val.Start();
			try
			{
				string result = val.StandardOutput.ReadToEnd();
				if (!val.WaitForExit(2000))
				{
					try
					{
						val.Kill();
					}
					catch (Exception ex)
					{
						_logger.ErrorException("Error killing process", ex, Array.Empty<object>());
					}
				}
				return result;
			}
			catch
			{
				_logger.Info("Killing process {0} {1}", new object[2] { path, arguments });
				try
				{
					val.Kill();
				}
				catch (Exception ex2)
				{
					_logger.ErrorException("Error killing process", ex2, Array.Empty<object>());
				}
				throw;
			}
		}
		finally
		{
			((IDisposable)val2)?.Dispose();
		}
	}
}
