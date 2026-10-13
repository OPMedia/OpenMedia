using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.IO;

namespace Emby.Server.MediaEncoding.Api.Hls;

public class HlsSegmentService : BaseApiService
{
	private readonly IServerApplicationPaths _appPaths;

	private readonly IServerConfigurationManager _config;

	private readonly IFileSystem _fileSystem;

	public HlsSegmentService(IServerApplicationPaths appPaths, IServerConfigurationManager config, IFileSystem fileSystem)
	{
		_appPaths = appPaths;
		_config = config;
		_fileSystem = fileSystem;
	}

	public Task Delete(StopEncodingProcess request)
	{
		return ApiEntryPoint.Instance.KillTranscodingJobs(request.DeviceId, request.PlaySessionId, deleteFiles: true);
	}

	public Task<object> Get(GetHlsVideoSegmentLegacy request)
	{
		string path = request.SegmentId + Path.GetExtension(Request.PathInfo);
		string tempTranscodingPath = ApiEntryPoint.Instance.GetTempTranscodingPath(request.PlaySessionId);
		path = Path.Combine(tempTranscodingPath, path);
		string normalizedPlaylistId = request.PlaylistId;
		string playlistPath = _fileSystem.GetFilePaths(tempTranscodingPath, false).FirstOrDefault((string i) => string.Equals(Path.GetExtension(i), ".m3u8", StringComparison.OrdinalIgnoreCase) && i.IndexOf(normalizedPlaylistId, StringComparison.OrdinalIgnoreCase) != -1);
		return GetFileResult(path, playlistPath);
	}

	private Task<object> GetFileResult(string path, string playlistPath)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Expected Obj, but got Unknown
		TranscodingJob transcodingJob = ApiEntryPoint.Instance.OnTranscodeBeginRequest(playlistPath, (TranscodingJobType)1);
		return ResultFactory.GetStaticFileResult(Request, new StaticFileResultOptions
		{
			Path = path,
			FileShare = (FileShareMode)3,
			OnComplete = () =>
			{
				if (transcodingJob != null)
				{
					ApiEntryPoint.Instance.OnTranscodeEndRequest(transcodingJob);
				}
			},
			OnError = () =>
			{
				ApiEntryPoint.Instance.OnTranscodeEndRequest(transcodingJob);
			}
		});
	}
}
