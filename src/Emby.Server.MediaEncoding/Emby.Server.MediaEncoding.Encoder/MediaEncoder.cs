using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DvdLib.Ifo;
using Emby.Server.MediaEncoding.Probing;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.System;

namespace Emby.Server.MediaEncoding.Encoder;

public class MediaEncoder : IMediaEncoder, ITranscoderSupport, IDisposable
{
	private class ProcessWrapper : IDisposable
	{
		public readonly IProcess Process;

		public bool HasExited;

		public int? ExitCode;

		private readonly MediaEncoder _mediaEncoder;

		private readonly ILogger _logger;

		private bool _disposed;

		private readonly object _syncLock = new object();

		public ProcessWrapper(IProcess process, MediaEncoder mediaEncoder, ILogger logger)
		{
			Process = process;
			_mediaEncoder = mediaEncoder;
			_logger = logger;
			Process.Exited += Process_Exited;
		}

		public void Start()
		{
			try
			{
				Process.Start();
			}
			catch
			{
				HasExited = true;
				throw;
			}
		}

		private void Process_Exited(object sender, EventArgs e)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Expected Obj, but got Unknown
			IProcess val = (IProcess)sender;
			HasExited = true;
			try
			{
				ExitCode = val.ExitCode;
			}
			catch
			{
			}
			DisposeProcess(val);
		}

		private void DisposeProcess(IProcess process)
		{
			lock (_mediaEncoder._runningProcesses)
			{
				_mediaEncoder._runningProcesses.Remove(this);
			}
			try
			{
				((IDisposable)process).Dispose();
			}
			catch
			{
			}
		}

