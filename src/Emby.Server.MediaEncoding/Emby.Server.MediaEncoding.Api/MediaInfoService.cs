using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;

namespace Emby.Server.MediaEncoding.Api;

[Authenticated]
public class MediaInfoService : BaseApiService
{
	private readonly IMediaSourceManager _mediaSourceManager;

	private readonly IDeviceManager _deviceManager;

	private readonly ILibraryManager _libraryManager;

	private readonly IServerConfigurationManager _config;

	private readonly INetworkManager _networkManager;

	private readonly IMediaEncoder _mediaEncoder;

	private readonly IUserManager _userManager;

	private readonly IJsonSerializer _json;

	private readonly IAuthorizationContext _authContext;

	private ILogManager _logManager;

	public MediaInfoService(IMediaSourceManager mediaSourceManager, IDeviceManager deviceManager, ILibraryManager libraryManager, IServerConfigurationManager config, INetworkManager networkManager, IMediaEncoder mediaEncoder, IUserManager userManager, IJsonSerializer json, IAuthorizationContext authContext, ILogManager logManager)
	{
		_mediaSourceManager = mediaSourceManager;
		_deviceManager = deviceManager;
		_libraryManager = libraryManager;
		_config = config;
		_networkManager = networkManager;
		_mediaEncoder = mediaEncoder;
		_userManager = userManager;
		_json = json;
		_authContext = authContext;
		_logManager = logManager;
	}

	public object Get(GetBitrateTestBytes request)
	{
		byte[] array = new byte[request.Size];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = 0;
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		dictionary["Content-Encoding"] = "identity";
		return ResultFactory.GetResult(Request, array, "application/octet-stream", (IDictionary<string, string>)dictionary);
	}

