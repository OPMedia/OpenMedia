using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;

namespace Emby.Server.MediaEncoding.Encoder;

public abstract class BaseEncoder
{
	protected readonly MediaEncoder MediaEncoder;

	protected readonly ILogger Logger;

	protected readonly IServerConfigurationManager ConfigurationManager;

	protected readonly IFileSystem FileSystem;

	protected readonly IIsoManager IsoManager;

	protected readonly ILibraryManager LibraryManager;

	protected readonly ISessionManager SessionManager;

	protected readonly ISubtitleEncoder SubtitleEncoder;

	protected readonly IMediaSourceManager MediaSourceManager;

	protected IProcessFactory ProcessFactory;

	protected readonly IAssemblyInfo AssemblyInfo;

	protected readonly IJsonSerializer JsonSerializer;

	protected readonly IApplicationHost AppHost;

	protected readonly CultureInfo UsCulture = new CultureInfo("en-US");

	protected EncodingHelper EncodingHelper;

	protected abstract bool IsVideoEncoder { get; }

	protected BaseEncoder(MediaEncoder mediaEncoder, ILogger logger, IServerConfigurationManager configurationManager, IFileSystem fileSystem, IIsoManager isoManager, ILibraryManager libraryManager, ISessionManager sessionManager, ISubtitleEncoder subtitleEncoder, IMediaSourceManager mediaSourceManager, IProcessFactory processFactory, IAssemblyInfo assemblyInfo, IJsonSerializer json, IApplicationHost applicationHost)
	{
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Expected Obj, but got Unknown
		MediaEncoder = mediaEncoder;
		Logger = logger;
		ConfigurationManager = configurationManager;
		FileSystem = fileSystem;
		IsoManager = isoManager;
		LibraryManager = libraryManager;
		SessionManager = sessionManager;
		SubtitleEncoder = subtitleEncoder;
		MediaSourceManager = mediaSourceManager;
		ProcessFactory = processFactory;
		AssemblyInfo = assemblyInfo;
		JsonSerializer = json;
		AppHost = applicationHost;
		EncodingHelper = new EncodingHelper((IMediaEncoder)(object)MediaEncoder, FileSystem, SubtitleEncoder, (IApplicationPaths)(object)configurationManager.ApplicationPaths, AssemblyInfo);
	}

