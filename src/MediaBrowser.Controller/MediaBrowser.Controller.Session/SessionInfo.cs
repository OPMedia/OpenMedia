using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.Threading;

namespace MediaBrowser.Controller.Session;

public class SessionInfo : IDisposable
{
	private ISessionManager _sessionManager;

	private readonly ILogger _logger;

	private readonly object _progressLock = new object();

	private ITimer _progressTimer;

	private PlaybackProgressInfo _lastProgressInfo;

	private const long ProgressIncrement = 10000000L;

	private bool _disposed;

	public PlayerStateInfo PlayState { get; set; }

	public SessionUserInfo[] AdditionalUsers { get; set; }

	[IgnoreDataMember]
	public QueueItem[] NowPlayingQueue { get; set; }

	[IgnoreDataMember]
	public bool HasCustomDeviceName { get; set; }

	public ClientCapabilities Capabilities { get; set; }

	public string RemoteEndPoint { get; set; }

	public string[] PlayableMediaTypes
	{
		get
		{
			ClientCapabilities capabilities = Capabilities;
			if (capabilities == null)
			{
				return Array.Empty<string>();
			}
			return capabilities.PlayableMediaTypes;
		}
	}

	public string PlaylistItemId { get; set; }

	public string Id { get; set; }

	public string ServerId { get; set; }

	public Guid UserId { get; set; }

	public string UserName { get; set; }

	public string UserPrimaryImageTag { get; set; }

	public string Client { get; set; }

	public DateTime LastActivityDate { get; set; }

	[IgnoreDataMember]
	public DateTime LastPlaybackCheckIn { get; set; }

	public string DeviceName { get; set; }

	public string DeviceType { get; set; }

	public BaseItemDto NowPlayingItem { get; set; }

	[IgnoreDataMember]
	public BaseItem FullNowPlayingItem { get; set; }

	public string DeviceId { get; set; }

	public string ApplicationVersion { get; set; }

	[IgnoreDataMember]
	public ISessionController[] SessionControllers { get; set; }

	public string AppIconUrl
	{
		get
		{
			ClientCapabilities capabilities = Capabilities;
			if (capabilities != null)
			{
				return capabilities.IconUrl;
			}
			return null;
		}
	}

	public string[] SupportedCommands
	{
		get
		{
			ClientCapabilities capabilities = Capabilities;
			if (capabilities == null)
			{
				return Array.Empty<string>();
			}
			return capabilities.SupportedCommands;
		}
	}

	public TranscodingInfo TranscodingInfo { get; set; }

	[IgnoreDataMember]
	public bool IsActive
	{
		get
		{
			ISessionController[] sessionControllers = SessionControllers;
			ISessionController[] array = sessionControllers;
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i].IsSessionActive)
				{
					return true;
				}
			}
			if (sessionControllers.Length != 0)
			{
				return false;
			}
			return true;
		}
	}

	public bool SupportsRemoteControl
	{
		get
		{
			ClientCapabilities capabilities = Capabilities;
			if (capabilities == null || !capabilities.SupportsMediaControl)
			{
				return false;
			}
			ISessionController[] sessionControllers = SessionControllers;
			for (int i = 0; i < sessionControllers.Length; i++)
			{
				if (sessionControllers[i].SupportsMediaControl)
				{
					return true;
				}
			}
			return false;
		}
	}

	public SessionInfo(ISessionManager sessionManager, ILogger logger)
	{
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Expected Obj, but got Unknown
		_sessionManager = sessionManager;
		_logger = logger;
		AdditionalUsers = new SessionUserInfo[0];
		PlayState = new PlayerStateInfo();
		SessionControllers = new ISessionController[0];
	}

	public SessionInfo()
		: this(null, null)
	{
	}

	public Tuple<ISessionController, bool> EnsureController<T>(Func<SessionInfo, ISessionController> factory)
	{
		List<ISessionController> list = SessionControllers.ToList();
		foreach (ISessionController item in list)
		{
			if (item is T)
			{
				return new Tuple<ISessionController, bool>(item, item2: false);
			}
		}
		ISessionController sessionController = factory(this);
		_logger.Debug("Creating new {0}", new object[1] { sessionController.GetType().Name });
		list.Add(sessionController);
		SessionControllers = list.ToArray();
		return new Tuple<ISessionController, bool>(sessionController, item2: true);
	}

	public void AddController(ISessionController controller)
	{
		List<ISessionController> list = SessionControllers.ToList();
		list.Add(controller);
		SessionControllers = list.ToArray();
	}

	public bool ContainsUser(Guid userId)
	{
		if (UserId.Equals(userId))
		{
			return true;
		}
		SessionUserInfo[] additionalUsers = AdditionalUsers;
		foreach (SessionUserInfo val in additionalUsers)
		{
			if (userId.Equals(val.UserId))
			{
				return true;
			}
		}
		return false;
	}

	public void StartAutomaticProgress(ITimerFactory timerFactory, PlaybackProgressInfo progressInfo)
	{
		if (_disposed)
		{
			return;
		}
		lock (_progressLock)
		{
			_lastProgressInfo = progressInfo;
			if (_progressTimer == null)
			{
				_progressTimer = timerFactory.Create((Action<object>)OnProgressTimerCallback, (object)null, 1000, 1000);
			}
			else
			{
				_progressTimer.Change(1000, 1000);
			}
		}
	}

	private async void OnProgressTimerCallback(object state)
	{
		if (_disposed)
		{
			return;
		}
		PlaybackProgressInfo lastProgressInfo = _lastProgressInfo;
		if (lastProgressInfo == null || lastProgressInfo.IsPaused)
		{
			return;
		}
		long num = lastProgressInfo.PositionTicks ?? 0;
		if (num < 0)
		{
			num = 0L;
		}
		long num2 = num + 10000000;
		BaseItemDto item = lastProgressInfo.Item;
		long? num3 = ((item == null) ? ((long?)null) : item.RunTimeTicks);
		if (num3.HasValue && num2 >= num3.Value)
		{
			return;
		}
		lastProgressInfo.PositionTicks = num2;
		try
		{
			await _sessionManager.OnPlaybackProgress(lastProgressInfo, isAutomated: true).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			_logger.ErrorException("Error reporting playback progress", ex, Array.Empty<object>());
		}
	}

	public void StopAutomaticProgress()
	{
		lock (_progressLock)
		{
			if (_progressTimer != null)
			{
				((IDisposable)_progressTimer).Dispose();
				_progressTimer = null;
			}
			_lastProgressInfo = null;
		}
	}

	public void Dispose()
	{
		_disposed = true;
		StopAutomaticProgress();
		List<ISessionController> list = SessionControllers.ToList();
		SessionControllers = new ISessionController[0];
		foreach (ISessionController item in list)
		{
			if (item is IDisposable disposable)
			{
				_logger.Debug("Disposing session controller {0}", new object[1] { disposable.GetType().Name });
				try
				{
					disposable.Dispose();
				}
				catch (Exception ex)
				{
					_logger.ErrorException("Error disposing session controller", ex, Array.Empty<object>());
				}
			}
		}
		_sessionManager = null;
	}
}