	public async Task<object> Get(GetPlaybackInfo request)
	{
		User userById = _userManager.GetUserById(request.UserId);
		BaseItem itemById = _libraryManager.GetItemById(request.Id);

		// T1 家族语义缺口（负路径防线最后一条）：合法但不存在的 Guid → 404
		// （原 null 直接传 GetPlaybackInfo → ArgumentNullException 400）
		if (itemById == null)
		{
			throw new ResourceNotFoundException(string.Format("Item {0} not found", request.Id));
		}

		return await GetPlaybackInfo(itemById, userById, allowMediaProbe: true).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<object> Post(OpenMediaSource request)
	{
		return await OpenMediaSource(request).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<LiveStreamResponse> OpenMediaSource(OpenMediaSource request)
	{
		AuthorizationInfo authInfo = _authContext.GetAuthorizationInfo(Request);
		LiveStreamResponse val = await _mediaSourceManager.OpenLiveStream((LiveStreamRequest)(object)request, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
		DeviceProfile deviceProfile = ((LiveStreamRequest)request).DeviceProfile;
		if (deviceProfile != null)
		{
			BaseItem itemById = _libraryManager.GetItemById(((LiveStreamRequest)request).ItemId);
			User userById = _userManager.GetUserById(((LiveStreamRequest)request).UserId);
			SetDeviceSpecificData(itemById.Id, itemById.MediaType, val.MediaSource, deviceProfile, authInfo, ((LiveStreamRequest)request).MaxStreamingBitrate ?? 0, ((LiveStreamRequest)request).StartTimeTicks ?? 0, val.MediaSource.Id, ((LiveStreamRequest)request).AudioStreamIndex, ((LiveStreamRequest)request).SubtitleStreamIndex, ((LiveStreamRequest)request).MaxAudioChannels, ((LiveStreamRequest)request).PlaySessionId, userById, ((LiveStreamRequest)request).EnableDirectPlay, ((LiveStreamRequest)request).DirectPlayProtocols, ((LiveStreamRequest)request).EnableDirectStream, enableTranscoding: true, allowVideoStreamCopy: true, allowAudioStreamCopy: true);
		}
		else if (!string.IsNullOrWhiteSpace(val.MediaSource.TranscodingUrl))
		{
			MediaSourceInfo mediaSource = val.MediaSource;
			mediaSource.TranscodingUrl = mediaSource.TranscodingUrl + "&LiveStreamId=" + val.MediaSource.LiveStreamId;
		}
		if (val.MediaSource != null)
		{
			NormalizeMediaSourceContainer(val.MediaSource, deviceProfile, (DlnaProfileType)1);
		}
		return val;
	}

	public Task Post(CloseMediaSource request)
	{
		return _mediaSourceManager.CloseLiveStream(request.LiveStreamId);
	}

	public async Task<object> Post(GetLiveStreamMediaInfo request)
	{
		return await _mediaSourceManager.GetLiveStreamMediaInfo(request.LiveStreamId, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PlaybackInfoResponse> GetPlaybackInfo(GetPostedPlaybackInfo request)
	{
		AuthorizationInfo authInfo = _authContext.GetAuthorizationInfo(Request);
		DeviceProfile profile = ((PlaybackInfoRequest)request).DeviceProfile;
		if ((int)_logManager.LogSeverity == 1)
		{
			Logger.Debug("GetPostedPlaybackInfo request: {0}", new object[1] { _json.SerializeToString((object)request) });
		}
		User userById = _userManager.GetUserById(((PlaybackInfoRequest)request).UserId);
		BaseItem item = _libraryManager.GetItemById(((PlaybackInfoRequest)request).Id);

		// T1 家族语义缺口（同 GET 版）：合法但不存在的 Guid → 404
		if (item == null)
		{
			throw new ResourceNotFoundException(string.Format("Item {0} not found", ((PlaybackInfoRequest)request).Id));
		}

		PlaybackInfoResponse info = await GetPlaybackInfo(item, userById, ((PlaybackInfoRequest)request).IsPlayback, ((PlaybackInfoRequest)request).MediaSourceId, ((PlaybackInfoRequest)request).LiveStreamId).ConfigureAwait(continueOnCapturedContext: false);
		if (profile != null)
		{
			string mediaSourceId = ((PlaybackInfoRequest)request).MediaSourceId;
			SetDeviceSpecificData(item, info, profile, authInfo, ((PlaybackInfoRequest)request).MaxStreamingBitrate ?? profile.MaxStreamingBitrate ?? 0, ((PlaybackInfoRequest)request).StartTimeTicks ?? 0, mediaSourceId, ((PlaybackInfoRequest)request).AudioStreamIndex, ((PlaybackInfoRequest)request).SubtitleStreamIndex, ((PlaybackInfoRequest)request).MaxAudioChannels, ((PlaybackInfoRequest)request).UserId, ((PlaybackInfoRequest)request).EnableDirectPlay, ((PlaybackInfoRequest)request).DirectPlayProtocols, ((PlaybackInfoRequest)request).EnableDirectStream, ((PlaybackInfoRequest)request).EnableTranscoding, ((PlaybackInfoRequest)request).AllowVideoStreamCopy, ((PlaybackInfoRequest)request).AllowAudioStreamCopy);
		}
		if (((PlaybackInfoRequest)request).AutoOpenLiveStream)
		{
			MediaSourceInfo val = (string.IsNullOrWhiteSpace(((PlaybackInfoRequest)request).MediaSourceId) ? info.MediaSources.FirstOrDefault() : info.MediaSources.FirstOrDefault((MediaSourceInfo i) => string.Equals(i.Id, ((PlaybackInfoRequest)request).MediaSourceId, StringComparison.Ordinal)));
			if (val != null && val.RequiresOpening && string.IsNullOrWhiteSpace(val.LiveStreamId))
			{
				MediaInfoService mediaInfoService = this;
				OpenMediaSource openMediaSource = new OpenMediaSource();
				((LiveStreamRequest)openMediaSource).AudioStreamIndex = ((PlaybackInfoRequest)request).AudioStreamIndex;
				((LiveStreamRequest)openMediaSource).DeviceProfile = ((PlaybackInfoRequest)request).DeviceProfile;
				((LiveStreamRequest)openMediaSource).EnableDirectPlay = ((PlaybackInfoRequest)request).EnableDirectPlay;
				((LiveStreamRequest)openMediaSource).EnableDirectStream = ((PlaybackInfoRequest)request).EnableDirectStream;
				((LiveStreamRequest)openMediaSource).ItemId = ((PlaybackInfoRequest)request).Id;
				((LiveStreamRequest)openMediaSource).MaxAudioChannels = ((PlaybackInfoRequest)request).MaxAudioChannels;
				((LiveStreamRequest)openMediaSource).MaxStreamingBitrate = ((PlaybackInfoRequest)request).MaxStreamingBitrate;
				((LiveStreamRequest)openMediaSource).PlaySessionId = info.PlaySessionId;
				((LiveStreamRequest)openMediaSource).StartTimeTicks = ((PlaybackInfoRequest)request).StartTimeTicks;
				((LiveStreamRequest)openMediaSource).SubtitleStreamIndex = ((PlaybackInfoRequest)request).SubtitleStreamIndex;
				((LiveStreamRequest)openMediaSource).UserId = ((PlaybackInfoRequest)request).UserId;
				((LiveStreamRequest)openMediaSource).OpenToken = val.OpenToken;
				((LiveStreamRequest)openMediaSource).DirectPlayProtocols = ((PlaybackInfoRequest)request).DirectPlayProtocols;
				LiveStreamResponse val2 = await mediaInfoService.OpenMediaSource(openMediaSource).ConfigureAwait(continueOnCapturedContext: false);
				info.MediaSources = new MediaSourceInfo[1] { val2.MediaSource };
			}
		}
		if (info.MediaSources != null)
		{
			MediaSourceInfo[] mediaSources = info.MediaSources;
			foreach (MediaSourceInfo mediaSource in mediaSources)
			{
				NormalizeMediaSourceContainer(mediaSource, profile, (DlnaProfileType)1);
			}
		}
		return info;
	}

	private void NormalizeMediaSourceContainer(MediaSourceInfo mediaSource, DeviceProfile profile, DlnaProfileType type)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		mediaSource.Container = StreamBuilder.NormalizeMediaSourceFormatIntoSingleContainer(mediaSource.Container, mediaSource.Path, profile, type);
	}

	public async Task<object> Post(GetPostedPlaybackInfo request)
	{
		return await GetPlaybackInfo(request).ConfigureAwait(continueOnCapturedContext: false);
	}

	private T Clone<T>(T obj)
	{
		string text = _json.SerializeToString((object)obj);
		return _json.DeserializeFromString<T>(text);
	}

	private async Task<PlaybackInfoResponse> GetPlaybackInfo(BaseItem item, User user, bool allowMediaProbe, string mediaSourceId = null, string liveStreamId = null)
	{
		PlaybackInfoResponse result = new PlaybackInfoResponse();
		if (string.IsNullOrWhiteSpace(liveStreamId))
		{
			PlaybackInfoResponse val = result;
			val.MediaSources = (await _mediaSourceManager.GetPlayackMediaSources(item, user, allowMediaProbe, true, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false)).ToArray();
			if (!string.IsNullOrWhiteSpace(mediaSourceId))
			{
				result.MediaSources = result.MediaSources.Where((MediaSourceInfo i) => string.Equals(i.Id, mediaSourceId, StringComparison.OrdinalIgnoreCase)).ToArray();
			}
		}
		else
		{
			MediaSourceInfo val2 = await _mediaSourceManager.GetLiveStream(liveStreamId, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			result.MediaSources = new MediaSourceInfo[1] { val2 };
		}
		if (result.MediaSources.Length == 0)
		{
			if (!result.ErrorCode.HasValue)
			{
				Logger.Info("Returning error code NoCompatibleStream", Array.Empty<object>());
				result.ErrorCode = (PlaybackErrorCode)1;
			}
		}
		else
		{
			result.MediaSources = Clone(result.MediaSources);
			result.PlaySessionId = Guid.NewGuid().ToString("N");
		}
		return result;
	}

	private void SetDeviceSpecificData(BaseItem item, PlaybackInfoResponse result, DeviceProfile profile, AuthorizationInfo auth, long clientMaxBitrate, long startTimeTicks, string mediaSourceId, int? audioStreamIndex, int? subtitleStreamIndex, int? maxAudioChannels, Guid userId, bool enableDirectPlay, MediaProtocol[] forceRemoteDirectPlayProtocols, bool enableDirectStream, bool enableTranscoding, bool allowVideoStreamCopy, bool allowAudioStreamCopy)
	{
		User userById = _userManager.GetUserById(userId);
		MediaSourceInfo[] mediaSources = result.MediaSources;
		foreach (MediaSourceInfo mediaSource in mediaSources)
		{
			SetDeviceSpecificData(item.Id, item.MediaType, mediaSource, profile, auth, clientMaxBitrate, startTimeTicks, mediaSourceId, audioStreamIndex, subtitleStreamIndex, maxAudioChannels, result.PlaySessionId, userById, enableDirectPlay, forceRemoteDirectPlayProtocols, enableDirectStream, enableTranscoding, allowVideoStreamCopy, allowAudioStreamCopy);
		}
		SortMediaSources(result, clientMaxBitrate, userById);
	}

	private bool EnableRemoteDirectPlay(string mediaType, MediaSourceInfo mediaSource, MediaProtocol[] forceRemoteDirectPlayProtocols)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		if (!mediaSource.IsRemote)
		{
			return false;
		}
		if (!forceRemoteDirectPlayProtocols.Contains(mediaSource.Protocol))
		{
			return false;
		}
		if (string.Equals(mediaType, "Video", StringComparison.OrdinalIgnoreCase) && mediaSource.MediaStreams.Any((MediaStream i) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			//IL_000f: Invalid comparison between Unknown and I4
			return ((int)i.Type == 0 || (int)i.Type == 1) && i.Index != 1;
		}))
		{
			return false;
		}
		return true;
	}

	private void SetDeviceSpecificData(Guid itemId, string mediaType, MediaSourceInfo mediaSource, DeviceProfile profile, AuthorizationInfo auth, long maxBitrate, long startTimeTicks, string mediaSourceId, int? audioStreamIndex, int? subtitleStreamIndex, int? maxAudioChannels, string playSessionId, User user, bool enableDirectPlay, MediaProtocol[] forceRemoteDirectPlayProtocols, bool enableDirectStream, bool enableTranscoding, bool allowVideoStreamCopy, bool allowAudioStreamCopy)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Expected Obj, but got Unknown
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected Obj, but got Unknown
		//IL_033e: Unknown result type (might be due to invalid IL or missing references)
		StreamBuilder val = new StreamBuilder((ITranscoderSupport)(object)_mediaEncoder, Logger);
		VideoOptions val2 = new VideoOptions();
		((AudioOptions)val2).MediaSources = new MediaSourceInfo[1] { mediaSource };
		((AudioOptions)val2).Context = (EncodingContext)0;
		((AudioOptions)val2).DeviceId = auth.DeviceId;
		((AudioOptions)val2).ItemId = itemId;
		((AudioOptions)val2).Profile = profile;
		((AudioOptions)val2).MaxAudioChannels = maxAudioChannels;
		VideoOptions val3 = val2;
		if (string.Equals(mediaSourceId, mediaSource.Id, StringComparison.OrdinalIgnoreCase))
		{
			((AudioOptions)val3).MediaSourceId = mediaSourceId;
			val3.AudioStreamIndex = audioStreamIndex;
			val3.SubtitleStreamIndex = subtitleStreamIndex;
		}
		if (!enableDirectPlay)
		{
			mediaSource.SupportsDirectPlay = false;
		}
		if (!enableDirectStream)
		{
			mediaSource.SupportsDirectStream = false;
		}
		if (string.Equals(mediaType, "Audio", StringComparison.OrdinalIgnoreCase))
		{
			if (!user.Policy.EnableAudioPlaybackTranscoding)
			{
				enableTranscoding = false;
			}
		}
		else if (string.Equals(mediaType, "Video", StringComparison.OrdinalIgnoreCase) && !user.Policy.EnableAudioPlaybackTranscoding && !user.Policy.EnableVideoPlaybackTranscoding && !user.Policy.EnablePlaybackRemuxing)
		{
			enableTranscoding = false;
		}
		if (!enableTranscoding)
		{
			mediaSource.SupportsTranscoding = false;
		}
		if (string.Equals(mediaType, "Audio", StringComparison.OrdinalIgnoreCase))
		{
			Logger.Info("User policy for {0}. EnableAudioPlaybackTranscoding: {1}", new object[2]
			{
				((BaseItem)user).Name,
				user.Policy.EnableAudioPlaybackTranscoding
			});
		}
		else
		{
			Logger.Info("User policy for {0}. EnablePlaybackRemuxing: {1} EnableVideoPlaybackTranscoding: {2} EnableAudioPlaybackTranscoding: {3}", new object[4]
			{
				((BaseItem)user).Name,
				user.Policy.EnablePlaybackRemuxing,
				user.Policy.EnableVideoPlaybackTranscoding,
				user.Policy.EnableAudioPlaybackTranscoding
			});
		}
		if (mediaSource.SupportsDirectPlay)
		{
			bool supportsDirectStream = mediaSource.SupportsDirectStream;
			mediaSource.SupportsDirectStream = true;
			if (maxBitrate > 0)
			{
				((AudioOptions)val3).MaxBitrate = maxBitrate;
			}
			bool flag = false;
			if (!mediaSource.SupportsTranscoding || EnableRemoteDirectPlay(mediaType, mediaSource, forceRemoteDirectPlayProtocols))
			{
				((AudioOptions)val3).ForceDirectPlay = true;
				flag = true;
			}
			StreamInfo val4 = (string.Equals(mediaType, "Audio", StringComparison.OrdinalIgnoreCase) ? val.BuildAudioItem((AudioOptions)(object)val3) : val.BuildVideoItem(val3));
			if (flag)
			{
				((AudioOptions)val3).ForceDirectPlay = false;
			}
			if (val4 == null || !val4.IsDirectStream)
			{
				mediaSource.SupportsDirectPlay = false;
			}
			mediaSource.SupportsDirectStream = supportsDirectStream;
			if (val4 != null)
			{
				if (val4.AudioStreamIndex.HasValue)
				{
					mediaSource.DefaultAudioStreamIndex = val4.AudioStreamIndex;
				}
				SetDeviceSpecificSubtitleInfo(val4, mediaSource, auth.Token);
			}
		}
		if (mediaSource.SupportsDirectStream)
		{
			((AudioOptions)val3).MaxBitrate = GetMaxBitrate(maxBitrate, user);
			if (!mediaSource.SupportsTranscoding)
			{
				((AudioOptions)val3).ForceDirectStream = true;
			}
			StreamInfo val5 = (string.Equals(mediaType, "Audio", StringComparison.OrdinalIgnoreCase) ? val.BuildAudioItem((AudioOptions)(object)val3) : val.BuildVideoItem(val3));
			if (val5 == null || !val5.IsDirectStream)
			{
				mediaSource.SupportsDirectStream = false;
			}
			if (val5 != null)
			{
				if (val5.AudioStreamIndex.HasValue)
				{
					mediaSource.DefaultAudioStreamIndex = val5.AudioStreamIndex;
				}
				SetDeviceSpecificSubtitleInfo(val5, mediaSource, auth.Token);
			}
		}
		if (!mediaSource.SupportsTranscoding)
		{
			return;
		}
		((AudioOptions)val3).MaxBitrate = GetMaxBitrate(maxBitrate, user);
		StreamInfo val6 = (string.Equals(mediaType, "Audio", StringComparison.OrdinalIgnoreCase) ? val.BuildAudioItem((AudioOptions)(object)val3) : val.BuildVideoItem(val3));
		if (val6 == null)
		{
			return;
		}
		val6.PlaySessionId = playSessionId;
		if ((int)val6.PlayMethod == 0)
		{
			val6.StartPositionTicks = startTimeTicks;
			mediaSource.TranscodingUrl = val6.ToUrl("-", auth.Token).TrimStart('-');
			if (!allowVideoStreamCopy)
			{
				mediaSource.TranscodingUrl += "&allowVideoStreamCopy=false";
			}
			if (!allowAudioStreamCopy)
			{
				mediaSource.TranscodingUrl += "&allowAudioStreamCopy=false";
			}
			mediaSource.TranscodingContainer = val6.Container;
			mediaSource.TranscodingSubProtocol = val6.SubProtocol;
		}
		if (val6.AudioStreamIndex.HasValue)
		{
			mediaSource.DefaultAudioStreamIndex = val6.AudioStreamIndex;
		}
		SetDeviceSpecificSubtitleInfo(val6, mediaSource, auth.Token);
	}

	private long? GetMaxBitrate(long clientMaxBitrate, User user)
	{
		long num = clientMaxBitrate;
		int num2 = ((user != null) ? user.Policy.RemoteClientBitrateLimit : 0);
		if (num2 <= 0)
		{
			num2 = _config.Configuration.RemoteClientBitrateLimit;
		}
		if (num2 > 0)
		{
			try
			{
				bool flag = _networkManager.IsInLocalNetwork(Request.RemoteIp);
				Logger.Info("RemoteClientBitrateLimit: {0}, RemoteIp: {1}, IsInLocalNetwork: {2}", new object[3] { num2, Request.RemoteIp, flag });
				if (!flag)
				{
					if (num <= 0)
					{
						num = num2;
					}
					num = Math.Min(num, num2);
				}
			}
			catch (Exception ex)
			{
				Logger.ErrorException("Error in NetworkManager.IsInLocalNetwork", ex, Array.Empty<object>());
			}
		}
		if (num <= 0)
		{
			return null;
		}
		return num;
	}

	private void SetDeviceSpecificSubtitleInfo(StreamInfo info, MediaSourceInfo mediaSource, string accessToken)
	{
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Invalid comparison between Unknown and I4
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Invalid comparison between Unknown and I4
		List<SubtitleStreamInfo> subtitleProfiles = info.GetSubtitleProfiles((ITranscoderSupport)(object)_mediaEncoder, false, "-", accessToken);
		mediaSource.DefaultSubtitleStreamIndex = info.SubtitleStreamIndex;
		mediaSource.TranscodeReasons = info.TranscodeReasons;
		foreach (SubtitleStreamInfo item in subtitleProfiles)
		{
			foreach (MediaStream mediaStream in mediaSource.MediaStreams)
			{
				if ((int)mediaStream.Type == 2 && mediaStream.Index == item.Index)
				{
					mediaStream.DeliveryMethod = item.DeliveryMethod;
					if ((int)item.DeliveryMethod == 2)
					{
						mediaStream.DeliveryUrl = item.Url.TrimStart('-');
						mediaStream.IsExternalUrl = item.IsExternalUrl;
					}
				}
			}
		}
	}

	private void SortMediaSources(PlaybackInfoResponse result, long clientMaxBitrate, User user)
	{
		List<MediaSourceInfo> list = result.MediaSources.ToList();
		result.MediaSources = result.MediaSources.OrderBy((MediaSourceInfo i) =>
		{
			//IL_0009: Unknown result type (might be due to invalid IL or missing references)
			return (!i.SupportsDirectPlay || (int)i.Protocol != 0) ? 1 : 0;
		}).ThenBy((MediaSourceInfo i) => (!i.SupportsDirectPlay && !i.SupportsDirectStream) ? 1 : 0).ThenBy((MediaSourceInfo i) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			MediaProtocol protocol = i.Protocol;
			return ((int)protocol != 0) ? 1 : 0;
		})
			.ThenBy((MediaSourceInfo i) =>
			{
				long? num = ((i.IsRemote && i.SupportsDirectPlay) ? new long?(clientMaxBitrate) : GetMaxBitrate(clientMaxBitrate, user));
				return (!num.HasValue || !i.Bitrate.HasValue) ? 1 : ((i.Bitrate.Value > num.Value) ? 2 : 0);
			})
			.ThenBy(list.IndexOf)
			.ToArray();
	}
}