	public async Task<EncodingJobInfo> Start(EncodingJobOptions options, EncodingOptions encodingOptions, IProgress<double> progress, CancellationToken cancellationToken)
	{
		encodingOptions = CloneOptions(encodingOptions);
		EncodingJobInfo encodingJob = await new EncodingJobFactory(Logger, LibraryManager, MediaSourceManager, (IConfigurationManager)(object)ConfigurationManager, (IMediaEncoder)(object)MediaEncoder).CreateJob(options, EncodingHelper, IsVideoEncoder, progress, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		encodingJob.OutputFilePath = GetOutputFilePath(encodingJob);
		FileSystem.CreateDirectory(FileSystem.GetDirectoryName(encodingJob.OutputFilePath));
		encodingJob.ReadInputAtNativeFramerate = options.ReadInputAtNativeFramerate;
		await AcquireResources(encodingJob, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		string commandLineArguments = GetCommandLineArguments(encodingJob, encodingOptions);
		IProcess process = ProcessFactory.Create(new ProcessOptions
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
		string workingDirectory = GetWorkingDirectory(options);
		if (!string.IsNullOrWhiteSpace(workingDirectory))
		{
			process.StartInfo.WorkingDirectory = workingDirectory;
		}
		OnTranscodeBeginning(encodingJob);
		string text = process.StartInfo.FileName + " " + process.StartInfo.Arguments;
		Logger.Info(text, Array.Empty<object>());
		try
		{
			process.Start();
		}
		catch (Exception ex)
		{
			Logger.ErrorException("Error starting ffmpeg", ex, Array.Empty<object>());
			OnTranscodeFailedToStart(encodingJob.OutputFilePath, encodingJob);
			throw;
		}
		if (encodingJob.LogFileStream == null)
		{
			string text2 = "ffmpeg-transcode";
			if (encodingJob.IsVideoRequest && string.Equals(encodingJob.OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase) && string.Equals(encodingJob.OutputAudioCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				text2 = "ffmpeg-directstream";
			}
			else if (encodingJob.IsVideoRequest && string.Equals(encodingJob.OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
			{
				text2 = "ffmpeg-remux";
			}
			string text3 = Path.Combine(((IApplicationPaths)ConfigurationManager.ApplicationPaths).LogDirectoryPath, string.Concat(text2, "-", Guid.NewGuid(), ".txt"));
			FileSystem.CreateDirectory(FileSystem.GetDirectoryName(text3));
			encodingJob.LogFileStream = FileSystem.GetFileStream(text3, (FileOpenMode)2, (FileAccessMode)2, (FileShareMode)1, true);
			await LogInitialInfo(encodingJob, text, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		new JobLogger(Logger).StartStreamingLog(encodingJob, process.StandardError.BaseStream, encodingJob.LogFileStream);
		bool hasProcessExited = false;
		EventHandler onExited = (object sender, EventArgs args) =>
		{
			hasProcessExited = true;
		};
		process.Exited += onExited;
		bool processStarted = false;
		while (!hasProcessExited)
		{
			processStarted = ValidateProcessStarted(encodingJob, encodingJob.OutputFilePath);
			if (processStarted)
			{
				break;
			}
			await Task.Delay(50, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		process.Exited -= onExited;
		if (hasProcessExited && !processStarted)
		{
			try
			{
				((IDisposable)process).Dispose();
			}
			catch
			{
			}
			if (encodingOptions.EnableHardwareEncoding || !string.IsNullOrWhiteSpace(EncodingHelper.GetHardwareAcceleratedVideoDecoder(encodingJob.VideoType, encodingJob.VideoStream, encodingOptions)))
			{
				encodingOptions.EnableHardwareEncoding = false;
				encodingOptions.HardwareDecodingCodecs = Array.Empty<string>();
				return await Start(options, encodingOptions, progress, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			throw new Exception("Error starting ffmpeg");
		}
		Logger.Info("ffmpeg successfully started", Array.Empty<object>());
		cancellationToken.Register(() =>
		{
			Cancel(process, encodingJob);
		});
		while (!FileSystem.FileExists(encodingJob.OutputFilePath) && !encodingJob.HasExited)
		{
			await Task.Delay(100, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		process.Exited += (object sender, EventArgs args) =>
		{
			OnFfMpegProcessExited(process, encodingJob);
		};
		return encodingJob;
	}

	private Task LogInitialInfo(EncodingJobInfo state, string commandLineLogMessage, CancellationToken cancellationToken)
	{
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
		stringBuilder.AppendLine($"Application version: {AppHost.ApplicationVersion}");
		stringBuilder.AppendLine(string.Empty);
		stringBuilder.AppendLine(JsonSerializer.SerializeToString((object)state.MediaSource));
		stringBuilder.AppendLine(string.Empty);
		if ((int)state.BaseRequest.Context == 0)
		{
			User user = state.User;
			if (user != null)
			{
				stringBuilder.AppendLine($"User policy for {((BaseItem)user).Name}: EnablePlaybackRemuxing: {user.Policy.EnablePlaybackRemuxing} EnableVideoPlaybackTranscoding: {user.Policy.EnableVideoPlaybackTranscoding} EnableAudioPlaybackTranscoding: {user.Policy.EnableAudioPlaybackTranscoding}");
				stringBuilder.AppendLine(string.Empty);
			}
		}
		stringBuilder.AppendLine(commandLineLogMessage);
		byte[] bytes = Encoding.UTF8.GetBytes(stringBuilder.ToString());
		return state.LogFileStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
	}

	protected virtual bool ValidateProcessStarted(EncodingJobInfo state, string outputPath)
	{
		FileSystemMetadata fileInfo = FileSystem.GetFileInfo(outputPath);
		if (fileInfo.Exists)
		{
			return fileInfo.Length > 0;
		}
		return false;
	}

	private void Cancel(IProcess process, EncodingJobInfo job)
	{
		Logger.Info("Killing ffmpeg process for {0}", new object[1] { job.OutputFilePath });
		process.StandardInput.WriteLine("q");
		job.IsCancelled = true;
	}

	private void OnFfMpegProcessExited(IProcess process, EncodingJobInfo job)
	{
		job.HasExited = true;
		Logger.Debug("Disposing stream resources", Array.Empty<object>());
		job.Dispose();
		bool flag = false;
		try
		{
			int exitCode = process.ExitCode;
			Logger.Info("FFMpeg exited with code {0}", new object[1] { exitCode });
			flag = exitCode == 0;
		}
		catch
		{
			Logger.Error("FFMpeg exited with an error.", Array.Empty<object>());
		}
		if (flag && !job.IsCancelled)
		{
			job.TaskCompletionSource.TrySetResult(result: true);
			return;
		}
		if (job.IsCancelled)
		{
			try
			{
				DeleteFiles(job);
			}
			catch
			{
			}
			try
			{
				job.TaskCompletionSource.TrySetException(new OperationCanceledException());
				return;
			}
			catch
			{
				return;
			}
		}
		try
		{
			DeleteFiles(job);
		}
		catch
		{
		}
		try
		{
			job.TaskCompletionSource.TrySetException(new Exception("Encoding failed"));
		}
		catch
		{
		}
	}

	protected virtual void DeleteFiles(EncodingJobInfo job)
	{
		FileSystem.DeleteFile(job.OutputFilePath);
	}

	private void OnTranscodeBeginning(EncodingJobInfo job)
	{
		job.ReportTranscodingProgress((TimeSpan?)null, 0f, (double?)null, 0L, (int?)null);
	}

	private void OnTranscodeFailedToStart(string path, EncodingJobInfo job)
	{
		if (!string.IsNullOrWhiteSpace(job.BaseRequest.DeviceId))
		{
			SessionManager.ClearTranscodingInfo(job.BaseRequest.DeviceId);
		}
	}

	protected virtual string GetWorkingDirectory(EncodingJobOptions options)
	{
		return null;
	}

	protected abstract string GetCommandLineArguments(EncodingJobInfo job, EncodingOptions encodingOptions);

	private string GetOutputFilePath(EncodingJobInfo state)
	{
		BaseEncodingJobOptions baseRequest = state.BaseRequest;
		EncodingJobOptions val = (EncodingJobOptions)(object)((baseRequest is EncodingJobOptions) ? baseRequest : null);
		string text = ((val == null) ? null : val.TempDirectory);
		string path = (string.IsNullOrWhiteSpace(text) ? ConfigurationManager.ApplicationPaths.GetTranscodingTempPath() : text);
		string path2 = string.Concat(arg1: (GetOutputFileExtension(state) ?? string.Empty).ToLower(), arg0: state.Id);
		return Path.Combine(path, path2);
	}

	protected virtual string GetOutputFileExtension(EncodingJobInfo state)
	{
		if (!string.IsNullOrWhiteSpace(state.BaseRequest.Container))
		{
			return "." + state.BaseRequest.Container;
		}
		return null;
	}

	private async Task AcquireResources(EncodingJobInfo state, CancellationToken cancellationToken)
	{
		if ((int)state.VideoType == 1 && state.IsoType.HasValue && IsoManager.CanMount(state.MediaPath))
		{
			state.IsoMount = await IsoManager.Mount(state.MediaPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (state.MediaSource.RequiresOpening && string.IsNullOrWhiteSpace(state.BaseRequest.LiveStreamId))
		{
			LiveStreamResponse val = await MediaSourceManager.OpenLiveStream(new LiveStreamRequest
			{
				OpenToken = state.MediaSource.OpenToken
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			EncodingHelper.AttachMediaSourceInfo(state, val.MediaSource, (string)null);
			if (state.IsVideoRequest)
			{
				EncodingHelper.TryStreamCopy(state);
			}
		}
		if (state.MediaSource.BufferMs.HasValue)
		{
			await Task.Delay(state.MediaSource.BufferMs.Value, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private EncodingOptions CloneOptions(EncodingOptions options)
	{
		return JsonSerializer.DeserializeFromString<EncodingOptions>(JsonSerializer.SerializeToString((object)options));
	}
}
