using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;

namespace Emby.Server.MediaEncoding.Api.Hls;

public class DynamicHlsService : BaseHlsService
{
	private const int BufferSize = 81920;

	protected INetworkManager NetworkManager { get; private set; }

	public DynamicHlsService(IServerConfigurationManager serverConfig, IUserManager userManager, ILibraryManager libraryManager, IIsoManager isoManager, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IDlnaManager dlnaManager, ISubtitleEncoder subtitleEncoder, IDeviceManager deviceManager, IMediaSourceManager mediaSourceManager, IZipClient zipClient, IJsonSerializer jsonSerializer, IAuthorizationContext authorizationContext, INetworkManager networkManager, IServerApplicationHost appHost, IAssemblyInfo assemblyInfo)
		: base(serverConfig, userManager, libraryManager, isoManager, mediaEncoder, fileSystem, dlnaManager, subtitleEncoder, deviceManager, mediaSourceManager, zipClient, jsonSerializer, authorizationContext, appHost, assemblyInfo)
	{
		NetworkManager = networkManager;
	}

	public Task<object> Get(GetMasterHlsVideoPlaylist request)
	{
		return GetMasterPlaylistInternal(request, "GET");
	}

	public Task<object> Head(GetMasterHlsVideoPlaylist request)
	{
		return GetMasterPlaylistInternal(request, "HEAD");
	}

	public Task<object> Get(GetMasterHlsAudioPlaylist request)
	{
		return GetMasterPlaylistInternal(request, "GET");
	}

	public Task<object> Head(GetMasterHlsAudioPlaylist request)
	{
		return GetMasterPlaylistInternal(request, "HEAD");
	}

	public Task<object> Get(GetVariantHlsVideoPlaylist request)
	{
		return GetVariantPlaylistInternal(request, isOutputVideo: true, "main");
	}

	public Task<object> Get(GetVariantHlsAudioPlaylist request)
	{
		return GetVariantPlaylistInternal(request, isOutputVideo: false, "main");
	}

	public Task<object> Head(GetHlsSegment request)
	{
		return GetDynamicSegment(request, request.SegmentId);
	}

	public Task<object> Get(GetHlsSegment request)
	{
		return GetDynamicSegment(request, request.SegmentId);
	}

