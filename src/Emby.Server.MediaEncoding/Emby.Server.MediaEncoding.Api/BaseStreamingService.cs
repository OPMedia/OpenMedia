using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.MediaEncoding.Encoder;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;

namespace Emby.Server.MediaEncoding.Api;

public abstract class BaseStreamingService : BaseApiService
{
	protected readonly CultureInfo UsCulture = new CultureInfo("en-US");

	protected IServerConfigurationManager ServerConfigurationManager { get; private set; }

	protected IUserManager UserManager { get; private set; }

	protected ILibraryManager LibraryManager { get; private set; }

	protected IIsoManager IsoManager { get; private set; }

	protected IMediaEncoder MediaEncoder { get; private set; }

	protected IFileSystem FileSystem { get; private set; }

	protected IDlnaManager DlnaManager { get; private set; }

	protected IDeviceManager DeviceManager { get; private set; }

	protected ISubtitleEncoder SubtitleEncoder { get; private set; }

	protected IMediaSourceManager MediaSourceManager { get; private set; }

	protected IZipClient ZipClient { get; private set; }

	protected IJsonSerializer JsonSerializer { get; private set; }

	protected IAuthorizationContext AuthorizationContext { get; private set; }

	protected EncodingHelper EncodingHelper { get; set; }

	protected IServerApplicationHost AppHost { get; private set; }

	protected IAssemblyInfo AssemblyInfo { get; set; }

	protected abstract TranscodingJobType TranscodingJobType { get; }

	protected virtual bool EnableOutputInSubFolder => false;

	protected BaseStreamingService(IServerConfigurationManager serverConfig, IUserManager userManager, ILibraryManager libraryManager, IIsoManager isoManager, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IDlnaManager dlnaManager, ISubtitleEncoder subtitleEncoder, IDeviceManager deviceManager, IMediaSourceManager mediaSourceManager, IZipClient zipClient, IJsonSerializer jsonSerializer, IAuthorizationContext authorizationContext, IServerApplicationHost appHost, IAssemblyInfo assemblyInfo)
	{
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Expected Obj, but got Unknown
		JsonSerializer = jsonSerializer;
		AuthorizationContext = authorizationContext;
		AppHost = appHost;
		ZipClient = zipClient;
		MediaSourceManager = mediaSourceManager;
		DeviceManager = deviceManager;
		SubtitleEncoder = subtitleEncoder;
		DlnaManager = dlnaManager;
		FileSystem = fileSystem;
		ServerConfigurationManager = serverConfig;
		UserManager = userManager;
		LibraryManager = libraryManager;
		IsoManager = isoManager;
		MediaEncoder = mediaEncoder;
		AssemblyInfo = assemblyInfo;
		EncodingHelper = new EncodingHelper(MediaEncoder, FileSystem, SubtitleEncoder, (IApplicationPaths)(object)serverConfig.ApplicationPaths, AssemblyInfo);
	}

	protected abstract string GetCommandLineArguments(string outputPath, EncodingOptions encodingOptions, StreamState state, bool isEncoding);

	protected virtual string GetOutputFileExtension(StreamState state)
	{
		return Path.GetExtension(state.RequestedUrl);
	}

	protected virtual string GetOutputFilePath(StreamState state, EncodingOptions encodingOptions, string outputFileExtension)
	{
		string tempTranscodingPath = ApiEntryPoint.Instance.GetTempTranscodingPath(state.Request.PlaySessionId);
		string text = BaseExtensions.GetMD5(string.Concat(GetCommandLineArguments("dummy\\dummy", encodingOptions, state, isEncoding: false) + "-" + (((BaseEncodingJobOptions)state.Request).DeviceId ?? string.Empty), "-", state.Request.PlaySessionId ?? string.Empty)).ToString("N");
		if (EnableOutputInSubFolder)
		{
			return Path.Combine(tempTranscodingPath, text, text + (outputFileExtension ?? string.Empty).ToLower());
		}
		return Path.Combine(tempTranscodingPath, text + (outputFileExtension ?? string.Empty).ToLower());
	}

