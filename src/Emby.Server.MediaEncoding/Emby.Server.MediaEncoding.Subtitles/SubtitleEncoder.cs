using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Text;

namespace Emby.Server.MediaEncoding.Subtitles;

public class SubtitleEncoder : ISubtitleEncoder
{
	private readonly ILibraryManager _libraryManager;

	private readonly ILogger _logger;

	private readonly IApplicationPaths _appPaths;

	private readonly IFileSystem _fileSystem;

	private readonly IMediaEncoder _mediaEncoder;

	private readonly IJsonSerializer _json;

	private readonly IHttpClient _httpClient;

	private readonly IMediaSourceManager _mediaSourceManager;

	private readonly IProcessFactory _processFactory;

	private readonly ITextEncoding _textEncoding;

	private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphoreLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

	private string SubtitleCachePath => Path.Combine(_appPaths.DataPath, "subtitles");

	public SubtitleEncoder(ILibraryManager libraryManager, ILogger logger, IApplicationPaths appPaths, IFileSystem fileSystem, IMediaEncoder mediaEncoder, IJsonSerializer json, IHttpClient httpClient, IMediaSourceManager mediaSourceManager, IProcessFactory processFactory, ITextEncoding textEncoding)
	{
		_libraryManager = libraryManager;
		_logger = logger;
		_appPaths = appPaths;
		_fileSystem = fileSystem;
		_mediaEncoder = mediaEncoder;
		_json = json;
		_httpClient = httpClient;
		_mediaSourceManager = mediaSourceManager;
		_processFactory = processFactory;
		_textEncoding = textEncoding;
	}

	private Stream ConvertSubtitles(Stream stream, string inputFormat, string outputFormat, long startTimeTicks, long endTimeTicks, bool preserveOriginalTimestamps, CancellationToken cancellationToken)
	{
		MemoryStream memoryStream = new MemoryStream();
		try
		{
			SubtitleTrackInfo val = GetReader(inputFormat, throwIfMissing: true).Parse(stream, cancellationToken);
			FilterEvents(val, startTimeTicks, endTimeTicks, preserveOriginalTimestamps);
			GetWriter(outputFormat).Write(val, memoryStream, cancellationToken);
			memoryStream.Position = 0L;
			return memoryStream;
		}
		catch
		{
			memoryStream.Dispose();
			throw;
		}
	}

	private void FilterEvents(SubtitleTrackInfo track, long startPositionTicks, long endTimeTicks, bool preserveTimestamps)
	{
		track.TrackEvents = track.TrackEvents.SkipWhile((SubtitleTrackEvent i) => i.StartPositionTicks - startPositionTicks < 0 || i.EndPositionTicks - startPositionTicks < 0).ToArray();
		if (endTimeTicks > 0)
		{
			track.TrackEvents = track.TrackEvents.TakeWhile((SubtitleTrackEvent i) => i.StartPositionTicks <= endTimeTicks).ToArray();
		}
		if (!preserveTimestamps)
		{
			SubtitleTrackEvent[] trackEvents = track.TrackEvents;
			foreach (SubtitleTrackEvent obj in trackEvents)
			{
				obj.EndPositionTicks -= startPositionTicks;
				obj.StartPositionTicks -= startPositionTicks;
			}
		}
	}