	private async Task<object> GetDynamicSegment(StreamRequest request, string segmentId)
	{
		if ((((BaseEncodingJobOptions)request).StartTimeTicks ?? 0) > 0)
		{
			throw new ArgumentException("StartTimeTicks is not allowed.");
		}
		CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		CancellationToken cancellationToken = cancellationTokenSource.Token;
		StreamState state = await GetState(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		string playlistPath = Path.ChangeExtension(((EncodingJobInfo)state).OutputFilePath, ".m3u8");
		string segmentPath = GetSegmentPath(state, playlistPath, segmentId);
		int requestedIndex = ((!string.Equals(segmentId, "init", StringComparison.OrdinalIgnoreCase)) ? int.Parse(segmentId, NumberStyles.Integer, UsCulture) : 0);
		string segmentExtension = GetSegmentFileExtension(state.Request);
		TranscodingJob transcodingJob = null;
		if (FileSystem.FileExists(segmentPath))
		{
			transcodingJob = ApiEntryPoint.Instance.OnTranscodeBeginRequest(playlistPath, TranscodingJobType);
			return await GetSegmentResult(state, playlistPath, segmentPath, segmentExtension, requestedIndex, transcodingJob, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		SemaphoreSlim transcodingLock = ApiEntryPoint.Instance.GetTranscodingLock(playlistPath);
		await transcodingLock.WaitAsync(cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
		bool released = false;
		bool flag = false;
		try
		{
			if (FileSystem.FileExists(segmentPath))
			{
				transcodingJob = ApiEntryPoint.Instance.OnTranscodeBeginRequest(playlistPath, TranscodingJobType);
				transcodingLock.Release();
				released = true;
				return await GetSegmentResult(state, playlistPath, segmentPath, segmentExtension, requestedIndex, transcodingJob, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			int? currentTranscodingIndex = GetCurrentTranscodingIndex(playlistPath, segmentExtension);
			int num = (((EncodingJobInfo)state).IsVideoRequest ? (24 / state.SegmentLength) : (48 / state.SegmentLength));
			if (!currentTranscodingIndex.HasValue)
			{
				Logger.Debug("Starting transcoding because currentTranscodingIndex=null", Array.Empty<object>());
				flag = true;
			}
			else if (requestedIndex < currentTranscodingIndex.Value)
			{
				Logger.Debug("Starting transcoding because requestedIndex={0} and currentTranscodingIndex={1}", new object[2] { requestedIndex, currentTranscodingIndex });
				flag = true;
			}
			else if (requestedIndex - currentTranscodingIndex.Value > num)
			{
				Logger.Debug("Starting transcoding because segmentGap is {0} and max allowed gap is {1}. requestedIndex={2}", new object[3]
				{
					requestedIndex - currentTranscodingIndex.Value,
					num,
					requestedIndex
				});
				flag = true;
			}
			if (flag)
			{
				try
				{
					await ApiEntryPoint.Instance.KillTranscodingJobs(((BaseEncodingJobOptions)request).DeviceId, request.PlaySessionId, deleteFiles: false).ConfigureAwait(continueOnCapturedContext: false);
					if (currentTranscodingIndex.HasValue)
					{
						await DeleteLastFile(playlistPath, segmentExtension, 0).ConfigureAwait(continueOnCapturedContext: false);
					}
					((BaseEncodingJobOptions)request).StartTimeTicks = GetStartPositionTicks(state, requestedIndex);
					transcodingJob = await StartFfMpeg(state, playlistPath, ApiEntryPoint.Instance.GetEncodingOptions(), cancellationTokenSource).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch
				{
					((EncodingJobInfo)state).Dispose();
					throw;
				}
			}
			else
			{
				transcodingJob = ApiEntryPoint.Instance.OnTranscodeBeginRequest(playlistPath, TranscodingJobType);
			}
		}
		finally
		{
			if (!released)
			{
				transcodingLock.Release();
			}
		}
		Logger.Info("returning {0}", new object[1] { segmentPath });
		transcodingJob = transcodingJob ?? ApiEntryPoint.Instance.OnTranscodeBeginRequest(playlistPath, TranscodingJobType);
		return await GetSegmentResult(state, playlistPath, segmentPath, segmentExtension, requestedIndex, transcodingJob, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private long GetStartPositionTicks(StreamState state, int requestedIndex)
	{
		double num = 0.0;
		double[] segmentLengths = GetSegmentLengths(state);
		if (requestedIndex >= segmentLengths.Length)
		{
			throw new ArgumentException($"Invalid segment index requested: {requestedIndex} - Segment count: {segmentLengths.Length}");
		}
		for (int i = 0; i < requestedIndex; i++)
		{
			num += segmentLengths[i];
		}
		return TimeSpan.FromSeconds(num).Ticks;
	}

	private long GetEndPositionTicks(StreamState state, int requestedIndex)
	{
		double num = 0.0;
		double[] segmentLengths = GetSegmentLengths(state);
		if (requestedIndex >= segmentLengths.Length)
		{
			throw new ArgumentException($"Invalid segment index requested: {requestedIndex} - Segment count: {segmentLengths.Length}");
		}
		for (int i = 0; i <= requestedIndex; i++)
		{
			num += segmentLengths[i];
		}
		return TimeSpan.FromSeconds(num).Ticks;
	}

	private double[] GetSegmentLengths(StreamState state)
	{
		List<double> list = new List<double>();
		long num = ((EncodingJobInfo)state).RunTimeTicks ?? 0;
		long ticks = TimeSpan.FromSeconds(state.SegmentLength).Ticks;
		while (num > 0)
		{
			long num2 = ((num >= ticks) ? ticks : num);
			list.Add(TimeSpan.FromTicks(num2).TotalSeconds);
			num -= num2;
		}
		return list.ToArray();
	}

	public int? GetCurrentTranscodingIndex(string playlist, string segmentExtension)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		TranscodingJob transcodingJob = ApiEntryPoint.Instance.GetTranscodingJob(playlist, TranscodingJobType);
		if (transcodingJob == null || transcodingJob.HasExited)
		{
			return null;
		}
		FileSystemMetadata lastTranscodingFile = BaseHlsService.GetLastTranscodingFile(playlist, segmentExtension, FileSystem);
		if (lastTranscodingFile == null)
		{
			return null;
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(playlist);
		return int.Parse(Path.GetFileNameWithoutExtension(lastTranscodingFile.Name).Substring(fileNameWithoutExtension.Length), NumberStyles.Integer, UsCulture);
	}

	private Task DeleteLastFile(string playlistPath, string segmentExtension, int retryCount)
	{
		FileSystemMetadata lastTranscodingFile = BaseHlsService.GetLastTranscodingFile(playlistPath, segmentExtension, FileSystem);
		if (lastTranscodingFile != null)
		{
			return DeleteFile(lastTranscodingFile.FullName, retryCount);
		}
		return Task.CompletedTask;
	}

	private async Task DeleteFile(string path, int retryCount)
	{
		if (retryCount >= 5)
		{
			return;
		}
		Logger.Debug("Deleting partial HLS file {0}", new object[1] { path });
		try
		{
			FileSystem.DeleteFile(path);
		}
		catch (IOException ex)
		{
			Logger.ErrorException("Error deleting partial stream file(s) {0}", (Exception)ex, new object[1] { path });
			await Task.Delay(100).ConfigureAwait(continueOnCapturedContext: false);
			await DeleteFile(path, retryCount + 1).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex2)
		{
			Logger.ErrorException("Error deleting partial stream file(s) {0}", ex2, new object[1] { path });
		}
	}

	protected override int GetStartNumber(StreamState state)
	{
		return GetStartNumber(state.Request);
	}

	private int GetStartNumber(StreamRequest request)
	{
		string text = "0";
		if (request is GetHlsSegment getHlsSegment)
		{
			text = getHlsSegment.SegmentId;
		}
		if (string.Equals(text, "init", StringComparison.OrdinalIgnoreCase))
		{
			text = "0";
		}
		return int.Parse(text, NumberStyles.Integer, UsCulture);
	}

	private async Task<object> GetSegmentResult(StreamState state, string playlistPath, string segmentPath, string segmentExtension, int segmentIndex, TranscodingJob transcodingJob, CancellationToken cancellationToken)
	{
		bool segmentFileExists = FileSystem.FileExists(segmentPath);
		if ((transcodingJob?.HasExited ?? false) & segmentFileExists)
		{
			return await GetSegmentResult(state, segmentPath, segmentIndex, transcodingJob).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (segmentFileExists && segmentIndex < GetCurrentTranscodingIndex(playlistPath, segmentExtension))
		{
			return await GetSegmentResult(state, segmentPath, segmentIndex, transcodingJob).ConfigureAwait(continueOnCapturedContext: false);
		}
		string segmentFilename = Path.GetFileName(segmentPath);
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				if (FileSystem.ReadAllText(playlistPath, Encoding.UTF8).IndexOf(segmentFilename, StringComparison.OrdinalIgnoreCase) != -1)
				{
					if (!segmentFileExists)
					{
						segmentFileExists = FileSystem.FileExists(segmentPath);
					}
					if (segmentFileExists)
					{
						return await GetSegmentResult(state, segmentPath, segmentIndex, transcodingJob).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
			}
			catch (IOException)
			{
			}
			await Task.Delay(100, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		cancellationToken.ThrowIfCancellationRequested();
		return await GetSegmentResult(state, segmentPath, segmentIndex, transcodingJob).ConfigureAwait(continueOnCapturedContext: false);
	}

	private Task<object> GetSegmentResult(StreamState state, string segmentPath, int index, TranscodingJob transcodingJob)
	{
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Expected Obj, but got Unknown
		long segmentEndingPositionTicks = GetEndPositionTicks(state, index);
		return ResultFactory.GetStaticFileResult(Request, new StaticFileResultOptions
		{
			Path = segmentPath,
			FileShare = (FileShareMode)3,
			OnComplete = () =>
			{
				if (transcodingJob != null)
				{
					transcodingJob.DownloadPositionTicks = Math.Max(transcodingJob.DownloadPositionTicks ?? segmentEndingPositionTicks, segmentEndingPositionTicks);
					ApiEntryPoint.Instance.OnTranscodeEndRequest(transcodingJob);
				}
			},
			OnError = () =>
			{
				ApiEntryPoint.Instance.OnTranscodeEndRequest(transcodingJob);
			}
		});
	}

	private async Task<object> GetMasterPlaylistInternal(StreamRequest request, string method)
	{
		StreamState streamState = await GetState(request, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrEmpty(((BaseEncodingJobOptions)request).MediaSourceId))
		{
			throw new ArgumentException("MediaSourceId is required");
		}
		string text = string.Empty;
		if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
		{
			int num = ((EncodingJobInfo)streamState).OutputAudioBitrate ?? 0;
			int num2 = ((EncodingJobInfo)streamState).OutputVideoBitrate ?? 0;
			text = GetMasterPlaylistFileText(streamState, num2 + num);
			Logger.Debug("Master playlist: \n" + text, Array.Empty<object>());
		}
		return ResultFactory.GetResult(Request, text, MimeTypes.GetMimeType("playlist.m3u8"), (IDictionary<string, string>)new Dictionary<string, string>());
	}

	private string GetHlsVersion(StreamRequest request)
	{
		string segmentContainer = request.SegmentContainer;
		if (string.Equals(segmentContainer, "m4s", StringComparison.OrdinalIgnoreCase) || string.Equals(segmentContainer, "mp4", StringComparison.OrdinalIgnoreCase))
		{
			return "7";
		}
		return "3";
	}

	private string GetMasterPlaylistFileText(StreamState state, int totalBitrate)
	{
		//IL_01aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b0: Invalid comparison between Unknown and I4
		//IL_01d2: Unknown result type (might be due to invalid IL or missing references)
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("#EXTM3U");
		stringBuilder.AppendLine("#EXT-X-VERSION:" + GetHlsVersion(state.Request));
		stringBuilder.AppendLine("#EXT-X-INDEPENDENT-SEGMENTS");
		bool isSegmentedLiveStream = ((EncodingJobInfo)state).IsSegmentedLiveStream;
		int num = Request.RawUrl.IndexOf('?');
		string text = ((num == -1) ? string.Empty : Request.RawUrl.Substring(num));
		if (text.IndexOf("SegmentContainer", StringComparison.OrdinalIgnoreCase) == -1 && !string.IsNullOrWhiteSpace(state.Request.SegmentContainer))
		{
			text = text + "&SegmentContainer=" + state.Request.SegmentContainer;
		}
		if (!((EncodingJobInfo)state).IsVideoRequest && text.IndexOf("AudioBitrate", StringComparison.OrdinalIgnoreCase) == -1 && ((BaseEncodingJobOptions)state.Request).AudioBitRate.HasValue)
		{
			text = text + "&AudioBitrate=" + ((BaseEncodingJobOptions)state.Request).AudioBitRate.Value.ToString(CultureInfo.InvariantCulture);
		}
		if (!string.IsNullOrWhiteSpace(((BaseEncodingJobOptions)state.Request).TranscodeReasons) && text.IndexOf("TranscodeReasons=", StringComparison.OrdinalIgnoreCase) == -1)
		{
			text = text + "&TranscodeReasons=" + ((BaseEncodingJobOptions)state.Request).TranscodeReasons;
		}
		string text2 = (isSegmentedLiveStream ? "live.m3u8" : "main.m3u8");
		text2 += text;
		StreamRequest request = state.Request;
		List<MediaStream> list = ((EncodingJobInfo)state).MediaSource.MediaStreams.Where((MediaStream i) => i.IsTextSubtitleStream).ToList();
		string text3 = ((list.Count > 0 && request is GetMasterHlsVideoPlaylist && ((int)((EncodingJobInfo)state).SubtitleDeliveryMethod == 3 || state.VideoRequest.EnableSubtitlesInManifest)) ? "subs" : null);
		if (((EncodingJobInfo)state).SubtitleStream != null && (int)((EncodingJobInfo)state).SubtitleDeliveryMethod == 0)
		{
			text3 = null;
		}
		if (!string.IsNullOrWhiteSpace(text3))
		{
			AddSubtitles(state, list, stringBuilder);
		}
		AppendPlaylist(stringBuilder, state, text2, totalBitrate, text3);
		if (EnableAdaptiveBitrateStreaming(state, isSegmentedLiveStream))
		{
			int num2 = ((state.VideoRequest != null) ? (((BaseEncodingJobOptions)state.VideoRequest).VideoBitRate ?? 0) : 0);
			int bitrateVariation = GetBitrateVariation(totalBitrate);
			int bitrate = totalBitrate - bitrateVariation;
			string url = ReplaceBitrate(text2, num2, num2 - bitrateVariation);
			AppendPlaylist(stringBuilder, state, url, bitrate, text3);
			bitrateVariation *= 2;
			bitrate = totalBitrate - bitrateVariation;
			url = ReplaceBitrate(text2, num2, num2 - bitrateVariation);
			AppendPlaylist(stringBuilder, state, url, bitrate, text3);
		}
		return stringBuilder.ToString();
	}

	private string ReplaceBitrate(string url, int oldValue, int newValue)
	{
		return StringHelper.Replace(url, "videobitrate=" + oldValue.ToString(UsCulture), "videobitrate=" + newValue.ToString(UsCulture), StringComparison.OrdinalIgnoreCase);
	}

	private void AddSubtitles(StreamState state, IEnumerable<MediaStream> subtitles, StringBuilder builder)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Invalid comparison between Unknown and I4
		int? num = ((((EncodingJobInfo)state).SubtitleStream == null || (int)((EncodingJobInfo)state).SubtitleDeliveryMethod != 3) ? ((int?)null) : new int?(((EncodingJobInfo)state).SubtitleStream.Index));
		List<string> source = new List<string>();
		foreach (MediaStream subtitle in subtitles)
		{
			string text = subtitle.DisplayTitle;
			while (source.Contains(text, StringComparer.OrdinalIgnoreCase))
			{
				text += " (1)";
			}
			bool flag = num.HasValue && num.Value == subtitle.Index;
			bool isForced = subtitle.IsForced;
			string text2 = $"{((BaseEncodingJobOptions)state.Request).MediaSourceId}/Subtitles/{subtitle.Index.ToString(UsCulture)}/subtitles.m3u8?SegmentLength={30.ToString(UsCulture)}&api_key={AuthorizationContext.GetAuthorizationInfo(Request).Token}";
			string value = string.Format("#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"subs\",NAME=\"{0}\",DEFAULT={1},FORCED={2},AUTOSELECT=YES,URI=\"{3}\",LANGUAGE=\"{4}\"", text, flag ? "YES" : "NO", isForced ? "YES" : "NO", text2, subtitle.Language ?? "Unknown");
			builder.AppendLine(value);
		}
	}

	private bool EnableAdaptiveBitrateStreaming(StreamState state, bool isLiveStream)
	{
		if (Request.IsLocal)
		{
			return false;
		}
		try
		{
			if (NetworkManager.IsInLocalNetwork(Request.RemoteIp))
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			Logger.ErrorException("Error in NetworkManager.IsInLocalNetwork", ex, Array.Empty<object>());
		}
		if (state.Request is IMasterHlsRequest { EnableAdaptiveBitrateStreaming: false })
		{
			return false;
		}
		if (isLiveStream || string.IsNullOrWhiteSpace(((EncodingJobInfo)state).MediaPath))
		{
			return false;
		}
		if (string.Equals(((EncodingJobInfo)state).OutputVideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (string.Equals(((EncodingJobInfo)state).OutputAudioCodec, "copy", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		_ = ((EncodingJobInfo)state).IsVideoRequest;
		return false;
	}

	private void AppendPlaylist(StringBuilder builder, StreamState state, string url, int bitrate, string subtitleGroup)
	{
		string text = "#EXT-X-STREAM-INF:BANDWIDTH=" + Convert.ToInt32((double)bitrate * 1.2).ToString(UsCulture) + ",AVERAGE-BANDWIDTH=" + bitrate.ToString(UsCulture);
		if (((EncodingJobInfo)state).IsVideoRequest)
		{
			string videoRangeValue = GetVideoRangeValue(state);
			if (!string.IsNullOrEmpty(videoRangeValue))
			{
				text = string.Concat(text, ",VIDEO-RANGE=" + videoRangeValue);
			}
			string codecsValue = GetCodecsValue(state);
			if (!string.IsNullOrEmpty(codecsValue))
			{
				text = text + ",CODECS=\"" + codecsValue + "\"";
			}
			int? outputWidth = ((EncodingJobInfo)state).OutputWidth;
			int? outputHeight = ((EncodingJobInfo)state).OutputHeight;
			if (outputWidth.HasValue && outputHeight.HasValue)
			{
				text = text + ",RESOLUTION=" + outputWidth.Value.ToString(UsCulture) + "x" + outputHeight.Value.ToString(UsCulture);
			}
			float? targetFramerate = ((EncodingJobInfo)state).TargetFramerate;
			if (targetFramerate.HasValue)
			{
				text = text + ",FRAME-RATE=" + targetFramerate.Value.ToString("0.000", UsCulture);
			}
		}
		if (!string.IsNullOrWhiteSpace(subtitleGroup))
		{
			text += $",SUBTITLES=\"{subtitleGroup}\"";
		}
		builder.AppendLine(text);
		builder.AppendLine(url);
	}

	private string GetVideoRangeValue(StreamState state)
	{
		return ((EncodingJobInfo)state).TargetVideoRange;
	}

	private string GetCodecsValue(StreamState state)
	{
		List<string> list = new List<string>();
		string videoCodecValue = GetVideoCodecValue(state);
		string audioCodecValue = GetAudioCodecValue(state);
		if (!string.IsNullOrEmpty(videoCodecValue))
		{
			list.Add(videoCodecValue);
			if (!string.IsNullOrEmpty(audioCodecValue))
			{
				list.Add(audioCodecValue);
			}
		}
		if (list.Count < 2)
		{
			return null;
		}
		return string.Join(",", list.ToArray());
	}

	private string GetVideoCodecValue(StreamState state)
	{
		if (string.Equals(((EncodingJobInfo)state).ActualOutputVideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
		{
			double num = ((EncodingJobInfo)state).TargetVideoLevel ?? 0.0;
			string targetVideoProfile = ((EncodingJobInfo)state).TargetVideoProfile;
			if ((targetVideoProfile ?? string.Empty).IndexOf("baseline", StringComparison.OrdinalIgnoreCase) != -1)
			{
				if (num == 30.0)
				{
					return "avc1.42001e";
				}
				if (num == 31.0)
				{
					return "avc1.42001f";
				}
			}
			else if (string.Equals(targetVideoProfile, "main", StringComparison.OrdinalIgnoreCase))
			{
				if (num == 30.0)
				{
					return "avc1.4d001e";
				}
				if (num == 31.0)
				{
					return "avc1.4d001f";
				}
				if (num == 40.0)
				{
					return "avc1.4d0028";
				}
			}
			else if (string.Equals(targetVideoProfile, "high", StringComparison.OrdinalIgnoreCase))
			{
				if (num == 31.0)
				{
					return "avc1.64001f";
				}
				if (num == 40.0)
				{
					return "avc1.640028";
				}
				if (num == 41.0)
				{
					return "avc1.640029";
				}
			}
		}
		return null;
	}

	private string GetAudioCodecValue(StreamState state)
	{
		string actualOutputAudioCodec = ((EncodingJobInfo)state).ActualOutputAudioCodec;
		if (string.Equals(actualOutputAudioCodec, "aac", StringComparison.OrdinalIgnoreCase))
		{
			if (string.Equals(((EncodingJobInfo)state).TargetAudioProfile, "he-aac", StringComparison.OrdinalIgnoreCase))
			{
				return "mp4a.40.5";
			}
			return "mp4a.40.2";
		}
		if (string.Equals(actualOutputAudioCodec, "mp3", StringComparison.OrdinalIgnoreCase))
		{
			return "mp4a.40.34";
		}
		return null;
	}

	private int GetBitrateVariation(int bitrate)
	{
		int result = 50000;
		if (bitrate >= 10000000)
		{
			result = 2000000;
		}
		else if (bitrate >= 5000000)
		{
			result = 1500000;
		}
		else if (bitrate >= 3000000)
		{
			result = 1000000;
		}
		else if (bitrate >= 2000000)
		{
			result = 500000;
		}
		else if (bitrate >= 1000000)
		{
			result = 300000;
		}
		else if (bitrate >= 600000)
		{
			result = 200000;
		}
		else if (bitrate >= 400000)
		{
			result = 100000;
		}
		return result;
	}

	private async Task<object> GetVariantPlaylistInternal(StreamRequest request, bool isOutputVideo, string name)
	{
		StreamState streamState = await GetState(request, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
		double[] segmentLengths = GetSegmentLengths(streamState);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("#EXTM3U");
		stringBuilder.AppendLine("#EXT-X-PLAYLIST-TYPE:VOD");
		stringBuilder.AppendLine("#EXT-X-VERSION:" + GetHlsVersion(request));
		stringBuilder.AppendLine("#EXT-X-TARGETDURATION:" + Math.Ceiling((segmentLengths.Length != 0) ? segmentLengths.Max() : ((double)streamState.SegmentLength)).ToString(UsCulture));
		stringBuilder.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");
		int num = Request.RawUrl.IndexOf('?');
		string text = ((num == -1) ? string.Empty : Request.RawUrl.Substring(num));
		string segmentContainer = streamState.Request.SegmentContainer;
		if (string.Equals(segmentContainer, "m4s", StringComparison.OrdinalIgnoreCase) || string.Equals(segmentContainer, "mp4", StringComparison.OrdinalIgnoreCase))
		{
			stringBuilder.AppendLine(string.Format("#EXT-X-MAP:URI=\"hls1/{0}/{1}{2}{3}\"", name, "init", ".mp4", text));
		}
		int num2 = 0;
		double[] array = segmentLengths;
		foreach (double num3 in array)
		{
			stringBuilder.AppendLine("#EXTINF:" + num3.ToString("0.0000", UsCulture) + ", nodesc");
			stringBuilder.AppendLine($"hls1/{name}/{num2.ToString(UsCulture)}{GetSegmentFileExtension(request)}{text}");
			num2++;
		}
		stringBuilder.AppendLine("#EXT-X-ENDLIST");
		string text2 = stringBuilder.ToString();
		return ResultFactory.GetResult(Request, text2, MimeTypes.GetMimeType("playlist.m3u8"), (IDictionary<string, string>)new Dictionary<string, string>());
	}

	protected override string GetAudioArguments(StreamState state, EncodingOptions encodingOptions)
	{
		string audioEncoder = EncodingHelper.GetAudioEncoder((EncodingJobInfo)(object)state);
		if (!((EncodingJobInfo)state).IsVideoRequest)
		{
			if (string.Equals(audioEncoder, "copy", StringComparison.OrdinalIgnoreCase))
			{
				return "-acodec copy";
			}
			List<string> list = new List<string>();
			list.Add("-acodec " + audioEncoder);
			if (((EncodingJobInfo)state).OutputAudioBitrate.HasValue)
			{
				list.Add("-ab " + ((EncodingJobInfo)state).OutputAudioBitrate.Value.ToString(UsCulture));
			}
			if (((EncodingJobInfo)state).OutputAudioChannels.HasValue)
			{
				list.Add("-ac " + ((EncodingJobInfo)state).OutputAudioChannels.Value.ToString(UsCulture));
			}
			if (((EncodingJobInfo)state).OutputAudioSampleRate.HasValue)
			{
				list.Add("-ar " + ((EncodingJobInfo)state).OutputAudioSampleRate.Value.ToString(UsCulture));
			}
			list.Add("-vn");
			return string.Join(" ", list.ToArray());
		}
		if (string.Equals(audioEncoder, "copy", StringComparison.OrdinalIgnoreCase))
		{
			string videoEncoder = EncodingHelper.GetVideoEncoder((EncodingJobInfo)(object)state, encodingOptions);
			if (string.Equals(videoEncoder, "copy", StringComparison.OrdinalIgnoreCase) && ((EncodingJobInfo)state).EnableBreakOnNonKeyFrames(videoEncoder))
			{
				return "-codec:a:0 copy -copypriorss:a:0 0";
			}
			return "-codec:a:0 copy";
		}
		string text = "-codec:a:0 " + audioEncoder;
		int? outputAudioChannels = ((EncodingJobInfo)state).OutputAudioChannels;
		if (outputAudioChannels.HasValue)
		{
			text = text + " -ac " + outputAudioChannels.Value;
		}
		int? outputAudioBitrate = ((EncodingJobInfo)state).OutputAudioBitrate;
		if (outputAudioBitrate.HasValue)
		{
			text = text + " -ab " + outputAudioBitrate.Value.ToString(UsCulture);
		}
		if (((EncodingJobInfo)state).OutputAudioSampleRate.HasValue)
		{
			text = text + " -ar " + ((EncodingJobInfo)state).OutputAudioSampleRate.Value.ToString(UsCulture);
		}
		return text + " " + EncodingHelper.GetAudioFilterParam((EncodingJobInfo)(object)state, encodingOptions, true);
	}

	protected override string GetVideoArguments(StreamState state, EncodingOptions encodingOptions)
	{
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Invalid comparison between Unknown and I4
		if (!((EncodingJobInfo)state).IsVideoRequest)
		{
			return string.Empty;
		}
		string videoEncoder = EncodingHelper.GetVideoEncoder((EncodingJobInfo)(object)state, encodingOptions);
		string text = "-codec:v:0 " + videoEncoder;
		if (((BaseEncodingJobOptions)state.Request).EnableMpegtsM2TsMode)
		{
			text += " -mpegts_m2ts_mode 1";
		}
		string a = state.Request.SegmentContainer;
		if (string.Equals(a, "ts", StringComparison.OrdinalIgnoreCase))
		{
			a = "mpegts";
		}
		if (string.Equals(videoEncoder, "copy", StringComparison.OrdinalIgnoreCase))
		{
			if (string.Equals(a, "mpegts", StringComparison.OrdinalIgnoreCase) && ((EncodingJobInfo)state).VideoStream != null && EncodingHelper.IsH264(((EncodingJobInfo)state).VideoStream) && !string.Equals(((EncodingJobInfo)state).VideoStream.NalLengthSize, "0", StringComparison.OrdinalIgnoreCase))
			{
				text += " -bsf:v h264_mp4toannexb";
			}
			goto IL_0184;
		}
		string text2 = $" -force_key_frames \"expr:if(isnan(prev_forced_t),eq(t,t),gte(t,prev_forced_t+{((state.SegmentLength == 3) ? 3 : 2).ToString(UsCulture)}))\"";
		if (videoEncoder.IndexOf("nvenc", StringComparison.OrdinalIgnoreCase) != -1)
		{
			text2 = " -forced-idr 1 " + text2;
		}
		int num;
		if (((EncodingJobInfo)state).SubtitleStream != null && !((EncodingJobInfo)state).SubtitleStream.IsTextSubtitleStream)
		{
			num = (((int)((EncodingJobInfo)state).SubtitleDeliveryMethod == 0) ? 1 : 0);
			if (num != 0)
			{
				goto IL_014b;
			}
		}
		else
		{
			num = 0;
		}
		text += EncodingHelper.GetOutputSizeParam((EncodingJobInfo)(object)state, encodingOptions, videoEncoder, true);
		goto IL_014b;
		IL_014b:
		if (num != 0)
		{
			text += EncodingHelper.GetGraphicalSubtitleParam((EncodingJobInfo)(object)state, encodingOptions, videoEncoder);
		}
		text = text + " " + EncodingHelper.GetVideoQualityParam((EncodingJobInfo)(object)state, videoEncoder, encodingOptions, GetDefaultH264Preset()) + text2;
		goto IL_0184;
		IL_0184:
		if (text.IndexOf("-copyts", StringComparison.OrdinalIgnoreCase) == -1)
		{
			text += " -copyts";
		}
		if (!string.IsNullOrEmpty(((EncodingJobInfo)state).OutputVideoSync))
		{
			text = text + " -vsync " + ((EncodingJobInfo)state).OutputVideoSync;
		}
		return text + EncodingHelper.GetOutputFFlags((EncodingJobInfo)(object)state);
	}

	protected override string GetCommandLineArguments(string outputPath, EncodingOptions encodingOptions, StreamState state, bool isEncoding)
	{
		int numberOfThreads = EncodingHelper.GetNumberOfThreads((EncodingJobInfo)(object)state, encodingOptions, ((EncodingJobInfo)state).OutputVideoCodec);
		string inputModifier = EncodingHelper.GetInputModifier((EncodingJobInfo)(object)state, encodingOptions);
		int startNumber = GetStartNumber(state);
		string text = (isEncoding ? startNumber.ToString(UsCulture) : "0");
		string text2 = (((EncodingJobInfo)state).IsVideoRequest ? EncodingHelper.GetMapArgs((EncodingJobInfo)(object)state) : string.Empty);
		string text3 = Path.Combine(FileSystem.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath)) + "%d" + GetSegmentFileExtension(state.Request);
		string text4 = string.Empty;
		if (isEncoding && startNumber > 0)
		{
			int num = state.SegmentLength * startNumber;
			text4 = $"-segment_time_delta -{num}";
		}
		string text5 = state.Request.SegmentContainer;
		if (string.Equals(text5, "ts", StringComparison.OrdinalIgnoreCase))
		{
			text5 = "mpegts";
		}
		string videoEncoder = EncodingHelper.GetVideoEncoder((EncodingJobInfo)(object)state, encodingOptions);
		bool flag = ((EncodingJobInfo)state).EnableBreakOnNonKeyFrames(videoEncoder);
		if (!string.Equals(text5, "m4s", StringComparison.OrdinalIgnoreCase) && !string.Equals(text5, "mp4", StringComparison.OrdinalIgnoreCase))
		{
			string text6 = (flag ? " -break_non_keyframes 1" : "");
			return string.Format("{0} {1} -threads {2} {3} {4} {5} -f segment -max_delay 5000000 -avoid_negative_ts disabled -map_metadata -1 -map_chapters -1 -start_at_zero -segment_time {6} {10} -individual_header_trailer 0{12} -segment_format {11} -segment_list_type m3u8 -segment_start_number {7} -segment_list \"{8}\" -y \"{9}\"", inputModifier, EncodingHelper.GetInputArgument((EncodingJobInfo)(object)state, encodingOptions), numberOfThreads, text2, GetVideoArguments(state, encodingOptions), GetAudioArguments(state, encodingOptions), state.SegmentLength.ToString(UsCulture), text, outputPath, text3, text4, text5, text6).Trim();
		}
		string text7 = Path.Combine(FileSystem.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath)) + "init.mp4";
		List<string> list = new List<string>();
		if (flag)
		{
			list.Add("split_by_time");
		}
		list.Add("temp_file");
		string text8 = ((list.Count == 0) ? "" : ("-hls_flags " + string.Join("+", list.ToArray())));
		string text9 = ((string.Equals(text5, "m4s", StringComparison.OrdinalIgnoreCase) || string.Equals(text5, "m4s", StringComparison.OrdinalIgnoreCase)) ? "fmp4" : "mpegts");
		return string.Format("{0} {1} -map_metadata -1 -map_chapters -1 -threads {2} {3} {4} {5} -max_delay 5000000 -avoid_negative_ts disabled -start_at_zero -hls_segment_type {13} -hls_time {6} {10} -individual_header_trailer 0 -start_number {7} -hls_list_size {8} -hls_segment_filename \"{12}\" -hls_fmp4_init_filename \"{11}\" -y \"{9}\"", inputModifier, EncodingHelper.GetInputArgument((EncodingJobInfo)(object)state, encodingOptions), numberOfThreads, text2, GetVideoArguments(state, encodingOptions), GetAudioArguments(state, encodingOptions), state.SegmentLength.ToString(UsCulture), text, ((EncodingJobInfo)state).HlsListSize.ToString(UsCulture), outputPath, text8, text7, text3, text9).Trim();
	}
}
