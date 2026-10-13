using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Security;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Session;

namespace MediaBrowser.Controller.Session;

public interface ISessionManager
{
	IEnumerable<SessionInfo> Sessions { get; }

	event EventHandler<PlaybackProgressEventArgs> PlaybackStart;

	event EventHandler<PlaybackProgressEventArgs> PlaybackProgress;

	event EventHandler<PlaybackStopEventArgs> PlaybackStopped;

	event EventHandler<SessionEventArgs> SessionStarted;

	event EventHandler<SessionEventArgs> SessionEnded;

	event EventHandler<SessionEventArgs> SessionActivity;

	event EventHandler<SessionEventArgs> CapabilitiesChanged;

	event EventHandler<GenericEventArgs<AuthenticationRequest>> AuthenticationFailed;

	event EventHandler<GenericEventArgs<AuthenticationResult>> AuthenticationSucceeded;

	SessionInfo LogSessionActivity(string appName, string appVersion, string deviceId, string deviceName, string remoteEndPoint, User user);

	void UpdateDeviceName(string sessionId, string reportedDeviceName);

	Task OnPlaybackStart(PlaybackStartInfo info);

	Task OnPlaybackProgress(PlaybackProgressInfo info);

	Task OnPlaybackProgress(PlaybackProgressInfo info, bool isAutomated);

	Task OnPlaybackStopped(PlaybackStopInfo info);

	void ReportSessionEnded(string sessionId);

	Task SendGeneralCommand(string controllingSessionId, string sessionId, GeneralCommand command, CancellationToken cancellationToken);

	Task SendMessageCommand(string controllingSessionId, string sessionId, MessageCommand command, CancellationToken cancellationToken);

	Task SendPlayCommand(string controllingSessionId, string sessionId, PlayRequest command, CancellationToken cancellationToken);

	Task SendBrowseCommand(string controllingSessionId, string sessionId, BrowseRequest command, CancellationToken cancellationToken);

	Task SendPlaystateCommand(string controllingSessionId, string sessionId, PlaystateRequest command, CancellationToken cancellationToken);

	Task SendMessageToAdminSessions<T>(string name, T data, CancellationToken cancellationToken);

	Task SendMessageToUserSessions<T>(List<Guid> userIds, string name, T data, CancellationToken cancellationToken);

	Task SendMessageToUserSessions<T>(List<Guid> userIds, string name, Func<T> dataFn, CancellationToken cancellationToken);

	Task SendMessageToUserDeviceSessions<T>(string deviceId, string name, T data, CancellationToken cancellationToken);

	Task SendMessageToUserDeviceAndAdminSessions<T>(string deviceId, string name, T data, CancellationToken cancellationToken);

	Task SendRestartRequiredNotification(CancellationToken cancellationToken);

	Task SendServerShutdownNotification(CancellationToken cancellationToken);

	Task SendServerRestartNotification(CancellationToken cancellationToken);

	void AddAdditionalUser(string sessionId, Guid userId);

	void RemoveAdditionalUser(string sessionId, Guid userId);

	void ReportNowViewingItem(string sessionId, string itemId);

	void ReportNowViewingItem(string sessionId, BaseItemDto item);

	Task<AuthenticationResult> AuthenticateNewSession(AuthenticationRequest request);

	Task<AuthenticationResult> CreateNewSession(AuthenticationRequest request);

	void ReportCapabilities(string sessionId, ClientCapabilities capabilities);

	void ReportTranscodingInfo(string deviceId, TranscodingInfo info);

	void ClearTranscodingInfo(string deviceId);

	SessionInfo GetSession(string deviceId, string client, string version);

	SessionInfo GetSessionByAuthenticationToken(string token, string deviceId, string remoteEndpoint);

	SessionInfo GetSessionByAuthenticationToken(AuthenticationInfo info, string deviceId, string remoteEndpoint, string appVersion);

	void Logout(string accessToken);

	void Logout(AuthenticationInfo authenticationInfo);

	void RevokeUserTokens(Guid userId, string currentAccessToken);

	void RevokeToken(string id);

	void CloseIfNeeded(SessionInfo session);
}
