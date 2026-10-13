using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Emby.Server.MediaEncoding.Api.Hls;
using Emby.Server.MediaEncoding.Api.Progressive;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.System;

namespace Emby.Server.MediaEncoding.Api;

[Authenticated]
public class UniversalAudioService : BaseApiService
{
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

	protected IImageProcessor ImageProcessor { get; private set; }

	protected INetworkManager NetworkManager { get; private set; }

	protected IEnvironmentInfo EnvironmentInfo { get; private set; }

	protected IHttpClient HttpClient { get; private set; }

	protected IServerApplicationHost AppHost { get; private set; }

	protected ILogManager LogManager { get; private set; }

	protected IAssemblyInfo AssemblyInfo { get; private set; }

	public UniversalAudioService(IServerConfigurationManager serverConfigurationManager, ILogManager logManager, IUserManager userManager, ILibraryManager libraryManager, IIsoManager isoManager, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IDlnaManager dlnaManager, IDeviceManager deviceManager, ISubtitleEncoder subtitleEncoder, IMediaSourceManager mediaSourceManager, IZipClient zipClient, IJsonSerializer jsonSerializer, IAuthorizationContext authorizationContext, IImageProcessor imageProcessor, INetworkManager networkManager, IEnvironmentInfo environmentInfo, IHttpClient httpClient, IServerApplicationHost appHost, IAssemblyInfo assemblyInfo)
	{
		ServerConfigurationManager = serverConfigurationManager;
		UserManager = userManager;
		LibraryManager = libraryManager;
		IsoManager = isoManager;
		MediaEncoder = mediaEncoder;
		FileSystem = fileSystem;
		DlnaManager = dlnaManager;
		DeviceManager = deviceManager;
		SubtitleEncoder = subtitleEncoder;
		MediaSourceManager = mediaSourceManager;
		ZipClient = zipClient;
		JsonSerializer = jsonSerializer;
		AuthorizationContext = authorizationContext;
		ImageProcessor = imageProcessor;
		NetworkManager = networkManager;
		EnvironmentInfo = environmentInfo;
		HttpClient = httpClient;
		AppHost = appHost;
		LogManager = logManager;
		AssemblyInfo = assemblyInfo;
	}

	public Task<object> Get(GetUniversalAudioStream request)
	{
		return GetUniversalStream(request, isHeadRequest: false);
	}

	public Task<object> Head(GetUniversalAudioStream request)
	{
		return GetUniversalStream(request, isHeadRequest: true);
	}

