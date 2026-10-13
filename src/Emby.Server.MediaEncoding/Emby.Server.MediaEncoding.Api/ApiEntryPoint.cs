using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.Threading;
using ITimer = MediaBrowser.Model.Threading.ITimer;

namespace Emby.Server.MediaEncoding.Api;

public class ApiEntryPoint : IServerEntryPoint, IDisposable
{
	public static ApiEntryPoint Instance;

	private readonly IServerConfigurationManager _config;

	private readonly ISessionManager _sessionManager;

	private readonly IFileSystem _fileSystem;

	private readonly IMediaSourceManager _mediaSourceManager;

	public readonly ITimerFactory TimerFactory;

	public readonly IProcessFactory ProcessFactory;

	private readonly List<TranscodingJob> _activeTranscodingJobs = new List<TranscodingJob>();

	private readonly Dictionary<string, SemaphoreSlim> _transcodingLocks = new Dictionary<string, SemaphoreSlim>();

	internal ILogger Logger { get; private set; }

	internal IHttpResultFactory ResultFactory { get; private set; }

	public ApiEntryPoint(ILogger logger, ISessionManager sessionManager, IServerConfigurationManager config, IFileSystem fileSystem, IMediaSourceManager mediaSourceManager, ITimerFactory timerFactory, IProcessFactory processFactory, IHttpResultFactory resultFactory)
	{
		Logger = logger;
		_sessionManager = sessionManager;
		_config = config;
		_fileSystem = fileSystem;
		_mediaSourceManager = mediaSourceManager;
		TimerFactory = timerFactory;
		ProcessFactory = processFactory;
		ResultFactory = resultFactory;
		Instance = this;
		_sessionManager.PlaybackProgress += _sessionManager_PlaybackProgress;
		_sessionManager.PlaybackStart += _sessionManager_PlaybackStart;
	}

	public string GetTempTranscodingPath(string playSessionId)
	{
		TranscodingJob transcodingJob = GetTranscodingJob(playSessionId);
		if (transcodingJob != null)
		{
			return _fileSystem.GetDirectoryName(transcodingJob.Path);
		}
		return _config.ApplicationPaths.GetTranscodingTempPath();
	}

	public SemaphoreSlim GetTranscodingLock(string outputPath)
	{
		lock (_transcodingLocks)
		{
			if (!_transcodingLocks.TryGetValue(outputPath, out var value))
			{
				value = new SemaphoreSlim(1, 1);
				_transcodingLocks[outputPath] = value;
			}
			return value;
		}
	}