		public void Dispose()
		{
			lock (_syncLock)
			{
				if (!_disposed && Process != null)
				{
					Process.Exited -= Process_Exited;
					DisposeProcess(Process);
				}
				_disposed = true;
			}
		}
	}

	private readonly ILogger _logger;

	private readonly IJsonSerializer _jsonSerializer;

	private readonly SemaphoreSlim _thumbnailResourcePool = new SemaphoreSlim(1, 1);

	protected readonly IServerConfigurationManager ConfigurationManager;

	protected readonly IFileSystem FileSystem;

	protected readonly ILiveTvManager LiveTvManager;

	protected readonly IIsoManager IsoManager;

	protected readonly ILibraryManager LibraryManager;

	protected readonly IChannelManager ChannelManager;

	protected readonly ISessionManager SessionManager;

	protected readonly Func<ISubtitleEncoder> SubtitleEncoder;

	protected readonly Func<IMediaSourceManager> MediaSourceManager;

	private readonly IHttpClient _httpClient;

	private readonly IZipClient _zipClient;

	private readonly IProcessFactory _processFactory;

	private readonly List<ProcessWrapper> _runningProcesses = new List<ProcessWrapper>();

	private readonly bool _hasExternalEncoder;

	private readonly string _originalFFMpegPath;

	private readonly string _originalFFProbePath;

	private readonly int DefaultImageExtractionTimeoutMs = 40000;

	private readonly IEnvironmentInfo _environmentInfo;

	private readonly IBlurayExaminer _blurayExaminer;

	private readonly IAssemblyInfo _assemblyInfo;

	private readonly IApplicationHost _appHost;

	private string[] _encoders = Array.Empty<string>();

	private string[] _decoders = Array.Empty<string>();

	private string[] _inputProtocols = Array.Empty<string>();

	protected readonly CultureInfo UsCulture = new CultureInfo("en-US");

	public string FFMpegPath { get; private set; }

	public string FFProbePath { get; private set; }

	public string EncoderLocationType
	{
		get
		{
			if (_hasExternalEncoder)
			{
				return "External";
			}
			if (string.IsNullOrWhiteSpace(FFMpegPath))
			{
				return null;
			}
			if (IsSystemInstalledPath(FFMpegPath))
			{
				return "System";
			}
			return "Custom";
		}
	}

	public string EncoderPath => FFMpegPath;

	private bool SupportsSmbInputProtocol => _inputProtocols.Any((string i) => i.IndexOf("smb", StringComparison.OrdinalIgnoreCase) != -1);

	public MediaEncoder(ILogger logger, IJsonSerializer jsonSerializer, string ffMpegPath, string ffProbePath, bool hasExternalEncoder, IServerConfigurationManager configurationManager, IFileSystem fileSystem, ILiveTvManager liveTvManager, IIsoManager isoManager, ILibraryManager libraryManager, IChannelManager channelManager, ISessionManager sessionManager, Func<ISubtitleEncoder> subtitleEncoder, Func<IMediaSourceManager> mediaSourceManager, IHttpClient httpClient, IZipClient zipClient, IProcessFactory processFactory, IEnvironmentInfo environmentInfo, IBlurayExaminer blurayExaminer, IAssemblyInfo assemblyInfo, IApplicationHost appHost)
	{
		_logger = logger;
		_jsonSerializer = jsonSerializer;
		ConfigurationManager = configurationManager;
		FileSystem = fileSystem;
		LiveTvManager = liveTvManager;
		IsoManager = isoManager;
		LibraryManager = libraryManager;
		ChannelManager = channelManager;
		SessionManager = sessionManager;
		SubtitleEncoder = subtitleEncoder;
		MediaSourceManager = mediaSourceManager;
		_httpClient = httpClient;
		_zipClient = zipClient;
		_processFactory = processFactory;
		_environmentInfo = environmentInfo;
		_blurayExaminer = blurayExaminer;
		FFProbePath = ffProbePath;
		FFMpegPath = ffMpegPath;
		_originalFFProbePath = ffProbePath;
		_originalFFMpegPath = ffMpegPath;
		_assemblyInfo = assemblyInfo;
		_appHost = appHost;
		_hasExternalEncoder = hasExternalEncoder;
	}

	private bool IsSystemInstalledPath(string path)
	{
		if (path.IndexOf("/", StringComparison.Ordinal) == -1 && path.IndexOf("\\", StringComparison.Ordinal) == -1)
		{
			return true;
		}
		return false;
	}

	private void SetExecutable(string path)
	{
		try
		{
			FileSystem.SetExecutable(path);
		}
		catch (Exception ex)
		{
			_logger.ErrorException("Error setting {0} executable", ex, new object[1] { path });
		}
	}

	public void Init()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Invalid comparison between Unknown and I4
		InitPaths();
		if (!string.IsNullOrWhiteSpace(FFMpegPath))
		{
			if ((int)_environmentInfo.OperatingSystem == 2)
			{
				SetExecutable(FFMpegPath);
				SetExecutable(FFProbePath);
			}
			Tuple<List<string>, List<string>, List<string>> tuple = new EncoderValidator(_logger, _processFactory).Validate(FFMpegPath);
			SetAvailableDecoders(tuple.Item1.ToArray());
			SetAvailableEncoders(tuple.Item2.ToArray());
			SetAvailableInputProtocols(tuple.Item3.ToArray());
		}
	}

	private void InitPaths()
	{
		ConfigureEncoderPaths();
		if (_hasExternalEncoder)
		{
			LogPaths();
			return;
		}
		EncodingOptions encodingOptions = GetEncodingOptions();
		string encoderAppPath = encodingOptions.EncoderAppPath;
		string text = FFMpegPath;
		if (!string.IsNullOrWhiteSpace(text) && (IsSystemInstalledPath(text) || _hasExternalEncoder))
		{
			text = null;
		}
		if (!string.Equals(text, encoderAppPath, StringComparison.Ordinal))
		{
			encodingOptions.EncoderAppPath = text;
			((IConfigurationManager)ConfigurationManager).SaveConfiguration("encoding", (object)encodingOptions);
		}
	}

	public void UpdateEncoderPath(string path, string pathType)
	{
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		if (_hasExternalEncoder)
		{
			return;
		}
		_logger.Info("Attempting to update encoder path to {0}. pathType: {1}", new object[2]
		{
			path ?? string.Empty,
			pathType ?? string.Empty
		});
		Tuple<string, string> tuple;
		if (string.Equals(pathType, "system", StringComparison.OrdinalIgnoreCase))
		{
			path = "ffmpeg";
			tuple = TestForInstalledVersions();
		}
		else
		{
			if (!string.Equals(pathType, "custom", StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("Unexpected pathType value");
			}
			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentNullException("path");
			}
			if (!FileSystem.FileExists(path) && !FileSystem.DirectoryExists(path))
			{
				throw new ResourceNotFoundException();
			}
			tuple = GetEncoderPaths(path);
		}
		if (string.IsNullOrWhiteSpace(tuple.Item1))
		{
			throw new ResourceNotFoundException("ffmpeg not found");
		}
		if (string.IsNullOrWhiteSpace(tuple.Item2))
		{
			throw new ResourceNotFoundException("ffprobe not found");
		}
		path = tuple.Item1;
		if (!ValidateVersion(path, logOutput: true))
		{
			throw new ResourceNotFoundException("ffmpeg version 3.0 or greater is required.");
		}
		EncodingOptions encodingOptions = GetEncodingOptions();
		encodingOptions.EncoderAppPath = path;
		((IConfigurationManager)ConfigurationManager).SaveConfiguration("encoding", (object)encodingOptions);
		Init();
	}

	private bool ValidateVersion(string path, bool logOutput)
	{
		return new EncoderValidator(_logger, _processFactory).ValidateVersion(path, logOutput);
	}

	private void ConfigureEncoderPaths()
	{
		if (!_hasExternalEncoder)
		{
			string text = GetEncodingOptions().EncoderAppPath;
			if (string.IsNullOrWhiteSpace(text))
			{
				text = Path.Combine(((IApplicationPaths)ConfigurationManager.ApplicationPaths).ProgramDataPath, "ffmpeg");
			}
			Tuple<string, string> tuple = GetEncoderPaths(text);
			if (string.IsNullOrWhiteSpace(tuple.Item1) || string.IsNullOrWhiteSpace(tuple.Item2) || IsSystemInstalledPath(text))
			{
				tuple = TestForInstalledVersions();
			}
			if (!string.IsNullOrWhiteSpace(tuple.Item1) && !string.IsNullOrWhiteSpace(tuple.Item2))
			{
				FFMpegPath = tuple.Item1;
				FFProbePath = tuple.Item2;
			}
			LogPaths();
		}
	}

	private Tuple<string, string> GetEncoderPaths(string configuredPath)
	{
		if (!string.IsNullOrWhiteSpace(configuredPath))
		{
			if (FileSystem.DirectoryExists(configuredPath))
			{
				return GetPathsFromDirectory(configuredPath);
			}
			if (FileSystem.FileExists(configuredPath))
			{
				return new Tuple<string, string>(configuredPath, GetProbePathFromEncoderPath(configuredPath));
			}
		}
		return new Tuple<string, string>(null, null);
	}

	private Tuple<string, string> TestForInstalledVersions()
	{
		string text = null;
		string item = null;
		if (_hasExternalEncoder && ValidateVersion(_originalFFMpegPath, logOutput: true))
		{
			text = _originalFFMpegPath;
			item = _originalFFProbePath;
		}
		if (string.IsNullOrWhiteSpace(text) && ValidateVersion("ffmpeg", logOutput: true) && ValidateVersion("ffprobe", logOutput: false))
		{
			text = "ffmpeg";
			item = "ffprobe";
		}
		return new Tuple<string, string>(text, item);
	}

	private Tuple<string, string> GetPathsFromDirectory(string path)
	{
		IEnumerable<string> filePaths = FileSystem.GetFilePaths(path, false);
		string[] excludeExtensions = new string[1] { ".c" };
		string text = filePaths.FirstOrDefault((string i) => string.Equals(Path.GetFileNameWithoutExtension(i), "ffmpeg", StringComparison.OrdinalIgnoreCase) && !excludeExtensions.Contains(Path.GetExtension(i) ?? string.Empty));
		string item = filePaths.FirstOrDefault((string i) => string.Equals(Path.GetFileNameWithoutExtension(i), "ffprobe", StringComparison.OrdinalIgnoreCase) && !excludeExtensions.Contains(Path.GetExtension(i) ?? string.Empty));
		if (string.IsNullOrWhiteSpace(text) || !FileSystem.FileExists(text))
		{
			filePaths = FileSystem.GetFilePaths(path, true);
			text = filePaths.FirstOrDefault((string i) => string.Equals(Path.GetFileNameWithoutExtension(i), "ffmpeg", StringComparison.OrdinalIgnoreCase) && !excludeExtensions.Contains(Path.GetExtension(i) ?? string.Empty));
			if (!string.IsNullOrWhiteSpace(text))
			{
				item = GetProbePathFromEncoderPath(text);
			}
		}
		return new Tuple<string, string>(text, item);
	}

	private string GetProbePathFromEncoderPath(string appPath)
	{
		return FileSystem.GetFilePaths(FileSystem.GetDirectoryName(appPath), false).FirstOrDefault((string i) => string.Equals(Path.GetFileNameWithoutExtension(i), "ffprobe", StringComparison.OrdinalIgnoreCase));
	}

	private void LogPaths()
	{
		_logger.Info("FFMpeg: {0}", new object[1] { FFMpegPath ?? "not found" });
		_logger.Info("FFProbe: {0}", new object[1] { FFProbePath ?? "not found" });
	}

	private EncodingOptions GetEncodingOptions()
	{
		return ConfigurationManagerExtensions.GetConfiguration<EncodingOptions>((IConfigurationManager)(object)ConfigurationManager, "encoding");
	}

	public void SetAvailableEncoders(string[] list)
	{
		_encoders = list;
	}

	public void SetAvailableDecoders(string[] list)
	{
		_decoders = list;
	}

	public void SetAvailableInputProtocols(string[] list)
	{
		_inputProtocols = list;
		string text = string.Join(",", list);
		_logger.Info("ffmpeg supported protocols: {0}", new object[1] { text });
	}

	public bool SupportsInputProtocol(string protocol)
	{
		return _inputProtocols.Contains(protocol, StringComparer.OrdinalIgnoreCase);
	}

	public bool SupportsEncoder(string encoder)
	{
		return _encoders.Contains(encoder, StringComparer.OrdinalIgnoreCase);
	}

	public bool SupportsDecoder(string decoder)
	{
		return _decoders.Contains(decoder, StringComparer.OrdinalIgnoreCase);
	}

	public bool CanEncodeToAudioCodec(string codec)
	{
		if (string.Equals(codec, "opus", StringComparison.OrdinalIgnoreCase))
		{
			codec = "libopus";
		}
		else if (string.Equals(codec, "mp3", StringComparison.OrdinalIgnoreCase))
		{
			codec = "libmp3lame";
		}
		return SupportsEncoder(codec);
	}

	public bool CanEncodeToSubtitleCodec(string codec)
	{
		return true;
	}

	public bool CanExtractSubtitles(string codec)
	{
		return GetEncodingOptions().EnableSubtitleExtraction;
	}

	public Task<MediaInfo> GetMediaInfo(MediaInfoRequest request, CancellationToken cancellationToken)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Invalid comparison between Unknown and I4
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Invalid comparison between Unknown and I4
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Invalid comparison between Unknown and I4
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		bool extractChapters = (int)request.MediaType == 1 && request.ExtractChapters;
		string text = request.MediaSource.Path;
		MediaProtocol val = request.MediaSource.Protocol;
		if (request.MediaSource.EncoderProtocol.HasValue && !string.IsNullOrWhiteSpace(request.MediaSource.EncoderPath))
		{
			text = request.MediaSource.EncoderPath;
			val = request.MediaSource.EncoderProtocol.Value;
		}
		string[] inputArgument = MediaEncoderHelpers.GetInputArgument(FileSystem, text, val, request.MountedIso, request.PlayableStreamFileNames);
		string probeSizeArgument = EncodingHelper.GetProbeSizeArgument(inputArgument.Length);
		int num = request.MediaSource.AnalyzeDurationMs ?? 0;
		string text2 = ((num <= 0) ? EncodingHelper.GetAnalyzeDurationArgument(inputArgument.Length) : ("-analyzeduration " + (num * 1000).ToString(CultureInfo.InvariantCulture)));
		probeSizeArgument = probeSizeArgument + " " + text2;
		probeSizeArgument = probeSizeArgument.Trim();
		bool forceEnableLogging = (int)val > 0;
		return GetMediaInfoInternal(GetInputArgument(inputArgument, val), text, val, extractChapters, probeSizeArgument, (int)request.MediaType == 0, (VideoType)(((int?)request.MediaSource.VideoType) ?? 0), forceEnableLogging, cancellationToken);
	}

	public string GetInputArgument(string[] inputFiles, MediaProtocol protocol)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		if ((int)protocol != 0)
		{
			string arg = inputFiles.First();
			return $"\"{arg}\"";
		}
		if ((int)_environmentInfo.OperatingSystem != 0 && inputFiles.Length != 0 && (int)protocol == 0 && !SupportsSmbInputProtocol)
		{
			string text = inputFiles[0];
			if (text.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase) || text.IndexOf("smb:/", StringComparison.OrdinalIgnoreCase) != -1)
			{
				inputFiles = new string[1] { $"http://127.0.0.1:8096/Items/File?Id={WebUtility.UrlEncode(text)}" };
			}
		}
		return GetConcatInputArgument(inputFiles);
	}

	private static string GetConcatInputArgument(IReadOnlyList<string> inputFiles)
	{
		if (inputFiles.Count > 1)
		{
			string arg = string.Join("|", inputFiles.Select(NormalizePath).ToArray());
			return $"concat:\"{arg}\"";
		}
		return GetFileInputArgument(inputFiles[0]);
	}

	private static string GetFileInputArgument(string path)
	{
		if (path.IndexOf("://") != -1)
		{
			return $"\"{path}\"";
		}
		path = NormalizePath(path);
		return $"file:\"{path}\"";
	}

	private static string NormalizePath(string path)
	{
		return path.Replace("\"", "\\\"");
	}

	private async Task<MediaInfo> GetMediaInfoInternal(string inputPath, string primaryPath, MediaProtocol protocol, bool extractChapters, string probeSizeArgument, bool isAudio, VideoType videoType, bool forceEnableLogging, CancellationToken cancellationToken)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		string format = (extractChapters ? "{0} -i {1} -threads 0 -v info -print_format json -show_streams -show_chapters -show_format" : "{0} -i {1} -threads 0 -v info -print_format json -show_streams -show_format");
		IProcess val = _processFactory.Create(new ProcessOptions
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			FileName = FFProbePath,
			Arguments = string.Format(format, probeSizeArgument, inputPath).Trim(),
			IsHidden = true,
			ErrorDialog = false,
			EnableRaisingEvents = true
		});
		if (forceEnableLogging)
		{
			_logger.Info("{0} {1}", new object[2]
			{
				val.StartInfo.FileName,
				val.StartInfo.Arguments
			});
		}
		else
		{
			_logger.Debug("{0} {1}", new object[2]
			{
				val.StartInfo.FileName,
				val.StartInfo.Arguments
			});
		}
		using ProcessWrapper processWrapper = new ProcessWrapper(val, this, _logger);
		StartProcess(processWrapper);
		try
		{
			InternalMediaInfoResult internalMediaInfoResult = await _jsonSerializer.DeserializeFromStreamAsync<InternalMediaInfoResult>(val.StandardOutput.BaseStream).ConfigureAwait(continueOnCapturedContext: false);
			if (internalMediaInfoResult == null || (internalMediaInfoResult.streams == null && internalMediaInfoResult.format == null))
			{
				throw new Exception($"ffprobe failed for {inputPath} - streams and format are both null.");
			}
			if (internalMediaInfoResult.streams != null)
			{
				MediaStreamInfo[] streams = internalMediaInfoResult.streams;
				foreach (MediaStreamInfo mediaStreamInfo in streams)
				{
					if (string.Equals(mediaStreamInfo.display_aspect_ratio, "0:1", StringComparison.OrdinalIgnoreCase))
					{
						mediaStreamInfo.display_aspect_ratio = string.Empty;
					}
					if (string.Equals(mediaStreamInfo.sample_aspect_ratio, "0:1", StringComparison.OrdinalIgnoreCase))
					{
						mediaStreamInfo.sample_aspect_ratio = string.Empty;
					}
				}
			}
			MediaInfo mediaInfo = new ProbeResultNormalizer(_logger, FileSystem).GetMediaInfo(internalMediaInfoResult, videoType, isAudio, primaryPath, protocol);
			MediaStream val2 = ((MediaSourceInfo)mediaInfo).MediaStreams.FirstOrDefault((MediaStream val3) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Invalid comparison between Unknown and I4
				return (int)val3.Type == 1;
			});
			if (val2 != null && !val2.IsInterlaced && DetectInterlaced((MediaSourceInfo)(object)mediaInfo, val2))
			{
				val2.IsInterlaced = true;
			}
			return mediaInfo;
		}
		catch
		{
			await StopProcess(processWrapper, 100).ConfigureAwait(continueOnCapturedContext: false);
			throw;
		}
	}

	private bool DetectInterlaced(MediaSourceInfo video, MediaStream videoStream)
	{
		if ((videoStream.Codec ?? string.Empty).IndexOf("mpeg", StringComparison.OrdinalIgnoreCase) != -1)
		{
			List<string> source = (video.Container ?? string.Empty).Split(',').ToList();
			if (!source.Contains("vob", StringComparer.OrdinalIgnoreCase) && !source.Contains("m2ts", StringComparer.OrdinalIgnoreCase) && !source.Contains("ts", StringComparer.OrdinalIgnoreCase) && !source.Contains("mpegts", StringComparer.OrdinalIgnoreCase))
			{
				return source.Contains("wtv", StringComparer.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	public Task<string> ExtractAudioImage(string path, int? imageStreamIndex, CancellationToken cancellationToken)
	{
		return ExtractImage(new string[1] { path }, null, null, imageStreamIndex, (MediaProtocol)0, isAudio: true, null, null, cancellationToken);
	}

	public Task<string> ExtractVideoImage(string[] inputFiles, string container, MediaProtocol protocol, MediaStream videoStream, Video3DFormat? threedFormat, TimeSpan? offset, CancellationToken cancellationToken)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		return ExtractImage(inputFiles, container, videoStream, null, protocol, isAudio: false, threedFormat, offset, cancellationToken);
	}

	public Task<string> ExtractVideoImage(string[] inputFiles, string container, MediaProtocol protocol, MediaStream imageStream, int? imageStreamIndex, CancellationToken cancellationToken)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		return ExtractImage(inputFiles, container, imageStream, imageStreamIndex, protocol, isAudio: false, null, null, cancellationToken);
	}

	private async Task<string> ExtractImage(string[] inputFiles, string container, MediaStream videoStream, int? imageStreamIndex, MediaProtocol protocol, bool isAudio, Video3DFormat? threedFormat, TimeSpan? offset, CancellationToken cancellationToken)
	{
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		string inputArgument = GetInputArgument(inputFiles, protocol);
		if (isAudio)
		{
			if (imageStreamIndex.HasValue && imageStreamIndex.Value > 0)
			{
				imageStreamIndex = imageStreamIndex.Value - 1;
			}
		}
		else
		{
			try
			{
				return await ExtractImageInternal(inputArgument, container, videoStream, imageStreamIndex, threedFormat, offset, useIFrame: true, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (ArgumentException)
			{
				throw;
			}
			catch
			{
				_logger.Error("I-frame image extraction failed, will attempt standard way. Input: {0}", new object[1] { inputArgument });
			}
		}
		return await ExtractImageInternal(inputArgument, container, videoStream, imageStreamIndex, threedFormat, offset, useIFrame: false, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<string> ExtractImageInternal(string inputPath, string container, MediaStream videoStream, int? imageStreamIndex, Video3DFormat? threedFormat, TimeSpan? offset, bool useIFrame, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(inputPath))
		{
			throw new ArgumentNullException("inputPath");
		}
		string tempExtractPath = Path.Combine(((IApplicationPaths)ConfigurationManager.ApplicationPaths).TempDirectory, string.Concat(Guid.NewGuid(), ".jpg"));
		FileSystem.CreateDirectory(FileSystem.GetDirectoryName(tempExtractPath));
		EncodingHelper val = new EncodingHelper((IMediaEncoder)(object)this, FileSystem, SubtitleEncoder(), (IApplicationPaths)(object)ConfigurationManager.ApplicationPaths, _assemblyInfo);
		int? num = ((videoStream == null) ? ((int?)null) : videoStream.Width);
		int? num2 = ((videoStream == null) ? ((int?)null) : videoStream.Height);
		string text = string.Join(",", val.GetScalingFilters(num, num2, threedFormat, (string)null, (string)null, (int?)600, (int?)null, (int?)null, (int?)null));
		string text2 = (imageStreamIndex.HasValue ? (" -map 0:v:" + imageStreamIndex.Value.ToString(CultureInfo.InvariantCulture)) : string.Empty);
		string text3 = ((!new string[1] { "wtv" }.Contains(container ?? string.Empty, StringComparer.OrdinalIgnoreCase)) ? ",thumbnail=24" : string.Empty);
		string text4 = (useIFrame ? string.Format("-i {0}{3} -threads 0 -v quiet -vframes 1 -vf \"{2}{4}\" -f image2 \"{1}\"", inputPath, tempExtractPath, text, text2, text3) : string.Format("-i {0}{3} -threads 0 -v quiet -vframes 1 -vf \"{2}\" -f image2 \"{1}\"", inputPath, tempExtractPath, text, text2));
		string probeSizeArgument = EncodingHelper.GetProbeSizeArgument(1);
		string analyzeDurationArgument = EncodingHelper.GetAnalyzeDurationArgument(1);
		if (!string.IsNullOrWhiteSpace(probeSizeArgument))
		{
			text4 = probeSizeArgument + " " + text4;
		}
		if (!string.IsNullOrWhiteSpace(analyzeDurationArgument))
		{
			text4 = analyzeDurationArgument + " " + text4;
		}
		if (offset.HasValue)
		{
			text4 = $"-ss {GetTimeParameter(offset.Value)} " + text4;
		}
		if (!string.IsNullOrWhiteSpace(container))
		{
			string inputFormat = val.GetInputFormat(container);
			if (!string.IsNullOrWhiteSpace(inputFormat))
			{
				text4 = "-f " + inputFormat + " " + text4;
			}
		}
		IProcess val2 = _processFactory.Create(new ProcessOptions
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			FileName = FFMpegPath,
			Arguments = text4,
			IsHidden = true,
			ErrorDialog = false,
			EnableRaisingEvents = true
		});
		_logger.Debug("{0} {1}", new object[2]
		{
			val2.StartInfo.FileName,
			val2.StartInfo.Arguments
		});
		using ProcessWrapper processWrapper = new ProcessWrapper(val2, this, _logger);
		StartProcess(processWrapper);
		int num3 = ConfigurationManager.Configuration.ImageExtractionTimeoutMs;
		if (num3 <= 0)
		{
			num3 = DefaultImageExtractionTimeoutMs;
		}
		bool ranToCompletion = await val2.WaitForExitAsync(num3).ConfigureAwait(continueOnCapturedContext: false);
		if (!ranToCompletion)
		{
			await StopProcess(processWrapper, 1000).ConfigureAwait(continueOnCapturedContext: false);
		}
		int num4 = ((!ranToCompletion) ? (-1) : (processWrapper.ExitCode ?? 0));
		FileSystemMetadata fileInfo = FileSystem.GetFileInfo(tempExtractPath);
		if (num4 == -1 || !fileInfo.Exists || fileInfo.Length == 0L)
		{
			string text5 = $"ffmpeg image extraction failed for {inputPath}";
			_logger.Error(text5, Array.Empty<object>());
			throw new Exception(text5);
		}
		return tempExtractPath;
	}

	public async Task ConvertImage(string inputPath, string outputPath)
	{
		if (string.IsNullOrEmpty(inputPath))
		{
			throw new ArgumentNullException("inputPath");
		}
		FileSystem.CreateDirectory(FileSystem.GetDirectoryName(outputPath));
		string arguments = $"-i \"{inputPath}\" \"{outputPath}\"";
		IProcess val = _processFactory.Create(new ProcessOptions
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			FileName = FFMpegPath,
			Arguments = arguments,
			IsHidden = true,
			ErrorDialog = false,
			EnableRaisingEvents = true
		});
		_logger.Debug("{0} {1}", new object[2]
		{
			val.StartInfo.FileName,
			val.StartInfo.Arguments
		});
		using ProcessWrapper processWrapper = new ProcessWrapper(val, this, _logger);
		StartProcess(processWrapper);
		bool ranToCompletion = await val.WaitForExitAsync(DefaultImageExtractionTimeoutMs).ConfigureAwait(continueOnCapturedContext: false);
		if (!ranToCompletion)
		{
			await StopProcess(processWrapper, 1000).ConfigureAwait(continueOnCapturedContext: false);
		}
		int num = ((!ranToCompletion) ? (-1) : (processWrapper.ExitCode ?? 0));
		FileSystemMetadata fileInfo = FileSystem.GetFileInfo(outputPath);
		if (num == -1 || !fileInfo.Exists || fileInfo.Length == 0L)
		{
			string text = $"ffmpeg image conversion failed for {inputPath}";
			_logger.Error(text, Array.Empty<object>());
			throw new Exception(text);
		}
	}

	public string GetTimeParameter(long ticks)
	{
		TimeSpan time = TimeSpan.FromTicks(ticks);
		return GetTimeParameter(time);
	}

	public string GetTimeParameter(TimeSpan time)
	{
		return time.ToString("hh\\:mm\\:ss\\.fff", UsCulture);
	}

	public async Task ExtractVideoImagesOnInterval(string[] inputFiles, string container, MediaStream videoStream, MediaProtocol protocol, Video3DFormat? threedFormat, TimeSpan interval, string targetDirectory, string filenamePrefix, int? maxWidth, CancellationToken cancellationToken)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		SemaphoreSlim resourcePool = _thumbnailResourcePool;
		string inputArgument = GetInputArgument(inputFiles, protocol);
		string text = "fps=fps=1/" + interval.TotalSeconds.ToString(UsCulture);
		if (maxWidth.HasValue)
		{
			string arg = maxWidth.Value.ToString(UsCulture);
			text += $",scale=min(iw\\,{arg}):trunc(ow/dar/2)*2";
		}
		FileSystem.CreateDirectory(targetDirectory);
		string arg2 = Path.Combine(targetDirectory, filenamePrefix + "%05d.jpg");
		string text2 = string.Format("-i {0} -threads 0 -v quiet -vf \"{2}\" -f image2 \"{1}\"", inputArgument, arg2, text);
		string probeSizeArgument = EncodingHelper.GetProbeSizeArgument(1);
		string analyzeDurationArgument = EncodingHelper.GetAnalyzeDurationArgument(1);
		if (!string.IsNullOrWhiteSpace(probeSizeArgument))
		{
			text2 = probeSizeArgument + " " + text2;
		}
		if (!string.IsNullOrWhiteSpace(analyzeDurationArgument))
		{
			text2 = analyzeDurationArgument + " " + text2;
		}
		EncodingHelper val = new EncodingHelper((IMediaEncoder)(object)this, FileSystem, SubtitleEncoder(), (IApplicationPaths)(object)ConfigurationManager.ApplicationPaths, _assemblyInfo);
		if (videoStream != null)
		{
			string hardwareAcceleratedVideoDecoder = val.GetHardwareAcceleratedVideoDecoder((VideoType)0, videoStream, GetEncodingOptions());
			if (!string.IsNullOrWhiteSpace(hardwareAcceleratedVideoDecoder))
			{
				text2 = hardwareAcceleratedVideoDecoder + " " + text2;
			}
		}
		if (!string.IsNullOrWhiteSpace(container))
		{
			string inputFormat = val.GetInputFormat(container);
			if (!string.IsNullOrWhiteSpace(inputFormat))
			{
				text2 = "-f " + inputFormat + " " + text2;
			}
		}
		IProcess process = _processFactory.Create(new ProcessOptions
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			FileName = FFMpegPath,
			Arguments = text2,
			IsHidden = true,
			ErrorDialog = false,
			EnableRaisingEvents = true
		});
		_logger.Info(process.StartInfo.FileName + " " + process.StartInfo.Arguments, Array.Empty<object>());
		await resourcePool.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		bool ranToCompletion = false;
		using ProcessWrapper processWrapper = new ProcessWrapper(process, this, _logger);
		try
		{
			StartProcess(processWrapper);
			int totalImageCount = 0;
			while (true)
			{
				if (await process.WaitForExitAsync(30000).ConfigureAwait(continueOnCapturedContext: false))
				{
					ranToCompletion = true;
					break;
				}
				cancellationToken.ThrowIfCancellationRequested();
				int num2 = FileSystem.GetFilePaths(targetDirectory, new string[1] { ".jpg" }, false, false).Count();
				if (num2 == totalImageCount)
				{
					break;
				}
				totalImageCount = num2;
			}
			if (!ranToCompletion)
			{
				await StopProcess(processWrapper, 1000).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		finally
		{
			resourcePool.Release();
		}
		if (((!ranToCompletion) ? (-1) : (processWrapper.ExitCode ?? 0)) == -1)
		{
			string text3 = $"ffmpeg image extraction failed for {inputArgument}";
			_logger.Error(text3, Array.Empty<object>());
			throw new Exception(text3);
		}
	}

	public async Task<string> EncodeAudio(EncodingJobOptions options, IProgress<double> progress, CancellationToken cancellationToken)
	{
		EncodingOptions encodingOptions = GetEncodingOptions();
		EncodingJobInfo job = await new AudioEncoder(this, _logger, ConfigurationManager, FileSystem, IsoManager, LibraryManager, SessionManager, SubtitleEncoder(), MediaSourceManager(), _processFactory, _assemblyInfo, _jsonSerializer, _appHost).Start(options, encodingOptions, progress, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await job.TaskCompletionSource.Task.ConfigureAwait(continueOnCapturedContext: false);
		return job.OutputFilePath;
	}

	public async Task<string> EncodeVideo(EncodingJobOptions options, IProgress<double> progress, CancellationToken cancellationToken)
	{
		EncodingOptions encodingOptions = GetEncodingOptions();
		EncodingJobInfo job = await new VideoEncoder(this, _logger, ConfigurationManager, FileSystem, IsoManager, LibraryManager, SessionManager, SubtitleEncoder(), MediaSourceManager(), _processFactory, _assemblyInfo, _jsonSerializer, _appHost).Start(options, encodingOptions, progress, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await job.TaskCompletionSource.Task.ConfigureAwait(continueOnCapturedContext: false);
		return job.OutputFilePath;
	}

	private void StartProcess(ProcessWrapper process)
	{
		process.Start();
		lock (_runningProcesses)
		{
			_runningProcesses.Add(process);
		}
	}

	private async Task StopProcess(ProcessWrapper process, int waitTimeMs)
	{
		try
		{
			if (await process.Process.WaitForExitAsync(waitTimeMs).ConfigureAwait(continueOnCapturedContext: false))
			{
				return;
			}
		}
		catch (Exception ex)
		{
			_logger.Error("Error in WaitForExit", new object[1] { ex });
		}
		try
		{
			if (!process.HasExited)
			{
				_logger.Info("Killing ffmpeg process", Array.Empty<object>());
				process.Process.Kill();
			}
		}
		catch (Exception ex2)
		{
			_logger.ErrorException("Error killing process", ex2, Array.Empty<object>());
		}
	}

	private async Task StopProcesses()
	{
		List<ProcessWrapper> list;
		lock (_runningProcesses)
		{
			list = _runningProcesses.ToList();
			_runningProcesses.Clear();
		}
		foreach (ProcessWrapper item in list)
		{
			if (!item.HasExited)
			{
				await StopProcess(item, 500).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	public string EscapeSubtitleFilterPath(string path)
	{
		return path.Replace('\\', '/').Replace(":", "\\:").Replace("'", "'\\\\\\''");
	}

	public string[] GetPlayableStreamFileNames(string path, VideoType videoType)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Invalid comparison between Unknown and I4
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Invalid comparison between Unknown and I4
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		if ((int)videoType == 2)
		{
			Title val = new Dvd(path, FileSystem).Titles.OrderByDescending(GetRuntime).FirstOrDefault();
			byte? b = null;
			if (val != null)
			{
				b = val.VideoTitleSetNumber;
			}
			return GetPrimaryPlaylistVobFiles(path, null, b).Select(Path.GetFileName).ToArray();
		}
		if ((int)videoType == 3)
		{
			return _blurayExaminer.GetDiscInfo(path).Files;
		}
		throw new ArgumentException();
	}

	private long GetRuntime(Title title)
	{
		return (from i in title.ProgramChains
			select (TimeSpan)i.PlaybackTime into i
			select i.Ticks).Sum();
	}

	public IEnumerable<string> GetPrimaryPlaylistVobFiles(string path, IIsoMount isoMount, uint? titleNumber)
	{
		string text = ((isoMount != null) ? isoMount.MountedPath : path);
		List<FileSystemMetadata> list = (from i in FileSystem.GetFiles(text, new string[1] { ".vob" }, false, true)
			orderby i.FullName
			select i).ToList();
		if (list.Count == 0)
		{
			_logger.Error("No vobs found in dvd structure.", Array.Empty<object>());
			return Array.Empty<string>();
		}
		if (titleNumber.HasValue)
		{
			string prefix = $"VTS_0{titleNumber.Value.ToString(UsCulture)}_";
			List<FileSystemMetadata> list2 = list.Where((FileSystemMetadata i) => i.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
			if (list2.Count > 0)
			{
				List<FileSystemMetadata> list3 = list2.SkipWhile((FileSystemMetadata f) => f.Length < 314572800).ToList();
				if (list3.Count != 0)
				{
					return list3.Select((FileSystemMetadata i) => i.FullName);
				}
				return list2.Select((FileSystemMetadata i) => i.FullName);
			}
			_logger.Info("Could not determine vob file list for {0} using DvdLib. Will scan using file sizes.", new object[1] { path });
		}
		List<FileSystemMetadata> list4 = list.SkipWhile((FileSystemMetadata f) => f.Length < 314572800).ToList();
		if (list4.Count == 0)
		{
			_logger.Warn("Vob size filter resulted in zero matches. Taking all vobs.", Array.Empty<object>());
			list4 = list;
		}
		if (list4.Count > 0)
		{
			string[] array = FileSystem.GetFileNameWithoutExtension(list4[0]).Split('_');
			if (array.Length == 3)
			{
				string title = array[1];
				list4 = list4.TakeWhile((FileSystemMetadata f) =>
				{
					string[] array2 = FileSystem.GetFileNameWithoutExtension(f).Split('_');
					return array2.Length == 3 && string.Equals(title, array2[1], StringComparison.OrdinalIgnoreCase);
				}).ToList();
				if (list4.Count == 0)
				{
					_logger.Warn("Vob filename filter resulted in zero matches. Taking all vobs.", Array.Empty<object>());
					list4 = list;
				}
			}
		}
		return list4.Select((FileSystemMetadata i) => i.FullName);
	}

	public void Dispose()
	{
		Dispose(dispose: true);
	}

	protected virtual void Dispose(bool dispose)
	{
		if (dispose)
		{
			Task task = StopProcesses();
			Task.WaitAll(task);
		}
	}
}
