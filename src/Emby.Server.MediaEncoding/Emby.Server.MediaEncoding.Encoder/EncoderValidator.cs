using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.Logging;

namespace Emby.Server.MediaEncoding.Encoder;

public class EncoderValidator
{
	private readonly ILogger _logger;

	private readonly IProcessFactory _processFactory;

	public EncoderValidator(ILogger logger, IProcessFactory processFactory)
	{
		_logger = logger;
		_processFactory = processFactory;
	}

	public Tuple<List<string>, List<string>, List<string>> Validate(string encoderPath)
	{
		_logger.Info("Validating media encoder at {0}", new object[1] { encoderPath });
		List<string> decoders = GetDecoders(encoderPath);
		List<string> encoders = GetEncoders(encoderPath);
		List<string> inputProtocols = GetInputProtocols(encoderPath);
		_logger.Info("Encoder validation complete", Array.Empty<object>());
		return new Tuple<List<string>, List<string>, List<string>>(decoders, encoders, inputProtocols);
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
		text = " " + text + " ";
		for (int i = 2013; i <= 2015; i++)
		{
			string text2 = i.ToString(CultureInfo.InvariantCulture);
			if (text.IndexOf(" " + text2 + " ", StringComparison.OrdinalIgnoreCase) != -1)
			{
				return false;
			}
		}
		return true;
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
		List<string> list = new List<string>();
		string[] array = new string[32]
		{
			"mpeg2video", "h264_qsv", "hevc_qsv", "mpeg2_qsv", "vc1_qsv", "h264_cuvid", "hevc_cuvid", "mpeg2_cuvid", "mpeg4_cuvid", "vc1_cuvid",
			"vp8_cuvid", "vp9_cuvid", "h264_mediacodec", "hevc_mediacodec", "mpeg2_mediacodec", "mpeg4_mediacodec", "vp8_mediacodec", "vp9_mediacodec", "dts", "ac3",
			"aac", "mp3", "h264", "hevc", "h264_mmal", "mpeg2_mmal", "h264_amf", "hevc_amf", "h264_vaapi", "hevc_vaapi",
			"mpeg2_vaapi", "vp8_vaapi"
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
		string[] array = new string[26]
		{
			"libx264", "libx265", "mpeg4", "msmpeg4", "libvpx", "libvpx-vp9", "aac", "libmp3lame", "libopus", "libvorbis",
			"srt", "h264_nvenc", "hevc_nvenc", "h264_qsv", "hevc_qsv", "h264_omx", "hevc_omx", "h264_mediacodec", "h264_mediacodecndk", "hevc_mediacodec",
			"h264_vaapi", "hevc_vaapi", "h264_v4l2m2m", "libwebp", "ac3", "h264_amf"
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
