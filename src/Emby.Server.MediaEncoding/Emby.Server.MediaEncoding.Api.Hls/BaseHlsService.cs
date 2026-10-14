using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;

namespace Emby.Server.MediaEncoding.Api.Hls;

public abstract class BaseHlsService : BaseStreamingService
{
	protected override TranscodingJobType TranscodingJobType => (TranscodingJobType)1;

	protected abstract string GetAudioArguments(StreamState state, EncodingOptions encodingOptions);

	protected abstract string GetVideoArguments(StreamState state, EncodingOptions encodingOptions);

	protected string GetSegmentFileExtension(StreamRequest request)
	{
		string segmentContainer = request.SegmentContainer;
		if (!string.IsNullOrWhiteSpace(segmentContainer))
		{
			return "." + segmentContainer;
		}
		return ".ts";
	}

	protected async Task<object> ProcessRequest(StreamRequest request)
	{
		CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		StreamState state = await GetState(request, cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
		TranscodingJob job = null;
		string playlist = ((EncodingJobInfo)state).OutputFilePath;
		if (!FileSystem.FileExists(playlist))
		{
			SemaphoreSlim transcodingLock = ApiEntryPoint.Instance.GetTranscodingLock(playlist);
			await transcodingLock.WaitAsync(cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				if (!FileSystem.FileExists(playlist))
				{
					try
					{
						job = await StartFfMpeg(state, playlist, ApiEntryPoint.Instance.GetEncodingOptions(), cancellationTokenSource).ConfigureAwait(continueOnCapturedContext: false);
						job.IsLiveOutput = true;
					}
					catch
					{
						((EncodingJobInfo)state).Dispose();
						throw;
					}
					int minSegments = state.MinSegments;
					if (minSegments > 0)
					{
						await WaitForMinimumSegmentCount(playlist, minSegments, cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
			}
			finally
			{
				transcodingLock.Release();
			}
		}
		job = job ?? ApiEntryPoint.Instance.OnTranscodeBeginRequest(playlist, TranscodingJobType);
		if (job != null)
		{
			ApiEntryPoint.Instance.OnTranscodeEndRequest(job);
		}
		return ResultFactory.GetResult(Request, GetLivePlaylistText(playlist, state.SegmentLength), MimeTypes.GetMimeType("playlist.m3u8"), (IDictionary<string, string>)new Dictionary<string, string>());
	}

	private string GetLivePlaylistText(string path, int segmentLength)
	{
		using Stream stream = FileSystem.GetFileStream(path, (FileOpenMode)3, (FileAccessMode)1, (FileShareMode)3, false);
		using StreamReader streamReader = new StreamReader(stream);
		string text = streamReader.ReadToEnd().Replace("#EXTM3U", "#EXTM3U\n#EXT-X-PLAYLIST-TYPE:EVENT");
		string text2 = "#EXT-X-TARGETDURATION:" + segmentLength.ToString(UsCulture);
		return StringHelper.Replace(text, "#EXT-X-TARGETDURATION:" + (segmentLength - 1).ToString(UsCulture), text2, StringComparison.OrdinalIgnoreCase);
	}

	protected static FileSystemMetadata GetLastTranscodingFile(string playlist, string segmentExtension, IFileSystem fileSystem)
	{
		string directoryName = fileSystem.GetDirectoryName(playlist);
		string filePrefix = Path.GetFileNameWithoutExtension(playlist) ?? string.Empty;
		try
		{
			return (from i in fileSystem.GetFiles(directoryName, new string[1] { segmentExtension }, true, false)
				where Path.GetFileNameWithoutExtension(i.Name).StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase) && i.Name.IndexOf("_init", StringComparison.OrdinalIgnoreCase) == -1
				select i).OrderByDescending((Func<FileSystemMetadata, DateTime>)fileSystem.GetLastWriteTimeUtc).FirstOrDefault();
		}
		catch (IOException)
		{
			return null;
		}
	}

	protected string GetSegmentPath(StreamState state, string playlist, string segmentId)
	{
		string directoryName = FileSystem.GetDirectoryName(playlist);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(playlist);
		string text = (string.Equals(segmentId, "init", StringComparison.OrdinalIgnoreCase) ? ".mp4" : GetSegmentFileExtension(state.Request));
		return Path.Combine(directoryName, fileNameWithoutExtension + segmentId + text);
	}

	protected override bool ValidateProcessStarted(StreamState state, string outputPath)
	{
		string segmentPath = GetSegmentPath(state, outputPath, GetStartNumber(state).ToString(UsCulture));
		return FileSystem.FileExists(segmentPath);
	}

	protected virtual async Task WaitForMinimumSegmentCount(string playlist, int segmentCount, CancellationToken cancellationToken)
	{
		Logger.Debug("Waiting for {0} segments in {1}", new object[2] { segmentCount, playlist });
		// BUG-22 双根因：
		// ① 旧版 Task.Delay(100) 写在 using 块内部——播放列表句柄每周期被持有 ~100ms/150ms
		//   （67% 占空比），ffmpeg 9 的 .tmp+rename（MoveFileEx REPLACE，实测任何共享模式
		//   下有句柄即 err=5）必然撞车 → 播放列表永久停在旧内容。修复：读完立即释放，
		//   句柄窗口压缩到 ~1ms，撞车概率趋零；偶发撞车由 ffmpeg 下一次分段写自愈。
		// ② 轮询无超时会在播放列表停滞时无限挂起。上限 30s：到点按现状返回并告警。
		DateTime deadline = DateTime.UtcNow.AddSeconds(30.0);
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				int num = 0;
				using (Stream fileStream = GetPlaylistFileStream(playlist))
				using (StreamReader reader = new StreamReader(fileStream))
				{
					while (!reader.EndOfStream)
					{
						if (reader.ReadLine().IndexOf("#EXTINF:", StringComparison.OrdinalIgnoreCase) != -1)
						{
							num++;
						}
					}
				}
				if (num >= segmentCount)
				{
					Logger.Debug("Finished waiting for {0} segments in {1}", new object[2] { segmentCount, playlist });
					return;
				}
			}
			catch (IOException)
			{
			}
			if (DateTime.UtcNow >= deadline)
			{
				Logger.Warn("Timed out (30s) waiting for {0} segments in {1}. Serving current playlist state.", new object[2] { segmentCount, playlist });
				return;
			}
			await Task.Delay(150, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	protected Stream GetPlaylistFileStream(string path)
	{
		// BUG-22：ffmpeg 9 用 .tmp+rename 原子写播放列表；句柄必须带 FILE_SHARE_DELETE
		// （FileShareMode.ReadWriteDelete），否则 rename 失败、播放列表停在旧内容。
		return FileSystem.GetFileStream(path, (FileOpenMode)3, (FileAccessMode)1, (FileShareMode)4, (FileOpenOptions)134217728);
	}

	protected override string GetCommandLineArguments(string outputPath, EncodingOptions encodingOptions, StreamState state, bool isEncoding)
	{
		int num = 0;
		string text = ((num == 0) ? string.Empty : $"-itsoffset {TimeSpan.FromMilliseconds(num).TotalSeconds.ToString(UsCulture)} ");
		int numberOfThreads = EncodingHelper.GetNumberOfThreads((EncodingJobInfo)(object)state, encodingOptions, ((EncodingJobInfo)state).OutputVideoCodec);
		string inputModifier = EncodingHelper.GetInputModifier((EncodingJobInfo)(object)state, encodingOptions);
		string text2 = (isEncoding ? GetStartNumber(state).ToString(UsCulture) : "0");
		string text3 = string.Empty;
		if (state.Request is GetLiveHlsStream)
		{
			text3 = string.Format(" -hls_base_url \"{0}/\"", "hls/" + Path.GetFileNameWithoutExtension(outputPath));
		}
		if (true)
		{
			string text4 = Path.Combine(FileSystem.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath)) + "%d" + GetSegmentFileExtension(state.Request);
			string empty = string.Empty;
			string text5 = state.Request.SegmentContainer;
			if (string.IsNullOrWhiteSpace(text5))
			{
				// BUG-21：无 SegmentContainer 时按分片扩展名推导封装格式（默认 .ts→mpegts），
				// 避免 -segment_format 空值造成 ffmpeg 参数移位（-segment_list_entry_prefix 被吞）
				text5 = GetSegmentFileExtension(state.Request).TrimStart('.');
			}
			if (string.Equals(text5, "ts", StringComparison.OrdinalIgnoreCase))
			{
				text5 = "mpegts";
			}
			text3 = string.Format("\"{0}/\"", "hls/" + Path.GetFileNameWithoutExtension(outputPath));
			return string.Format("{0} {1} -map_metadata -1 -map_chapters -1 -threads {2} {3} {4} {5} -f segment -max_delay 5000000 -avoid_negative_ts disabled -start_at_zero -segment_time {6} {10} -individual_header_trailer 0 -segment_format {11} -segment_list_entry_prefix {12} -segment_list_type m3u8 -segment_start_number {7} -segment_list \"{8}\" -y \"{9}\"", inputModifier, EncodingHelper.GetInputArgument((EncodingJobInfo)(object)state, encodingOptions), numberOfThreads, EncodingHelper.GetMapArgs((EncodingJobInfo)(object)state), GetVideoArguments(state, encodingOptions), GetAudioArguments(state, encodingOptions), state.SegmentLength.ToString(UsCulture), text2, outputPath, text4, empty, text5, text3).Trim();
		}
		return $"{text} {inputModifier} {EncodingHelper.GetInputArgument((EncodingJobInfo)(object)state, encodingOptions)} -map_metadata -1 -map_chapters -1 -threads {numberOfThreads} {EncodingHelper.GetMapArgs((EncodingJobInfo)(object)state)} {GetVideoArguments(state, encodingOptions)} -max_delay 5000000 -avoid_negative_ts disabled -start_at_zero {GetAudioArguments(state, encodingOptions)} -hls_time {state.SegmentLength.ToString(UsCulture)} -individual_header_trailer 0 -start_number {text2} -hls_list_size {((EncodingJobInfo)state).HlsListSize.ToString(UsCulture)}{text3} -y \"{outputPath}\"".Trim();
	}

	protected override string GetDefaultH264Preset()
	{
		return "veryfast";
	}

	protected virtual int GetStartNumber(StreamState state)
	{
		return 0;
	}

	public BaseHlsService(IServerConfigurationManager serverConfig, IUserManager userManager, ILibraryManager libraryManager, IIsoManager isoManager, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IDlnaManager dlnaManager, ISubtitleEncoder subtitleEncoder, IDeviceManager deviceManager, IMediaSourceManager mediaSourceManager, IZipClient zipClient, IJsonSerializer jsonSerializer, IAuthorizationContext authorizationContext, IServerApplicationHost appHost, IAssemblyInfo assemblyInfo)
		: base(serverConfig, userManager, libraryManager, isoManager, mediaEncoder, fileSystem, dlnaManager, subtitleEncoder, deviceManager, mediaSourceManager, zipClient, jsonSerializer, authorizationContext, appHost, assemblyInfo)
	{
	}
}
