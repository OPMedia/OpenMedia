using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Logging;

namespace Emby.Server.MediaEncoding.Encoder;

public class JobLogger
{
	private readonly CultureInfo _usCulture = new CultureInfo("en-US");

	private readonly ILogger _logger;

	public JobLogger(ILogger logger)
	{
		_logger = logger;
	}

	public async void StartStreamingLog(EncodingJobInfo state, Stream source, Stream target)
	{
		_ = 2;
		try
		{
			using StreamReader reader = new StreamReader(source);
			while (!reader.EndOfStream)
			{
				string text = await reader.ReadLineAsync().ConfigureAwait(continueOnCapturedContext: false);
				ParseLogLine(text, state);
				byte[] bytes = Encoding.UTF8.GetBytes(Environment.NewLine + text);
				await target.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(continueOnCapturedContext: false);
				await target.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception ex2)
		{
			_logger.ErrorException("Error reading ffmpeg log", ex2, Array.Empty<object>());
		}
	}

	private void ParseLogLine(string line, EncodingJobInfo state)
	{
		float num = 0f;
		double? num2 = null;
		TimeSpan? timeSpan = null;
		long num3 = 0L;
		int? num4 = null;
		string[] array = line.Split(' ');
		double num5 = (state.RunTimeTicks.HasValue ? TimeSpan.FromTicks(state.RunTimeTicks.Value).TotalMilliseconds : 0.0);
		double num6 = (state.BaseRequest.StartTimeTicks.HasValue ? TimeSpan.FromTicks(state.BaseRequest.StartTimeTicks.Value).TotalMilliseconds : 0.0);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i];
			if (string.Equals(text, "fps=", StringComparison.OrdinalIgnoreCase) && i + 1 < array.Length)
			{
				if (float.TryParse(array[i + 1], NumberStyles.Any, _usCulture, out var result))
				{
					num = result;
				}
			}
			else if (state.RunTimeTicks.HasValue && text.StartsWith("time=", StringComparison.OrdinalIgnoreCase))
			{
				if (TimeSpan.TryParse(text.Split(new char[1] { '=' }, 2).Last(), _usCulture, out var result2))
				{
					double num7 = (num6 + result2.TotalMilliseconds) / num5;
					num2 = 100.0 * num7;
					timeSpan = result2;
				}
			}
			else if (text.StartsWith("size=", StringComparison.OrdinalIgnoreCase))
			{
				string text2 = text.Split(new char[1] { '=' }, 2).Last();
				int? num8 = null;
				if (text2.IndexOf("kb", StringComparison.OrdinalIgnoreCase) != -1)
				{
					num8 = 1024;
					text2 = StringHelper.Replace(text2, "kb", string.Empty, StringComparison.OrdinalIgnoreCase);
				}
				if (num8.HasValue && long.TryParse(text2, NumberStyles.Any, _usCulture, out var result3))
				{
					num3 = result3 * num8.Value;
				}
			}
			else if (text.StartsWith("bitrate=", StringComparison.OrdinalIgnoreCase))
			{
				string text3 = text.Split(new char[1] { '=' }, 2).Last();
				int? num9 = null;
				if (text3.IndexOf("kbits/s", StringComparison.OrdinalIgnoreCase) != -1)
				{
					num9 = 1024;
					text3 = StringHelper.Replace(text3, "kbits/s", string.Empty, StringComparison.OrdinalIgnoreCase);
				}
				if (num9.HasValue && float.TryParse(text3, NumberStyles.Any, _usCulture, out var result4))
				{
					num4 = (int)Math.Ceiling(result4 * (float)num9.Value);
				}
			}
		}
		if (num > 0f || num2.HasValue)
		{
			state.ReportTranscodingProgress(timeSpan, num, num2, num3, num4);
		}
	}
}