	public async Task<Stream> GetSubtitles(BaseItem item, string mediaSourceId, int subtitleStreamIndex, string outputFormat, long startTimeTicks, long endTimeTicks, bool preserveOriginalTimestamps, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(mediaSourceId))
		{
			throw new ArgumentNullException("mediaSourceId");
		}
		MediaSourceInfo val = (await _mediaSourceManager.GetPlayackMediaSources(item, (User)null, false, false, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).FirstOrDefault((MediaSourceInfo i) => string.Equals(i.Id, mediaSourceId, StringComparison.OrdinalIgnoreCase));
		if (val == null)
		{
			// ①类：MediaSourceId 不存在 → 404（旧 First() 抛 IOE 落 500）
			throw new ResourceNotFoundException(string.Format("Media source {0} not found", mediaSourceId));
		}
		MediaStream subtitleStream = val.MediaStreams.FirstOrDefault((MediaStream i) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Invalid comparison between Unknown and I4
			return (int)i.Type == 2 && i.Index == subtitleStreamIndex;
		});
		if (subtitleStream == null)
		{
			// ①类：字幕流索引不存在（无字幕媒体常见）→ 404（旧 First() 抛 IOE 落 500）
			throw new ResourceNotFoundException(string.Format("Subtitle stream {0} not found in media source {1}", subtitleStreamIndex, mediaSourceId));
		}
		Tuple<Stream, string> tuple = await GetSubtitleStream(val, subtitleStream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		string item2 = tuple.Item2;
		if (TryGetWriter(outputFormat) == null)
		{
			return tuple.Item1;
		}
		if (string.Equals(item2, outputFormat, StringComparison.OrdinalIgnoreCase))
		{
			return tuple.Item1;
		}
		using Stream stream = tuple.Item1;
		return ConvertSubtitles(stream, item2, outputFormat, startTimeTicks, endTimeTicks, preserveOriginalTimestamps, cancellationToken);
	}

	private async Task<Tuple<Stream, string>> GetSubtitleStream(MediaSourceInfo mediaSource, MediaStream subtitleStream, CancellationToken cancellationToken)
	{
		string[] inputFiles = new string[1] { mediaSource.Path };
		Tuple<string, MediaProtocol, string, bool> fileInfo = await GetReadableFile(mediaSource.Path, inputFiles, mediaSource.Protocol, subtitleStream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		return new Tuple<Stream, string>(await GetSubtitleStream(fileInfo.Item1, subtitleStream.Language, fileInfo.Item2, fileInfo.Item4, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), fileInfo.Item3);
	}

	private async Task<Stream> GetSubtitleStream(string path, string language, MediaProtocol protocol, bool requiresCharset, CancellationToken cancellationToken)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		if (requiresCharset)
		{
			byte[] array = await GetBytes(path, protocol, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrEmpty(language) && string.Equals(Path.GetExtension(path), ".smi", StringComparison.OrdinalIgnoreCase))
			{
				language = "kor";
			}
			string detectedEncodingName = _textEncoding.GetDetectedEncodingName(array, array.Length, language, true);
			_logger.Debug("charset {0} detected for {1}", new object[2]
			{
				detectedEncodingName ?? "null",
				path
			});
			if (!string.IsNullOrEmpty(detectedEncodingName))
			{
				using (MemoryStream inputStream = new MemoryStream(array))
				{
					using StreamReader reader = new StreamReader(inputStream, _textEncoding.GetEncodingFromCharset(detectedEncodingName));
					string s = await reader.ReadToEndAsync().ConfigureAwait(continueOnCapturedContext: false);
					array = Encoding.UTF8.GetBytes(s);
					return new MemoryStream(array);
				}
			}
		}
		return _fileSystem.OpenRead(path);
	}

