using System;
using System.Globalization;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Session;

namespace Emby.Server.MediaEncoding.Api;

[Authenticated]
public class PlaystateService : BaseApiService
{
	private readonly IUserManager _userManager;

	private readonly IUserDataManager _userDataRepository;

	private readonly ILibraryManager _libraryManager;

	private readonly ISessionManager _sessionManager;

	private readonly ISessionContext _sessionContext;

	private readonly IAuthorizationContext _authContext;

	public PlaystateService(IUserManager userManager, IUserDataManager userDataRepository, ILibraryManager libraryManager, ISessionManager sessionManager, ISessionContext sessionContext, IAuthorizationContext authContext)
	{
		_userManager = userManager;
		_userDataRepository = userDataRepository;
		_libraryManager = libraryManager;
		_sessionManager = sessionManager;
		_sessionContext = sessionContext;
		_authContext = authContext;
	}

	public object Post(MarkPlayedItem request)
	{
		return MarkPlayed(request);
	}

	private UserItemDataDto MarkPlayed(MarkPlayedItem request)
	{
		User userById = _userManager.GetUserById(request.UserId);
		DateTime? datePlayed = null;
		if (!string.IsNullOrEmpty(request.DatePlayed))
		{
			datePlayed = DateTime.ParseExact(request.DatePlayed, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
		}
		SessionInfo session = GetSession(_sessionContext);
		BaseItem itemById = _libraryManager.GetItemById(request.Id);
		UserItemDataDto result = UpdatePlayedStatus(userById, itemById, wasPlayed: true, datePlayed);
		SessionUserInfo[] additionalUsers = session.AdditionalUsers;
		foreach (SessionUserInfo val in additionalUsers)
		{
			User userById2 = _userManager.GetUserById(val.UserId);
			UpdatePlayedStatus(userById2, itemById, wasPlayed: true, datePlayed);
		}
		return result;
	}

	private PlayMethod ValidatePlayMethod(PlayMethod method, string playSessionId)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		if ((int)method == 0 && (string.IsNullOrWhiteSpace(playSessionId) ? null : ApiEntryPoint.Instance.GetTranscodingJob(playSessionId)) == null)
		{
			return (PlayMethod)2;
		}
		return method;
	}