	private void _sessionManager_PlaybackStart(object sender, PlaybackProgressEventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(e.PlaySessionId))
		{
			PingTranscodingJob(e.PlaySessionId, e.IsPaused, "PlaybackStart");
		}
	}

	private void _sessionManager_PlaybackProgress(object sender, PlaybackProgressEventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(e.PlaySessionId) && !e.IsAutomated)
		{
			PingTranscodingJob(e.PlaySessionId, e.IsPaused, "PlaybackProgress");
		}
	}

	public void Run()
	{
		try
		{
			DeleteEncodedMediaCache();
		}
		catch (FileNotFoundException)
		{
		}
		catch (IOException)
		{
		}
		catch (Exception ex3)
		{
			Logger.ErrorException("Error deleting encoded media cache", ex3, Array.Empty<object>());
		}
	}

	public EncodingOptions GetEncodingOptions()
	{
		return ConfigurationManagerExtensions.GetConfiguration<EncodingOptions>((IConfigurationManager)(object)_config, "encoding");
	}

	private void DeleteEncodedMediaCache()
	{
		string transcodingTempPath = _config.ApplicationPaths.TranscodingTempPath;
		foreach (string item in _fileSystem.GetFilePaths(transcodingTempPath, true).ToList())
		{
			_fileSystem.DeleteFile(item);
		}
	}

	public void Dispose()
	{
		Dispose(dispose: true);
	}

	protected virtual void Dispose(bool dispose)
	{
		List<TranscodingJob> list = _activeTranscodingJobs.ToList();
		int count = list.Count;
		foreach (TranscodingJob item in list)
		{
			Task task = KillTranscodingJob(item, closeLiveStream: false, delete: true);
			Task.WaitAll(task);
		}
		if (count > 0)
		{
			Task task2 = Task.Delay(1000);
			Task.WaitAll(task2);
		}
	}

	public TranscodingJob OnTranscodeBeginning(string path, string playSessionId, string liveStreamId, Guid transcodingJobId, TranscodingJobType type, IProcess process, string deviceId, StreamState state, CancellationTokenSource cancellationTokenSource)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		lock (_activeTranscodingJobs)
		{
			int timerDuration = 10000;
			if ((int)type != 0)
			{
				timerDuration = 60000;
			}
			TranscodingJob transcodingJob = new TranscodingJob(Logger, TimerFactory, timerDuration)
			{
				Type = type,
				Path = path,
				Process = process,
				ActiveRequestCount = 1,
				DeviceId = deviceId,
				CancellationTokenSource = cancellationTokenSource,
				Id = transcodingJobId,
				PlaySessionId = playSessionId,
				LiveStreamId = liveStreamId,
				MediaSource = ((EncodingJobInfo)state).MediaSource
			};
			_activeTranscodingJobs.Add(transcodingJob);
			ReportTranscodingProgress(transcodingJob, state, null, 0f, null, 0L, null);
			return transcodingJob;
		}
	}

	public void ReportTranscodingProgress(TranscodingJob job, StreamState state, TimeSpan? transcodingPosition, float framerate, double? percentComplete, long bytesTranscoded, int? bitRate)
	{
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0154: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Expected Obj, but got Unknown
		long? transcodingPositionTicks = (transcodingPosition.HasValue ? new long?(transcodingPosition.Value.Ticks) : ((long?)null));
		if (job != null)
		{
			job.Framerate = ((framerate <= 0f) ? ((float?)null) : new float?(framerate));
			job.CompletionPercentage = percentComplete;
			job.TranscodingPositionTicks = transcodingPositionTicks;
			job.BytesTranscoded = ((bytesTranscoded <= 0) ? ((long?)null) : new long?(bytesTranscoded));
			job.BitRate = bitRate;
		}
		string deviceId = ((BaseEncodingJobOptions)state.Request).DeviceId;
		if (!string.IsNullOrWhiteSpace(deviceId))
		{
			string actualOutputAudioCodec = ((EncodingJobInfo)state).ActualOutputAudioCodec;
			string actualOutputVideoCodec = ((EncodingJobInfo)state).ActualOutputVideoCodec;
			_sessionManager.ReportTranscodingInfo(deviceId, new TranscodingInfo
			{
				Bitrate = (bitRate ?? ((EncodingJobInfo)state).TotalOutputBitrate),
				AudioCodec = actualOutputAudioCodec,
				VideoCodec = actualOutputVideoCodec,
				Container = ((EncodingJobInfo)state).OutputContainer,
				Framerate = ((framerate <= 0f) ? ((float?)null) : new float?(framerate)),
				CompletionPercentage = percentComplete,
				Width = ((EncodingJobInfo)state).OutputWidth,
				Height = ((EncodingJobInfo)state).OutputHeight,
				AudioChannels = ((EncodingJobInfo)state).OutputAudioChannels,
				IsAudioDirect = string.Equals(((EncodingJobInfo)state).OutputAudioCodec, "copy", StringComparison.OrdinalIgnoreCase),
				IsVideoDirect = string.Equals(((EncodingJobInfo)state).OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase),
				TranscodeReasons = ((EncodingJobInfo)state).TranscodeReasons
			});
		}
	}

	public void OnTranscodeFailedToStart(string path, TranscodingJobType type, StreamState state)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		lock (_activeTranscodingJobs)
		{
			TranscodingJob transcodingJob = _activeTranscodingJobs.FirstOrDefault((TranscodingJob j) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				return j.Type == type && string.Equals(j.Path, path, StringComparison.OrdinalIgnoreCase);
			});
			if (transcodingJob != null)
			{
				_activeTranscodingJobs.Remove(transcodingJob);
			}
		}
		lock (_transcodingLocks)
		{
			_transcodingLocks.Remove(path);
		}
		if (!string.IsNullOrWhiteSpace(((BaseEncodingJobOptions)state.Request).DeviceId))
		{
			_sessionManager.ClearTranscodingInfo(((BaseEncodingJobOptions)state.Request).DeviceId);
		}
	}

	public bool HasActiveTranscodingJob(string path, TranscodingJobType type)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		return GetTranscodingJob(path, type) != null;
	}

	public TranscodingJob GetTranscodingJob(string path, TranscodingJobType type)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		lock (_activeTranscodingJobs)
		{
			return _activeTranscodingJobs.FirstOrDefault((TranscodingJob j) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				return j.Type == type && string.Equals(j.Path, path, StringComparison.OrdinalIgnoreCase);
			});
		}
	}

	public TranscodingJob GetTranscodingJob(string playSessionId)
	{
		lock (_activeTranscodingJobs)
		{
			return _activeTranscodingJobs.FirstOrDefault((TranscodingJob j) => string.Equals(j.PlaySessionId, playSessionId, StringComparison.OrdinalIgnoreCase));
		}
	}

	public TranscodingJob OnTranscodeBeginRequest(string path, TranscodingJobType type)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		lock (_activeTranscodingJobs)
		{
			TranscodingJob transcodingJob = _activeTranscodingJobs.FirstOrDefault((TranscodingJob j) =>
			{
				//IL_0001: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				return j.Type == type && string.Equals(j.Path, path, StringComparison.OrdinalIgnoreCase);
			});
			if (transcodingJob == null)
			{
				return null;
			}
			OnTranscodeBeginRequest(transcodingJob);
			return transcodingJob;
		}
	}

	public void OnTranscodeBeginRequest(TranscodingJob job)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		job.ActiveRequestCount++;
		if (string.IsNullOrWhiteSpace(job.PlaySessionId) || (int)job.Type == 0)
		{
			job.StopKillTimer();
		}
	}

	public void OnTranscodeEndRequest(TranscodingJob job)
	{
		job.ActiveRequestCount--;
		if (job.ActiveRequestCount <= 0)
		{
			PingTimer(job, isProgressCheckIn: false, "TranscodeEndRequest");
		}
	}

	internal void PingTranscodingJob(string playSessionId, bool? isUserPaused, string reason)
	{
		if (string.IsNullOrEmpty(playSessionId))
		{
			throw new ArgumentNullException("playSessionId");
		}
		List<TranscodingJob> list;
		lock (_activeTranscodingJobs)
		{
			list = _activeTranscodingJobs.Where((TranscodingJob j) => string.Equals(playSessionId, j.PlaySessionId, StringComparison.OrdinalIgnoreCase)).ToList();
		}
		foreach (TranscodingJob item in list)
		{
			if (isUserPaused.HasValue)
			{
				item.IsUserPaused = isUserPaused.Value;
			}
			PingTimer(item, isProgressCheckIn: true, reason);
		}
	}

	private void PingTimer(TranscodingJob job, bool isProgressCheckIn, string reason)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		if (job.HasExited)
		{
			job.StopKillTimer();
			return;
		}
		job.LastPingDate = DateTime.UtcNow;
		if ((int)job.Type != 0 || !isProgressCheckIn)
		{
			job.StartKillTimer(OnTranscodeKillTimerStopped, reason);
		}
		else
		{
			job.ChangeKillTimerIfStarted(reason);
		}
	}

	private async void OnTranscodeKillTimerStopped(object state)
	{
		TranscodingJob transcodingJob = (TranscodingJob)state;
		if (!transcodingJob.HasExited && (int)transcodingJob.Type != 0 && transcodingJob.TimerDuration != -1 && (DateTime.UtcNow - transcodingJob.LastPingDate).TotalMilliseconds < (double)transcodingJob.TimerDuration)
		{
			transcodingJob.StartKillTimer(OnTranscodeKillTimerStopped, "ShortTimeSinceLastPing");
			return;
		}
		Logger.Info("Transcoding kill timer stopped for JobId {0} PlaySessionId {1}. Killing transcoding", new object[2] { transcodingJob.Id, transcodingJob.PlaySessionId });
		await KillTranscodingJob(transcodingJob, closeLiveStream: true, delete: true).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal Task KillTranscodingJobs(string deviceId, string playSessionId, bool deleteFiles)
	{
		return KillTranscodingJobs((TranscodingJob j) => (!string.IsNullOrWhiteSpace(playSessionId)) ? string.Equals(playSessionId, j.PlaySessionId, StringComparison.OrdinalIgnoreCase) : string.Equals(deviceId, j.DeviceId, StringComparison.OrdinalIgnoreCase), deleteFiles);
	}

	private async Task KillTranscodingJobs(Func<TranscodingJob, bool> killJob, bool deleteFiles)
	{
		List<TranscodingJob> list = new List<TranscodingJob>();
		lock (_activeTranscodingJobs)
		{
			list.AddRange(_activeTranscodingJobs.Where(killJob));
		}
		if (list.Count == 0)
		{
			return;
		}
		foreach (TranscodingJob item in list)
		{
			await KillTranscodingJob(item, closeLiveStream: false, deleteFiles).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private async Task KillTranscodingJob(TranscodingJob job, bool closeLiveStream, bool delete)
	{
		job.DisposeKillTimer();
		Logger.Debug("KillTranscodingJob - JobId {0} PlaySessionId {1}. Killing transcoding", new object[2] { job.Id, job.PlaySessionId });
		lock (_activeTranscodingJobs)
		{
			_activeTranscodingJobs.Remove(job);
			if (!job.CancellationTokenSource.IsCancellationRequested)
			{
				job.CancellationTokenSource.Cancel();
			}
		}
		lock (_transcodingLocks)
		{
			_transcodingLocks.Remove(job.Path);
		}
		await job.ProcessLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			IProcess process = job.Process;
			if (!job.HasExited)
			{
				try
				{
					Logger.Info("Stopping ffmpeg process with q command for {0}", new object[1] { job.Path });
					process.StandardInput.WriteLine("q");
					if (!(await process.WaitForExitAsync(5000).ConfigureAwait(continueOnCapturedContext: false)))
					{
						Logger.Info("Killing ffmpeg process for {0}", new object[1] { job.Path });
						process.Kill();
					}
				}
				catch (Exception ex)
				{
					Logger.ErrorException("Error killing transcoding job for {0}", ex, new object[1] { job.Path });
				}
			}
		}
		catch
		{
			job.ProcessLock.Release();
		}
		if (delete)
		{
			DeletePartialStreamFiles(job.Path, job.Type, 0, 1500);
		}
		if (closeLiveStream && !string.IsNullOrWhiteSpace(job.LiveStreamId))
		{
			try
			{
				await _mediaSourceManager.CloseLiveStream(job.LiveStreamId).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex2)
			{
				Logger.ErrorException("Error closing live stream for {0}", ex2, new object[1] { job.Path });
			}
		}
	}

	private async Task DeletePartialStreamFiles(string path, TranscodingJobType jobType, int retryCount, int delayMs)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		if (retryCount >= 10)
		{
			return;
		}
		Logger.Info("Deleting partial stream file(s) {0}", new object[1] { path });
		await Task.Delay(delayMs).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if ((int)jobType == 0)
			{
				DeleteProgressivePartialStreamFiles(path);
			}
			else
			{
				DeleteHlsPartialStreamFiles(path);
			}
		}
		catch (FileNotFoundException)
		{
		}
		catch (IOException ex2)
		{
			Logger.Debug("Unable to partial stream file(s) {0} due to {1}, will try again.", new object[2] { path, ex2.Message });
			await DeletePartialStreamFiles(path, jobType, retryCount + 1, 500).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex3)
		{
			Logger.Debug("Unable to partial stream file(s) {0} due to {1}, will try again.", new object[2] { path, ex3.Message });
			await DeletePartialStreamFiles(path, jobType, retryCount + 1, 500).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private void DeleteProgressivePartialStreamFiles(string outputFilePath)
	{
		_fileSystem.DeleteFile(outputFilePath);
	}

	private void DeleteHlsPartialStreamFiles(string outputFilePath)
	{
		string directoryName = _fileSystem.GetDirectoryName(outputFilePath);
		string name = Path.GetFileNameWithoutExtension(outputFilePath);
		List<string> list = (from f in _fileSystem.GetFilePaths(directoryName, false)
			where f.IndexOf(name, StringComparison.OrdinalIgnoreCase) != -1
			select f).ToList();
		Exception ex = null;
		foreach (string item in list)
		{
			try
			{
				_fileSystem.DeleteFile(item);
			}
			catch (FileNotFoundException)
			{
			}
			catch (IOException ex3)
			{
				ex = ex3;
			}
		}
		if (ex != null)
		{
			throw ex;
		}
	}
}
