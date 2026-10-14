using System;
using System.Runtime.CompilerServices;
using System.Threading;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Threading;
using ITimer = MediaBrowser.Model.Threading.ITimer;

namespace Emby.Server.MediaEncoding.Api;

public class TranscodingJob
{
	private readonly ITimerFactory _timerFactory;

	public SemaphoreSlim ProcessLock = new SemaphoreSlim(1, 1);

	private readonly object _timerLock = new object();

	public string PlaySessionId { get; set; }

	public string LiveStreamId { get; set; }

	public bool IsLiveOutput { get; set; }

	public MediaSourceInfo MediaSource { get; set; }

	public string Path { get; set; }

	public TranscodingJobType Type
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public IProcess Process { get; set; }

	public ILogger Logger { get; private set; }

	public int ActiveRequestCount { get; set; }

	private ITimer KillTimer { get; set; }

	public string DeviceId { get; set; }

	public CancellationTokenSource CancellationTokenSource { get; set; }

	public bool HasExited { get; set; }

	public bool IsUserPaused { get; set; }

	public Guid Id { get; set; }

	public float? Framerate { get; set; }

	public double? CompletionPercentage { get; set; }

	public long? BytesDownloaded { get; set; }

	public long? BytesTranscoded { get; set; }

	public int? BitRate { get; set; }

	public long? TranscodingPositionTicks { get; set; }

	public long? DownloadPositionTicks { get; set; }

	public DateTime LastPingDate { get; set; }

	public int TimerDuration { get; set; }

	public TranscodingJob(ILogger logger, ITimerFactory timerFactory, int timerDuration)
	{
		Logger = logger;
		TimerDuration = timerDuration;
		_timerFactory = timerFactory;
	}

	public void StopKillTimer()
	{
		lock (_timerLock)
		{
			if (KillTimer != null)
			{
				KillTimer.Change(-1, -1);
			}
		}
	}

	public void DisposeKillTimer()
	{
		lock (_timerLock)
		{
			if (KillTimer != null)
			{
				((IDisposable)KillTimer).Dispose();
				KillTimer = null;
			}
		}
	}

	public void StartKillTimer(Action<object> timerCallback, string reason)
	{
		if (HasExited)
		{
			return;
		}
		lock (_timerLock)
		{
			int timerDuration = TimerDuration;
			if (KillTimer == null)
			{
				KillTimer = _timerFactory.Create(timerCallback, (object)this, timerDuration, -1);
			}
			else
			{
				KillTimer.Change(timerDuration, -1);
			}
		}
	}

	/// <summary>
	/// 进程已退出后的兜底清理定时器（BUG-25）：无视 HasExited 守卫启动一次性删除。
	/// 回调（OnTranscodeKillTimerStopped）对 HasExited=true 的 job 会直接走删除+注销。
	/// </summary>
	public void StartExitedCleanupTimer(Action<object> timerCallback, string reason)
	{
		lock (_timerLock)
		{
			int timerDuration = TimerDuration;
			if (KillTimer == null)
			{
				KillTimer = _timerFactory.Create(timerCallback, (object)this, timerDuration, -1);
			}
			else
			{
				KillTimer.Change(timerDuration, -1);
			}
		}
	}

	public void ChangeKillTimerIfStarted(string reason)
	{
		if (HasExited)
		{
			return;
		}
		lock (_timerLock)
		{
			if (KillTimer != null)
			{
				int timerDuration = TimerDuration;
				KillTimer.Change(timerDuration, -1);
			}
		}
	}
}