	private async Task<Tuple<string, MediaProtocol, string, bool>> GetReadableFile(string mediaPath, string[] inputFiles, MediaProtocol mediaSourceProtocol, MediaStream subtitleStream, CancellationToken cancellationToken)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		MediaProtocol val = mediaSourceProtocol;
		if (subtitleStream.IsExternal && (int)mediaSourceProtocol != 0)
		{
			try
			{
				if (_fileSystem.FileExists(subtitleStream.Path))
				{
					val = (MediaProtocol)0;
				}
			}
			catch
			{
			}
		}
		if (!subtitleStream.IsExternal)
		{
			string outputCodec;
			string outputFormat;
			if (string.Equals(subtitleStream.Codec, "ass", StringComparison.OrdinalIgnoreCase) || string.Equals(subtitleStream.Codec, "ssa", StringComparison.OrdinalIgnoreCase) || string.Equals(subtitleStream.Codec, "srt", StringComparison.OrdinalIgnoreCase))
			{
				outputCodec = "copy";
				outputFormat = subtitleStream.Codec;
			}
			else if (string.Equals(subtitleStream.Codec, "subrip", StringComparison.OrdinalIgnoreCase))
			{
				outputCodec = "copy";
				outputFormat = "srt";
			}
			else
			{
				outputCodec = "srt";
				outputFormat = "srt";
			}
			string outputPath = GetSubtitleCachePath(mediaPath, mediaSourceProtocol, subtitleStream.Index, "." + outputFormat);
			await ExtractTextSubtitle(inputFiles, val, subtitleStream.Index, outputCodec, outputPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return new Tuple<string, MediaProtocol, string, bool>(outputPath, (MediaProtocol)0, outputFormat, item4: false);
		}
		string text = (Path.GetExtension(subtitleStream.Path) ?? subtitleStream.Codec).TrimStart('.');
		if (GetReader(text, throwIfMissing: false) == null)
		{
			string outputPath = GetSubtitleCachePath(mediaPath, mediaSourceProtocol, subtitleStream.Index, ".srt");
			await ConvertTextSubtitleToSrt(subtitleStream.Path, subtitleStream.Language, val, outputPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return new Tuple<string, MediaProtocol, string, bool>(outputPath, (MediaProtocol)0, "srt", item4: true);
		}
		return new Tuple<string, MediaProtocol, string, bool>(subtitleStream.Path, val, text, item4: true);
	}

	private ISubtitleParser GetReader(string format, bool throwIfMissing)
	{
		if (string.IsNullOrEmpty(format))
		{
			throw new ArgumentNullException("format");
		}
		if (string.Equals(format, "srt", StringComparison.OrdinalIgnoreCase))
		{
			return new SrtParser(_logger);
		}
		if (string.Equals(format, "ssa", StringComparison.OrdinalIgnoreCase))
		{
			return new SsaParser();
		}
		if (string.Equals(format, "ass", StringComparison.OrdinalIgnoreCase))
		{
			return new AssParser();
		}
		if (throwIfMissing)
		{
			throw new ArgumentException("Unsupported format: " + format);
		}
		return null;
	}

	private ISubtitleWriter TryGetWriter(string format)
	{
		if (string.IsNullOrEmpty(format))
		{
			throw new ArgumentNullException("format");
		}
		if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "js", StringComparison.OrdinalIgnoreCase))
		{
			return new JsonWriter(_json);
		}
		if (string.Equals(format, "srt", StringComparison.OrdinalIgnoreCase))
		{
			return new SrtWriter();
		}
		if (string.Equals(format, "vtt", StringComparison.OrdinalIgnoreCase))
		{
			return new VttWriter();
		}
		if (string.Equals(format, "ttml", StringComparison.OrdinalIgnoreCase))
		{
			return new TtmlWriter();
		}
		return null;
	}

	private ISubtitleWriter GetWriter(string format)
	{
		ISubtitleWriter subtitleWriter = TryGetWriter(format);
		if (subtitleWriter != null)
		{
			return subtitleWriter;
		}
		throw new ArgumentException("Unsupported format: " + format);
	}

	private SemaphoreSlim GetLock(string filename)
	{
		return _semaphoreLocks.GetOrAdd(filename, (string key) => new SemaphoreSlim(1, 1));
	}

	private async Task ConvertTextSubtitleToSrt(string inputPath, string language, MediaProtocol inputProtocol, string outputPath, CancellationToken cancellationToken)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		SemaphoreSlim semaphore = GetLock(outputPath);
		await semaphore.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (!_fileSystem.FileExists(outputPath))
			{
				await ConvertTextSubtitleToSrtInternal(inputPath, language, inputProtocol, outputPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		finally
		{
			semaphore.Release();
		}
	}

	private async Task ConvertTextSubtitleToSrtInternal(string inputPath, string language, MediaProtocol inputProtocol, string outputPath, CancellationToken cancellationToken)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		if (string.IsNullOrEmpty(inputPath))
		{
			throw new ArgumentNullException("inputPath");
		}
		if (string.IsNullOrEmpty(outputPath))
		{
			throw new ArgumentNullException("outputPath");
		}
		_fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(outputPath));
		string text = await GetSubtitleFileCharacterSet(inputPath, language, inputProtocol, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (!string.IsNullOrEmpty(text))
		{
			text = " -sub_charenc " + text;
		}
		IProcess process = _processFactory.Create(new ProcessOptions
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			FileName = _mediaEncoder.EncoderPath,
			Arguments = $"{text} -i \"{inputPath}\" -c:s srt \"{outputPath}\"",
			IsHidden = true,
			ErrorDialog = false,
			EnableRaisingEvents = true
		});
		_logger.Info("{0} {1}", new object[2]
		{
			process.StartInfo.FileName,
			process.StartInfo.Arguments
		});
		try
		{
			process.Start();
		}
		catch (Exception ex)
		{
			_logger.ErrorException("Error starting ffmpeg", ex, Array.Empty<object>());
			throw;
		}
		bool flag = await process.WaitForExitAsync(300000).ConfigureAwait(continueOnCapturedContext: false);
		if (!flag)
		{
			try
			{
				_logger.Info("Killing ffmpeg subtitle conversion process", Array.Empty<object>());
				process.Kill();
			}
			catch (Exception ex2)
			{
				_logger.ErrorException("Error killing subtitle conversion process", ex2, Array.Empty<object>());
			}
		}
		int num = (flag ? process.ExitCode : (-1));
		((IDisposable)process).Dispose();
		bool flag2 = false;
		if (num == -1)
		{
			flag2 = true;
			if (_fileSystem.FileExists(outputPath))
			{
				try
				{
					_logger.Info("Deleting converted subtitle due to failure: ", new object[1] { outputPath });
					_fileSystem.DeleteFile(outputPath);
				}
				catch (IOException ex3)
				{
					_logger.ErrorException("Error deleting converted subtitle {0}", (Exception)ex3, new object[1] { outputPath });
				}
			}
		}
		else if (!_fileSystem.FileExists(outputPath))
		{
			flag2 = true;
		}
		if (flag2)
		{
			string text2 = $"ffmpeg subtitle conversion failed for {inputPath}";
			_logger.Error(text2, Array.Empty<object>());
			throw new Exception(text2);
		}
		await SetAssFont(outputPath).ConfigureAwait(continueOnCapturedContext: false);
		_logger.Info("ffmpeg subtitle conversion succeeded for {0}", new object[1] { inputPath });
	}

	private async Task ExtractTextSubtitle(string[] inputFiles, MediaProtocol protocol, int subtitleStreamIndex, string outputCodec, string outputPath, CancellationToken cancellationToken)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		SemaphoreSlim semaphore = GetLock(outputPath);
		await semaphore.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (!_fileSystem.FileExists(outputPath))
			{
				await ExtractTextSubtitleInternal(_mediaEncoder.GetInputArgument(inputFiles, protocol), subtitleStreamIndex, outputCodec, outputPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		finally
		{
			semaphore.Release();
		}
	}

	private async Task ExtractTextSubtitleInternal(string inputPath, int subtitleStreamIndex, string outputCodec, string outputPath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(inputPath))
		{
			throw new ArgumentNullException("inputPath");
		}
		if (string.IsNullOrEmpty(outputPath))
		{
			throw new ArgumentNullException("outputPath");
		}
		_fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(outputPath));
		string arguments = $"-i {inputPath} -map 0:{subtitleStreamIndex} -an -vn -c:s {outputCodec} \"{outputPath}\"";
		IProcess process = _processFactory.Create(new ProcessOptions
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			FileName = _mediaEncoder.EncoderPath,
			Arguments = arguments,
			IsHidden = true,
			ErrorDialog = false,
			EnableRaisingEvents = true
		});
		_logger.Info("{0} {1}", new object[2]
		{
			process.StartInfo.FileName,
			process.StartInfo.Arguments
		});
		try
		{
			process.Start();
		}
		catch (Exception ex)
		{
			_logger.ErrorException("Error starting ffmpeg", ex, Array.Empty<object>());
			throw;
		}
		bool flag = await process.WaitForExitAsync(300000).ConfigureAwait(continueOnCapturedContext: false);
		if (!flag)
		{
			try
			{
				_logger.Info("Killing ffmpeg subtitle extraction process", Array.Empty<object>());
				process.Kill();
			}
			catch (Exception ex2)
			{
				_logger.ErrorException("Error killing subtitle extraction process", ex2, Array.Empty<object>());
			}
		}
		int num = (flag ? process.ExitCode : (-1));
		((IDisposable)process).Dispose();
		bool flag2 = false;
		if (num == -1)
		{
			flag2 = true;
			try
			{
				_logger.Info("Deleting extracted subtitle due to failure: {0}", new object[1] { outputPath });
				_fileSystem.DeleteFile(outputPath);
			}
			catch (FileNotFoundException)
			{
			}
			catch (IOException ex4)
			{
				_logger.ErrorException("Error deleting extracted subtitle {0}", (Exception)ex4, new object[1] { outputPath });
			}
		}
		else if (!_fileSystem.FileExists(outputPath))
		{
			flag2 = true;
		}
		if (flag2)
		{
			string text = $"ffmpeg subtitle extraction failed for {inputPath} to {outputPath}";
			_logger.Error(text, Array.Empty<object>());
			throw new Exception(text);
		}
		string text2 = $"ffmpeg subtitle extraction completed for {inputPath} to {outputPath}";
		_logger.Info(text2, Array.Empty<object>());
		if (string.Equals(outputCodec, "ass", StringComparison.OrdinalIgnoreCase))
		{
			await SetAssFont(outputPath).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private async Task SetAssFont(string file)
	{
		_logger.Info("Setting ass font within {0}", new object[1] { file });
		Encoding encoding;
		string text;
		using (Stream fileStream = _fileSystem.OpenRead(file))
		{
			using StreamReader reader = new StreamReader(fileStream, detectEncodingFromByteOrderMarks: true);
			encoding = reader.CurrentEncoding;
			text = await reader.ReadToEndAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		string text2 = text.Replace(",Arial,", ",Arial Unicode MS,");
		if (string.Equals(text, text2))
		{
			return;
		}
		using Stream stream = _fileSystem.GetFileStream(file, (FileOpenMode)2, (FileAccessMode)2, (FileShareMode)1, false);
		using StreamWriter streamWriter = new StreamWriter(stream, encoding);
		streamWriter.Write(text2);
	}

	private string GetSubtitleCachePath(string mediaPath, MediaProtocol mediaProtocol, int subtitleStreamIndex, string outputSubtitleExtension)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		if ((int)mediaProtocol == 0)
		{
			string empty = string.Empty;
			DateTime lastWriteTimeUtc = _fileSystem.GetLastWriteTimeUtc(mediaPath);
			string text = string.Concat(BaseExtensions.GetMD5(mediaPath + "_" + subtitleStreamIndex.ToString(CultureInfo.InvariantCulture) + "_" + lastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + empty), outputSubtitleExtension);
			string path = text.Substring(0, 1);
			return Path.Combine(SubtitleCachePath, path, text);
		}
		string text2 = string.Concat(BaseExtensions.GetMD5(mediaPath + "_" + subtitleStreamIndex.ToString(CultureInfo.InvariantCulture)), outputSubtitleExtension);
		string path2 = text2.Substring(0, 1);
		return Path.Combine(SubtitleCachePath, path2, text2);
	}

	public async Task<string> GetSubtitleFileCharacterSet(string path, string language, MediaProtocol protocol, CancellationToken cancellationToken)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		byte[] array = await GetBytes(path, protocol, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		string detectedEncodingName = _textEncoding.GetDetectedEncodingName(array, array.Length, language, true);
		_logger.Debug("charset {0} detected for {1}", new object[2]
		{
			detectedEncodingName ?? "null",
			path
		});
		return detectedEncodingName;
	}

	private async Task<byte[]> GetBytes(string path, MediaProtocol protocol, CancellationToken cancellationToken)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		if ((int)protocol == 1)
		{
			HttpResponseInfo response = await _httpClient.SendAsync(new HttpRequestOptions
			{
				Url = path,
				CancellationToken = cancellationToken,
				BufferContent = false
			}, "GET").ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				using Stream stream = response.Content;
				using MemoryStream memoryStream = new MemoryStream();
				await stream.CopyToAsync(memoryStream).ConfigureAwait(continueOnCapturedContext: false);
				memoryStream.Position = 0L;
				return memoryStream.ToArray();
			}
			finally
			{
				((IDisposable)response)?.Dispose();
			}
		}
		if ((int)protocol == 0)
		{
			return _fileSystem.ReadAllBytes(path);
		}
		throw new ArgumentOutOfRangeException("protocol");
	}
}