	public Task Post(OnPlaybackStart request)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		ReportPlaybackStart reportPlaybackStart = new ReportPlaybackStart();
		((PlaybackProgressInfo)reportPlaybackStart).CanSeek = request.CanSeek;
		((PlaybackProgressInfo)reportPlaybackStart).ItemId = request.Id;
		((PlaybackProgressInfo)reportPlaybackStart).MediaSourceId = request.MediaSourceId;
		((PlaybackProgressInfo)reportPlaybackStart).AudioStreamIndex = request.AudioStreamIndex;
		((PlaybackProgressInfo)reportPlaybackStart).SubtitleStreamIndex = request.SubtitleStreamIndex;
		((PlaybackProgressInfo)reportPlaybackStart).PlayMethod = request.PlayMethod;
		((PlaybackProgressInfo)reportPlaybackStart).PlaySessionId = request.PlaySessionId;
		((PlaybackProgressInfo)reportPlaybackStart).LiveStreamId = request.LiveStreamId;
		return Post(reportPlaybackStart);
	}

	public Task Post(ReportPlaybackStart request)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		((PlaybackProgressInfo)request).PlayMethod = ValidatePlayMethod(((PlaybackProgressInfo)request).PlayMethod, ((PlaybackProgressInfo)request).PlaySessionId);
		((PlaybackProgressInfo)request).SessionId = GetSession(_sessionContext).Id;
		return _sessionManager.OnPlaybackStart((PlaybackStartInfo)(object)request);
	}

	public Task Post(OnPlaybackProgress request)
	{
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		ReportPlaybackProgress reportPlaybackProgress = new ReportPlaybackProgress();
		((PlaybackProgressInfo)reportPlaybackProgress).ItemId = request.Id;
		((PlaybackProgressInfo)reportPlaybackProgress).PositionTicks = request.PositionTicks;
		((PlaybackProgressInfo)reportPlaybackProgress).IsMuted = request.IsMuted;
		((PlaybackProgressInfo)reportPlaybackProgress).IsPaused = request.IsPaused;
		((PlaybackProgressInfo)reportPlaybackProgress).MediaSourceId = request.MediaSourceId;
		((PlaybackProgressInfo)reportPlaybackProgress).AudioStreamIndex = request.AudioStreamIndex;
		((PlaybackProgressInfo)reportPlaybackProgress).SubtitleStreamIndex = request.SubtitleStreamIndex;
		((PlaybackProgressInfo)reportPlaybackProgress).VolumeLevel = request.VolumeLevel;
		((PlaybackProgressInfo)reportPlaybackProgress).PlayMethod = request.PlayMethod;
		((PlaybackProgressInfo)reportPlaybackProgress).PlaySessionId = request.PlaySessionId;
		((PlaybackProgressInfo)reportPlaybackProgress).LiveStreamId = request.LiveStreamId;
		((PlaybackProgressInfo)reportPlaybackProgress).RepeatMode = request.RepeatMode;
		return Post(reportPlaybackProgress);
	}

	public Task Post(ReportPlaybackProgress request)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		((PlaybackProgressInfo)request).PlayMethod = ValidatePlayMethod(((PlaybackProgressInfo)request).PlayMethod, ((PlaybackProgressInfo)request).PlaySessionId);
		((PlaybackProgressInfo)request).SessionId = GetSession(_sessionContext).Id;
		return _sessionManager.OnPlaybackProgress((PlaybackProgressInfo)(object)request);
	}

	public void Post(PingPlaybackSession request)
	{
	}

	public Task Delete(OnPlaybackStopped request)
	{
		ReportPlaybackStopped reportPlaybackStopped = new ReportPlaybackStopped();
		((PlaybackStopInfo)reportPlaybackStopped).ItemId = request.Id;
		((PlaybackStopInfo)reportPlaybackStopped).PositionTicks = request.PositionTicks;
		((PlaybackStopInfo)reportPlaybackStopped).MediaSourceId = request.MediaSourceId;
		((PlaybackStopInfo)reportPlaybackStopped).PlaySessionId = request.PlaySessionId;
		((PlaybackStopInfo)reportPlaybackStopped).LiveStreamId = request.LiveStreamId;
		((PlaybackStopInfo)reportPlaybackStopped).NextMediaType = request.NextMediaType;
		return Post(reportPlaybackStopped);
	}

	public async Task Post(ReportPlaybackStopped request)
	{
		Logger.Debug("ReportPlaybackStopped PlaySessionId: {0}", new object[1] { ((PlaybackStopInfo)request).PlaySessionId ?? string.Empty });
		if (!string.IsNullOrWhiteSpace(((PlaybackStopInfo)request).PlaySessionId))
		{
			await ApiEntryPoint.Instance.KillTranscodingJobs(_authContext.GetAuthorizationInfo(Request).DeviceId, ((PlaybackStopInfo)request).PlaySessionId, deleteFiles: true).ConfigureAwait(continueOnCapturedContext: false);
		}
		((PlaybackStopInfo)request).SessionId = GetSession(_sessionContext).Id;
		await _sessionManager.OnPlaybackStopped((PlaybackStopInfo)(object)request).ConfigureAwait(continueOnCapturedContext: false);
	}

	public object Delete(MarkUnplayedItem request)
	{
		return MarkUnplayed(request);
	}

	private UserItemDataDto MarkUnplayed(MarkUnplayedItem request)
	{
		User userById = _userManager.GetUserById(request.UserId);
		SessionInfo session = GetSession(_sessionContext);
		BaseItem itemById = _libraryManager.GetItemById(request.Id);
		UserItemDataDto result = UpdatePlayedStatus(userById, itemById, wasPlayed: false, null);
		SessionUserInfo[] additionalUsers = session.AdditionalUsers;
		foreach (SessionUserInfo val in additionalUsers)
		{
			User userById2 = _userManager.GetUserById(val.UserId);
			UpdatePlayedStatus(userById2, itemById, wasPlayed: false, null);
		}
		return result;
	}

	private UserItemDataDto UpdatePlayedStatus(User user, BaseItem item, bool wasPlayed, DateTime? datePlayed)
	{
		if (wasPlayed)
		{
			item.MarkPlayed(user, datePlayed, true);
		}
		else
		{
			item.MarkUnplayed(user);
		}
		return _userDataRepository.GetUserDataDto(item, user);
	}
}