	private DeviceProfile GetDeviceProfile(GetUniversalAudioStream request)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected Obj, but got Unknown
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected Obj, but got Unknown
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0154: Expected Obj, but got Unknown
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ba: Expected Obj, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected Obj, but got Unknown
		//IL_01cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01df: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_020f: Expected Obj, but got Unknown
		//IL_0221: Unknown result type (might be due to invalid IL or missing references)
		//IL_0226: Unknown result type (might be due to invalid IL or missing references)
		//IL_022d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0234: Unknown result type (might be due to invalid IL or missing references)
		//IL_023b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0263: Expected Obj, but got Unknown
		//IL_026d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0272: Unknown result type (might be due to invalid IL or missing references)
		//IL_0279: Unknown result type (might be due to invalid IL or missing references)
		//IL_0285: Unknown result type (might be due to invalid IL or missing references)
		//IL_0296: Expected Obj, but got Unknown
		DeviceProfile val = new DeviceProfile();
		List<DirectPlayProfile> list = new List<DirectPlayProfile>();
		if (string.IsNullOrWhiteSpace(request.Container))
		{
			list.Add(new DirectPlayProfile
			{
				Type = (DlnaProfileType)0
			});
		}
		else
		{
			string[] array = (request.Container ?? string.Empty).Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < array.Length; i++)
			{
				string[] array2 = array[i].Split(new char[1] { '|' }, StringSplitOptions.RemoveEmptyEntries);
				string audioCodec = ((array2.Length == 1) ? null : string.Join(",", array2.Skip(1).ToArray()));
				list.Add(new DirectPlayProfile
				{
					Type = (DlnaProfileType)0,
					Container = array2[0],
					AudioCodec = audioCodec
				});
			}
		}
		val.DirectPlayProfiles = list.ToArray();
		val.TranscodingProfiles = new TranscodingProfile[1]
		{
			new TranscodingProfile
			{
				Type = (DlnaProfileType)0,
				Context = (EncodingContext)0,
				Container = request.TranscodingContainer,
				AudioCodec = request.AudioCodec,
				Protocol = request.TranscodingProtocol,
				BreakOnNonKeyFrames = request.BreakOnNonKeyFrames,
				MaxAudioChannels = (request.TranscodingAudioChannels.HasValue ? request.TranscodingAudioChannels.Value.ToString(CultureInfo.InvariantCulture) : null)
			}
		};
		List<CodecProfile> list2 = new List<CodecProfile>();
		List<ProfileCondition> list3 = new List<ProfileCondition>();
		if (request.MaxAudioSampleRate.HasValue)
		{
			list3.Add(new ProfileCondition
			{
				Condition = (ProfileConditionType)2,
				IsRequired = false,
				Property = (ProfileConditionValue)22,
				Value = request.MaxAudioSampleRate.Value.ToString(CultureInfo.InvariantCulture)
			});
		}
		if (request.MaxAudioBitDepth.HasValue)
		{
			list3.Add(new ProfileCondition
			{
				Condition = (ProfileConditionType)2,
				IsRequired = false,
				Property = (ProfileConditionValue)23,
				Value = request.MaxAudioBitDepth.Value.ToString(CultureInfo.InvariantCulture)
			});
		}
		if (request.MaxAudioChannels.HasValue)
		{
			list3.Add(new ProfileCondition
			{
				Condition = (ProfileConditionType)2,
				IsRequired = false,
				Property = (ProfileConditionValue)0,
				Value = request.MaxAudioChannels.Value.ToString(CultureInfo.InvariantCulture)
			});
		}
		if (list3.Count > 0)
		{
			list2.Add(new CodecProfile
			{
				Type = (CodecType)2,
				Container = request.Container,
				Conditions = list3.ToArray()
			});
		}
		val.CodecProfiles = list2.ToArray();
		return val;
	}

	private async Task<object> GetUniversalStream(GetUniversalAudioStream request, bool isHeadRequest)
	{
		DeviceProfile deviceProfile = GetDeviceProfile(request);
		AuthorizationContext.GetAuthorizationInfo(Request).DeviceId = request.DeviceId;
		MediaInfoService mediaInfoService = new MediaInfoService(MediaSourceManager, DeviceManager, LibraryManager, ServerConfigurationManager, NetworkManager, MediaEncoder, UserManager, JsonSerializer, AuthorizationContext, LogManager)
		{
			Request = Request
		};
		GetPostedPlaybackInfo getPostedPlaybackInfo = new GetPostedPlaybackInfo();
		((PlaybackInfoRequest)getPostedPlaybackInfo).Id = request.Id;
		((PlaybackInfoRequest)getPostedPlaybackInfo).MaxAudioChannels = request.MaxAudioChannels;
		((PlaybackInfoRequest)getPostedPlaybackInfo).MaxStreamingBitrate = request.MaxStreamingBitrate;
		((PlaybackInfoRequest)getPostedPlaybackInfo).StartTimeTicks = request.StartTimeTicks;
		((PlaybackInfoRequest)getPostedPlaybackInfo).UserId = request.UserId;
		((PlaybackInfoRequest)getPostedPlaybackInfo).DeviceProfile = deviceProfile;
		((PlaybackInfoRequest)getPostedPlaybackInfo).MediaSourceId = request.MediaSourceId;
		PlaybackInfoResponse val = await mediaInfoService.GetPlaybackInfo(getPostedPlaybackInfo).ConfigureAwait(continueOnCapturedContext: false);
		MediaSourceInfo val2 = val.MediaSources[0];
		if (val2.SupportsDirectPlay && (int)val2.Protocol == 1 && request.EnableRedirection && val2.IsRemote && request.EnableRemoteMedia)
		{
			return ResultFactory.GetRedirectResult(val2.Path);
		}
		bool supportsDirectStream = val2.SupportsDirectStream;
		if (!supportsDirectStream && string.Equals(val2.TranscodingSubProtocol, "hls", StringComparison.OrdinalIgnoreCase))
		{
			DynamicHlsService dynamicHlsService = new DynamicHlsService(ServerConfigurationManager, UserManager, LibraryManager, IsoManager, MediaEncoder, FileSystem, DlnaManager, SubtitleEncoder, DeviceManager, MediaSourceManager, ZipClient, JsonSerializer, AuthorizationContext, NetworkManager, AppHost, AssemblyInfo)
			{
				Request = Request
			};
			TranscodingProfile val3 = deviceProfile.TranscodingProfiles[0];
			GetMasterHlsAudioPlaylist getMasterHlsAudioPlaylist = new GetMasterHlsAudioPlaylist();
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).AudioBitRate = (supportsDirectStream ? ((int?)null) : new int?(Convert.ToInt32(Math.Min(request.MaxStreamingBitrate ?? 192000, 384000L))));
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).AudioCodec = val3.AudioCodec;
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).Container = ".m3u8";
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).DeviceId = request.DeviceId;
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).Id = request.Id;
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).MaxAudioChannels = request.MaxAudioChannels;
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).MediaSourceId = val2.Id;
			getMasterHlsAudioPlaylist.PlaySessionId = val.PlaySessionId;
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).StartTimeTicks = request.StartTimeTicks;
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).Static = supportsDirectStream;
			getMasterHlsAudioPlaylist.SegmentContainer = request.TranscodingContainer;
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).AudioSampleRate = request.MaxAudioSampleRate;
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).MaxAudioBitDepth = request.MaxAudioBitDepth;
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).BreakOnNonKeyFrames = val3.BreakOnNonKeyFrames;
			((BaseEncodingJobOptions)getMasterHlsAudioPlaylist).TranscodeReasons = ((val2.TranscodeReasons == null) ? null : string.Join(",", val2.TranscodeReasons));
			GetMasterHlsAudioPlaylist request2 = getMasterHlsAudioPlaylist;
			if (isHeadRequest)
			{
				return await dynamicHlsService.Head(request2).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await dynamicHlsService.Get(request2).ConfigureAwait(continueOnCapturedContext: false);
		}
		AudioService audioService = new AudioService(ServerConfigurationManager, HttpClient, UserManager, LibraryManager, IsoManager, MediaEncoder, FileSystem, DlnaManager, SubtitleEncoder, DeviceManager, MediaSourceManager, ZipClient, JsonSerializer, AuthorizationContext, ImageProcessor, EnvironmentInfo, AppHost, AssemblyInfo)
		{
			Request = Request
		};
		GetAudioStream getAudioStream = new GetAudioStream();
		((BaseEncodingJobOptions)getAudioStream).AudioBitRate = (supportsDirectStream ? ((int?)null) : new int?(Convert.ToInt32(Math.Min(request.MaxStreamingBitrate ?? 192000, 2147483647L))));
		((BaseEncodingJobOptions)getAudioStream).AudioCodec = request.AudioCodec;
		((BaseEncodingJobOptions)getAudioStream).Container = (supportsDirectStream ? null : ("." + val2.TranscodingContainer));
		((BaseEncodingJobOptions)getAudioStream).DeviceId = request.DeviceId;
		((BaseEncodingJobOptions)getAudioStream).Id = request.Id;
		((BaseEncodingJobOptions)getAudioStream).MaxAudioChannels = request.MaxAudioChannels;
		((BaseEncodingJobOptions)getAudioStream).MediaSourceId = val2.Id;
		getAudioStream.PlaySessionId = val.PlaySessionId;
		((BaseEncodingJobOptions)getAudioStream).StartTimeTicks = request.StartTimeTicks;
		((BaseEncodingJobOptions)getAudioStream).Static = supportsDirectStream;
		((BaseEncodingJobOptions)getAudioStream).AudioSampleRate = request.MaxAudioSampleRate;
		((BaseEncodingJobOptions)getAudioStream).MaxAudioBitDepth = request.MaxAudioBitDepth;
		((BaseEncodingJobOptions)getAudioStream).TranscodeReasons = ((val2.TranscodeReasons == null) ? null : string.Join(",", val2.TranscodeReasons));
		GetAudioStream request3 = getAudioStream;
		if (isHeadRequest)
		{
			return await audioService.Head(request3).ConfigureAwait(continueOnCapturedContext: false);
		}
		return await audioService.Get(request3).ConfigureAwait(continueOnCapturedContext: false);
	}
}