	protected virtual string GetDefaultH264Preset()
	{
		return "superfast";
	}

	private async Task AcquireResources(StreamState state, CancellationToken cancellationToken)
	{
		if ((int)((EncodingJobInfo)state).VideoType == 1 && ((EncodingJobInfo)state).IsoType.HasValue && IsoManager.CanMount(((EncodingJobInfo)state).MediaPath))
		{
			((EncodingJobInfo)state).IsoMount = await IsoManager.Mount(((EncodingJobInfo)state).MediaPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (((EncodingJobInfo)state).MediaSource.RequiresOpening && string.IsNullOrWhiteSpace(((BaseEncodingJobOptions)state.Request).LiveStreamId))
		{
			LiveStreamResponse val = await MediaSourceManager.OpenLiveStream(new LiveStreamRequest
			{
				OpenToken = ((EncodingJobInfo)state).MediaSource.OpenToken
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			EncodingHelper.AttachMediaSourceInfo((EncodingJobInfo)(object)state, val.MediaSource, state.RequestedUrl);
			if (((EncodingJobInfo)state).IsVideoRequest)
			{
				EncodingHelper.TryStreamCopy((EncodingJobInfo)(object)state);
			}
		}
		if (((EncodingJobInfo)state).MediaSource.BufferMs.HasValue)
		{
			await Task.Delay(((EncodingJobInfo)state).MediaSource.BufferMs.Value, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private Task LogInitialInfo(StreamState state, string commandLineLogMessage, CancellationToken cancellationToken)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
		stringBuilder.AppendLine($"Application version: {((IApplicationHost)AppHost).ApplicationVersion}");
		stringBuilder.AppendLine(string.Empty);
		stringBuilder.AppendLine(Request.AbsoluteUri);
		stringBuilder.AppendLine(string.Empty);
		stringBuilder.AppendLine(JsonSerializer.SerializeToString((object)((EncodingJobInfo)state).MediaSource));
		stringBuilder.AppendLine(string.Empty);
		User user = ((EncodingJobInfo)state).User;
		if (user != null)
		{
			stringBuilder.AppendLine($"User policy for {((BaseItem)user).Name}: EnablePlaybackRemuxing: {user.Policy.EnablePlaybackRemuxing} EnableVideoPlaybackTranscoding: {user.Policy.EnableVideoPlaybackTranscoding} EnableAudioPlaybackTranscoding: {user.Policy.EnableAudioPlaybackTranscoding}");
			stringBuilder.AppendLine(string.Empty);
		}
		stringBuilder.AppendLine(commandLineLogMessage);
		byte[] bytes = Encoding.UTF8.GetBytes(stringBuilder.ToString());
		return ((EncodingJobInfo)state).LogFileStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
	}

	protected async Task<TranscodingJob> StartFfMpeg(StreamState state, string outputPath, EncodingOptions encodingOptions, CancellationTokenSource cancellationTokenSource, bool acquireResources = true)
	{
		FileSystem.CreateDirectory(FileSystem.GetDirectoryName(outputPath));
		encodingOptions = CloneOptions(encodingOptions);
		if (acquireResources)
		{
			await AcquireResources(state, cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (((EncodingJobInfo)state).User != null)
		{
			if (((EncodingJobInfo)state).IsVideoRequest)
			{
				if (string.Equals(((EncodingJobInfo)state).OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
				{
					if (!((EncodingJobInfo)state).User.Policy.EnablePlaybackRemuxing && !((EncodingJobInfo)state).User.Policy.EnableVideoPlaybackTranscoding)
					{
						ApiEntryPoint.Instance.OnTranscodeFailedToStart(outputPath, TranscodingJobType, state);
						throw new ArgumentException("User does not have video remuxing permission.");
					}
				}
				else if (!((EncodingJobInfo)state).User.Policy.EnableVideoPlaybackTranscoding)
				{
					ApiEntryPoint.Instance.OnTranscodeFailedToStart(outputPath, TranscodingJobType, state);
					throw new ArgumentException("User does not have video transcoding permission.");
				}
			}
			else if (!((EncodingJobInfo)state).User.Policy.EnableAudioPlaybackTranscoding)
			{
				ApiEntryPoint.Instance.OnTranscodeFailedToStart(outputPath, TranscodingJobType, state);
				throw new ArgumentException("User does not have audio transcoding permission.");
			}
		}
		if (!((EncodingJobInfo)state).MediaSource.SupportsTranscoding)
		{
			ApiEntryPoint.Instance.OnTranscodeFailedToStart(outputPath, TranscodingJobType, state);
			throw new ArgumentException("MediaSource does not support conversion.");
		}
		Guid transcodingId = Guid.NewGuid();
		string commandLineArguments = GetCommandLineArguments(outputPath, encodingOptions, state, isEncoding: true);
		IProcess process = ApiEntryPoint.Instance.ProcessFactory.Create(new ProcessOptions
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			FileName = MediaEncoder.EncoderPath,
			Arguments = commandLineArguments,
			IsHidden = true,
			ErrorDialog = false,
			EnableRaisingEvents = true
		});
		string text = process.StartInfo.FileName + " " + process.StartInfo.Arguments;
		Logger.Info(text, Array.Empty<object>());
		try
		{
			process.Start();
		}
		catch (Exception ex)
		{
			Logger.ErrorException("Error starting ffmpeg", ex, Array.Empty<object>());
			ApiEntryPoint.Instance.OnTranscodeFailedToStart(outputPath, TranscodingJobType, state);
			throw;
		}
		if (((EncodingJobInfo)state).LogFileStream == null)
		{
			string text2 = "ffmpeg-transcode";
			if (((EncodingJobInfo)state).IsVideoRequest && string.Equals(((EncodingJobInfo)state).OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase) && string.Equals(((EncodingJobInfo)state).OutputAudioCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				text2 = "ffmpeg-directstream";
			}
			else if (((EncodingJobInfo)state).IsVideoRequest && string.Equals(((EncodingJobInfo)state).OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				text2 = "ffmpeg-remux";
			}
			string text3 = Path.Combine(((IApplicationPaths)ServerConfigurationManager.ApplicationPaths).LogDirectoryPath, string.Concat(text2, "-", Guid.NewGuid(), ".txt"));
			FileSystem.CreateDirectory(FileSystem.GetDirectoryName(text3));
			((EncodingJobInfo)state).LogFileStream = FileSystem.GetFileStream(text3, (FileOpenMode)2, (FileAccessMode)2, (FileShareMode)1, true);
			await LogInitialInfo(state, text, cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
		}
		new JobLogger(Logger).StartStreamingLog((EncodingJobInfo)(object)state, process.StandardError.BaseStream, ((EncodingJobInfo)state).LogFileStream);
		bool hasProcessExited = false;
		EventHandler onExited = (object sender, EventArgs args) =>
		{
			hasProcessExited = true;
		};
		process.Exited += onExited;
		bool processStarted = false;
		while (!hasProcessExited)
		{
			processStarted = ValidateProcessStarted(state, outputPath);
			if (processStarted)
			{
				break;
			}
			await Task.Delay(50, cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
		}
		process.Exited -= onExited;
		if (hasProcessExited && !processStarted)
		{
			// BUG-23：短视频（如 8s 素材 remux ~90ms 完成）可能在 50ms 轮询窗口内跑完退出，
			// 但输出文件已真实落盘——退出后补一次文件检查，避免误判"启动失败"。
			processStarted = ValidateProcessStarted(state, outputPath);
		}
		if (hasProcessExited && !processStarted)
		{
			try
			{
				((IDisposable)process).Dispose();
			}
			catch
			{
			}
			if (encodingOptions.EnableHardwareEncoding || !string.IsNullOrWhiteSpace(EncodingHelper.GetHardwareAcceleratedVideoDecoder(((EncodingJobInfo)state).VideoType, ((EncodingJobInfo)state).VideoStream, encodingOptions)))
			{
				encodingOptions.EnableHardwareEncoding = false;
				encodingOptions.HardwareDecodingCodecs = Array.Empty<string>();
				return await StartFfMpeg(state, outputPath, encodingOptions, cancellationTokenSource, acquireResources: false).ConfigureAwait(continueOnCapturedContext: false);
			}
			throw new Exception("Error starting ffmpeg");
		}
		Logger.Info("ffmpeg successfully started", Array.Empty<object>());
		TranscodingJob transcodingJob = ApiEntryPoint.Instance.OnTranscodeBeginning(outputPath, state.Request.PlaySessionId, ((EncodingJobInfo)state).MediaSource.LiveStreamId, transcodingId, TranscodingJobType, process, ((BaseEncodingJobOptions)state.Request).DeviceId, state, cancellationTokenSource);
		state.TranscodingJob = transcodingJob;
		bool exitHandled = false;
		process.Exited += (object sender, EventArgs args) =>
		{
			if (!exitHandled)
			{
				exitHandled = true;
				OnFfMpegProcessExited(process, transcodingJob, state);
			}
		};
		if (hasProcessExited && !exitHandled)
		{
			// BUG-23 后半：进程在挂接 Exited 前已退出时事件不会补发，
			// 直接执行退出处理（置 HasExited/释放资源），保证 job 状态与清理链正确。
			exitHandled = true;
			OnFfMpegProcessExited(process, transcodingJob, state);
		}
		if (((EncodingJobInfo)state).IsInputVideo && (int)transcodingJob.Type == 0 && !transcodingJob.HasExited)
		{
			await Task.Delay(1000, cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (((EncodingJobInfo)state).ReadInputAtNativeFramerate && !transcodingJob.HasExited)
			{
				await Task.Delay(1500, cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		return transcodingJob;
	}

	protected virtual bool ValidateProcessStarted(StreamState state, string outputPath)
	{
		return FileSystem.FileExists(outputPath);
	}

	private EncodingOptions CloneOptions(EncodingOptions options)
	{
		return JsonSerializer.DeserializeFromString<EncodingOptions>(JsonSerializer.SerializeToString((object)options));
	}

	private void OnFfMpegProcessExited(IProcess process, TranscodingJob job, StreamState state)
	{
		if (job != null)
		{
			job.HasExited = true;
		}
		Logger.Debug("Disposing stream resources", Array.Empty<object>());
		((EncodingJobInfo)state).Dispose();
		try
		{
			Logger.Info("FFMpeg exited with code {0}", new object[1] { process.ExitCode });
		}
		catch
		{
			Logger.Error("FFMpeg exited with an error.", Array.Empty<object>());
		}
	}

	private void ParseDlnaHeaders(StreamRequest request)
	{
		if (!((BaseEncodingJobOptions)request).StartTimeTicks.HasValue)
		{
			string header = GetHeader("TimeSeekRange.dlna.org");
			((BaseEncodingJobOptions)request).StartTimeTicks = ParseTimeSeekHeader(header);
		}
	}

	private long ParseTimeSeekHeader(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return 0L;
		}
		if (value.IndexOf("npt=", StringComparison.OrdinalIgnoreCase) != 0)
		{
			throw new ArgumentException("Invalid timeseek header");
		}
		value = value.Substring(4).Split(new char[1] { '-' }, 2)[0];
		if (value.IndexOf(':') == -1)
		{
			if (double.TryParse(value, NumberStyles.Any, UsCulture, out var result))
			{
				return TimeSpan.FromSeconds(result).Ticks;
			}
			throw new ArgumentException("Invalid timeseek header");
		}
		string[] array = value.Split(new char[1] { ':' }, 3);
		double num = 0.0;
		int num2 = 3600;
		string[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			if (double.TryParse(array2[i], NumberStyles.Any, UsCulture, out var result2))
			{
				num += result2 * (double)num2;
				num2 /= 60;
				continue;
			}
			throw new ArgumentException("Invalid timeseek header");
		}
		return TimeSpan.FromSeconds(num).Ticks;
	}

	protected async Task<StreamState> GetState(StreamRequest request, CancellationToken cancellationToken)
	{
		ParseDlnaHeaders(request);
		string url = Request.PathInfo;
		if (string.IsNullOrEmpty(((BaseEncodingJobOptions)request).AudioCodec))
		{
			((BaseEncodingJobOptions)request).AudioCodec = EncodingHelper.InferAudioCodec(Path.GetExtension(url));
		}
		bool isVideoRequest = request is VideoStreamRequest && url.IndexOf("/Audio", StringComparison.OrdinalIgnoreCase) == -1;
		StreamState state = new StreamState(MediaSourceManager, Logger, TranscodingJobType, isVideoRequest)
		{
			Request = request,
			RequestedUrl = url,
			UserAgent = Request.UserAgent
		};
		foreach (NameValuePair item in (List<NameValuePair>)(object)Request.QueryString)
		{
			((BaseEncodingJobOptions)request).StreamOptions[item.Name] = item.Value;
		}
		AuthorizationInfo authorizationInfo = AuthorizationContext.GetAuthorizationInfo(Request);
		((EncodingJobInfo)state).User = authorizationInfo.User;
		if (((EncodingJobInfo)state).IsVideoRequest && !string.IsNullOrWhiteSpace(((BaseEncodingJobOptions)state.VideoRequest).VideoCodec))
		{
			((EncodingJobInfo)state).SupportedVideoCodecs = ((BaseEncodingJobOptions)state.VideoRequest).VideoCodec.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			((BaseEncodingJobOptions)state.VideoRequest).VideoCodec = ((EncodingJobInfo)state).SupportedVideoCodecs.FirstOrDefault();
		}
		if (!string.IsNullOrWhiteSpace(((BaseEncodingJobOptions)request).SubtitleCodec))
		{
			((EncodingJobInfo)state).SupportedSubtitleCodecs = ((BaseEncodingJobOptions)request).SubtitleCodec.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			((BaseEncodingJobOptions)state.Request).SubtitleCodec = ((EncodingJobInfo)state).SupportedSubtitleCodecs.FirstOrDefault((string i) => ((ITranscoderSupport)MediaEncoder).CanEncodeToSubtitleCodec(i)) ?? ((EncodingJobInfo)state).SupportedSubtitleCodecs.FirstOrDefault();
		}
		BaseItem itemById = LibraryManager.GetItemById(((BaseEncodingJobOptions)request).Id);
		((EncodingJobInfo)state).IsInputVideo = string.Equals(itemById.MediaType, "Video", StringComparison.OrdinalIgnoreCase);
		MediaSourceInfo val = null;
		if (string.IsNullOrWhiteSpace(((BaseEncodingJobOptions)request).LiveStreamId))
		{
			TranscodingJob transcodingJob = ((!string.IsNullOrWhiteSpace(request.PlaySessionId)) ? ApiEntryPoint.Instance.GetTranscodingJob(request.PlaySessionId) : null);
			if (transcodingJob != null)
			{
				val = transcodingJob.MediaSource;
			}
			if (val == null)
			{
				List<MediaSourceInfo> source = (await MediaSourceManager.GetPlayackMediaSources(itemById, (User)null, false, false, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).ToList();
				val = (string.IsNullOrEmpty(((BaseEncodingJobOptions)request).MediaSourceId) ? source.First() : source.FirstOrDefault((MediaSourceInfo i) => string.Equals(i.Id, ((BaseEncodingJobOptions)request).MediaSourceId)));
				if (val == null && string.Equals(((BaseEncodingJobOptions)request).Id.ToString("N"), ((BaseEncodingJobOptions)request).MediaSourceId, StringComparison.OrdinalIgnoreCase))
				{
					val = source.First();
				}
			}
		}
		else
		{
			Tuple<MediaSourceInfo, IDirectStreamProvider> tuple = await MediaSourceManager.GetLiveStreamWithDirectStreamProvider(((BaseEncodingJobOptions)request).LiveStreamId, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			val = tuple.Item1;
			state.DirectStreamProvider = tuple.Item2;
		}
		VideoStreamRequest videoStreamRequest = request as VideoStreamRequest;
		EncodingHelper.AttachMediaSourceInfo((EncodingJobInfo)(object)state, val, url);
		string text = Path.GetExtension(state.RequestedUrl);
		if (string.IsNullOrEmpty(text))
		{
			text = ((BaseEncodingJobOptions)request).Container;
		}
		if (string.IsNullOrEmpty(text))
		{
			text = (((BaseEncodingJobOptions)request).Static ? StreamBuilder.NormalizeMediaSourceFormatIntoSingleContainer(((EncodingJobInfo)state).InputContainer, ((EncodingJobInfo)state).MediaPath, (DeviceProfile)null, (DlnaProfileType)0) : GetOutputFileExtension(state));
		}
		((EncodingJobInfo)state).OutputContainer = (text ?? string.Empty).TrimStart('.');
		((EncodingJobInfo)state).OutputAudioBitrate = EncodingHelper.GetAudioBitrateParam((BaseEncodingJobOptions)(object)state.Request, ((EncodingJobInfo)state).AudioStream);
		((EncodingJobInfo)state).OutputAudioCodec = ((BaseEncodingJobOptions)state.Request).AudioCodec;
		((EncodingJobInfo)state).OutputAudioChannels = EncodingHelper.GetNumAudioChannelsParam((EncodingJobInfo)(object)state, ((EncodingJobInfo)state).AudioStream, ((EncodingJobInfo)state).OutputAudioCodec);
		if (((EncodingJobInfo)state).IsVideoRequest)
		{
			((EncodingJobInfo)state).OutputVideoCodec = ((BaseEncodingJobOptions)state.VideoRequest).VideoCodec;
			((EncodingJobInfo)state).OutputVideoBitrate = EncodingHelper.GetVideoBitrateParamValue((BaseEncodingJobOptions)(object)state.VideoRequest, ((EncodingJobInfo)state).VideoStream, ((EncodingJobInfo)state).OutputVideoCodec);
			EncodingHelper.TryStreamCopy((EncodingJobInfo)(object)state);
			if (((EncodingJobInfo)state).OutputVideoBitrate.HasValue && !string.Equals(((EncodingJobInfo)state).OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				ResolutionOptions val2 = ResolutionNormalizer.Normalize((((EncodingJobInfo)state).VideoStream == null) ? ((int?)null) : ((EncodingJobInfo)state).VideoStream.BitRate, (((EncodingJobInfo)state).VideoStream == null) ? ((int?)null) : ((EncodingJobInfo)state).VideoStream.Width, (((EncodingJobInfo)state).VideoStream == null) ? ((int?)null) : ((EncodingJobInfo)state).VideoStream.Height, ((EncodingJobInfo)state).OutputVideoBitrate.Value, (((EncodingJobInfo)state).VideoStream == null) ? null : ((EncodingJobInfo)state).VideoStream.Codec, ((EncodingJobInfo)state).OutputVideoCodec, ((BaseEncodingJobOptions)videoStreamRequest).MaxWidth, ((BaseEncodingJobOptions)videoStreamRequest).MaxHeight);
				((BaseEncodingJobOptions)videoStreamRequest).MaxWidth = val2.MaxWidth;
				((BaseEncodingJobOptions)videoStreamRequest).MaxHeight = val2.MaxHeight;
			}
		}
		ApplyDeviceProfileSettings(state);
		string outputFileExtension = (string.IsNullOrWhiteSpace(((EncodingJobInfo)state).OutputContainer) ? GetOutputFileExtension(state) : ("." + ((EncodingJobInfo)state).OutputContainer));
		EncodingOptions encodingOptions = ApiEntryPoint.Instance.GetEncodingOptions();
		((EncodingJobInfo)state).OutputFilePath = GetOutputFilePath(state, encodingOptions, outputFileExtension);
		return state;
	}

	private void ApplyDeviceProfileSettings(StreamState state)
	{
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		Request.Headers.ToDictionary();
		if (!string.IsNullOrWhiteSpace(state.Request.DeviceProfileId))
		{
			state.DeviceProfile = DlnaManager.GetProfile(state.Request.DeviceProfileId);
		}
		DeviceProfile deviceProfile = state.DeviceProfile;
		if (deviceProfile != null)
		{
			string actualOutputAudioCodec = ((EncodingJobInfo)state).ActualOutputAudioCodec;
			string actualOutputVideoCodec = ((EncodingJobInfo)state).ActualOutputVideoCodec;
			ResponseProfile val = ((!((EncodingJobInfo)state).IsVideoRequest) ? deviceProfile.GetAudioMediaProfile(((EncodingJobInfo)state).OutputContainer, actualOutputAudioCodec, ((EncodingJobInfo)state).OutputAudioChannels, ((EncodingJobInfo)state).OutputAudioBitrate, ((EncodingJobInfo)state).OutputAudioSampleRate, ((EncodingJobInfo)state).OutputAudioBitDepth) : deviceProfile.GetVideoMediaProfile(((EncodingJobInfo)state).OutputContainer, actualOutputAudioCodec, actualOutputVideoCodec, ((EncodingJobInfo)state).OutputWidth, ((EncodingJobInfo)state).OutputHeight, ((EncodingJobInfo)state).TargetVideoBitDepth, ((EncodingJobInfo)state).OutputVideoBitrate, ((EncodingJobInfo)state).TargetVideoProfile, ((EncodingJobInfo)state).TargetVideoLevel, ((EncodingJobInfo)state).TargetFramerate ?? 0f, ((EncodingJobInfo)state).TargetPacketLength, ((EncodingJobInfo)state).TargetTimestamp, ((EncodingJobInfo)state).IsTargetAnamorphic, ((EncodingJobInfo)state).IsTargetInterlaced, ((EncodingJobInfo)state).TargetRefFrames, ((EncodingJobInfo)state).TargetVideoStreamCount, ((EncodingJobInfo)state).TargetAudioStreamCount, ((EncodingJobInfo)state).TargetVideoCodecTag, ((EncodingJobInfo)state).IsTargetAVC));
			if (val != null)
			{
				((EncodingJobInfo)state).MimeType = val.MimeType;
			}
		}
	}

	protected void AddDlnaHeaders(StreamState state, IDictionary<string, string> responseHeaders, bool isStaticallyStreamed)
	{
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		//IL_018f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		if (!state.Request.DlnaHeaders)
		{
			return;
		}
		DeviceProfile val = state.DeviceProfile;
		string header = GetHeader("transferMode.dlna.org");
		responseHeaders["transferMode.dlna.org"] = (string.IsNullOrEmpty(header) ? "Streaming" : header);
		responseHeaders["realTimeInfo.dlna.org"] = "DLNA.ORG_TLAG=*";
		if (string.Equals(GetHeader("getMediaInfo.sec"), "1", StringComparison.OrdinalIgnoreCase) && ((EncodingJobInfo)state).RunTimeTicks.HasValue)
		{
			double totalMilliseconds = TimeSpan.FromTicks(((EncodingJobInfo)state).RunTimeTicks.Value).TotalMilliseconds;
			responseHeaders["MediaInfo.sec"] = $"SEC_Duration={Convert.ToInt32(totalMilliseconds).ToString(CultureInfo.InvariantCulture)};";
		}
		if (((EncodingJobInfo)state).RunTimeTicks.HasValue && !isStaticallyStreamed && val != null)
		{
			AddTimeSeekResponseHeaders(state, responseHeaders);
		}
		if (val == null)
		{
			val = DlnaManager.GetDefaultProfile();
		}
		string actualOutputAudioCodec = ((EncodingJobInfo)state).ActualOutputAudioCodec;
		if (!((EncodingJobInfo)state).IsVideoRequest)
		{
			responseHeaders["contentFeatures.dlna.org"] = new ContentFeatureBuilder(val).BuildAudioHeader(((EncodingJobInfo)state).OutputContainer, actualOutputAudioCodec, ((EncodingJobInfo)state).OutputAudioBitrate, ((EncodingJobInfo)state).OutputAudioSampleRate, ((EncodingJobInfo)state).OutputAudioChannels, ((EncodingJobInfo)state).OutputAudioBitDepth, isStaticallyStreamed, ((EncodingJobInfo)state).RunTimeTicks ?? 0, state.Request.TranscodeSeekInfo);
		}
		else
		{
			string actualOutputVideoCodec = ((EncodingJobInfo)state).ActualOutputVideoCodec;
			responseHeaders["contentFeatures.dlna.org"] = new ContentFeatureBuilder(val).BuildVideoHeader(((EncodingJobInfo)state).OutputContainer, actualOutputVideoCodec, actualOutputAudioCodec, ((EncodingJobInfo)state).OutputWidth, ((EncodingJobInfo)state).OutputHeight, ((EncodingJobInfo)state).TargetVideoBitDepth, ((EncodingJobInfo)state).OutputVideoBitrate, ((EncodingJobInfo)state).TargetTimestamp, isStaticallyStreamed, ((EncodingJobInfo)state).RunTimeTicks ?? 0, ((EncodingJobInfo)state).TargetVideoProfile, ((EncodingJobInfo)state).TargetVideoLevel, ((EncodingJobInfo)state).TargetFramerate ?? 0f, ((EncodingJobInfo)state).TargetPacketLength, state.Request.TranscodeSeekInfo, ((EncodingJobInfo)state).IsTargetAnamorphic, ((EncodingJobInfo)state).IsTargetInterlaced, ((EncodingJobInfo)state).TargetRefFrames, ((EncodingJobInfo)state).TargetVideoStreamCount, ((EncodingJobInfo)state).TargetAudioStreamCount, ((EncodingJobInfo)state).TargetVideoCodecTag, ((EncodingJobInfo)state).IsTargetAVC).FirstOrDefault() ?? string.Empty;
		}
		foreach (KeyValuePair<string, string> responseHeader in responseHeaders)
		{
			Request.Response.AddHeader(responseHeader.Key, responseHeader.Value);
		}
	}

	private void AddTimeSeekResponseHeaders(StreamState state, IDictionary<string, string> responseHeaders)
	{
		string arg = TimeSpan.FromTicks(((EncodingJobInfo)state).RunTimeTicks.Value).TotalSeconds.ToString(UsCulture);
		string arg2 = TimeSpan.FromTicks(((BaseEncodingJobOptions)state.Request).StartTimeTicks ?? 0).TotalSeconds.ToString(UsCulture);
		responseHeaders["TimeSeekRange.dlna.org"] = string.Format("npt={0}-{1}/{1}", arg2, arg);
		responseHeaders["X-AvailableSeekRange"] = $"1 npt={arg2}-{arg}";
	}
}
