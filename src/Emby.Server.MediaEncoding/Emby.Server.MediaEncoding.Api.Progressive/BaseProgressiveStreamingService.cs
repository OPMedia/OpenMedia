using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.System;

namespace Emby.Server.MediaEncoding.Api.Progressive;

public abstract class BaseProgressiveStreamingService : BaseStreamingService
{
	protected readonly IImageProcessor ImageProcessor;

	protected readonly IEnvironmentInfo EnvironmentInfo;

	protected readonly IHttpClient HttpClient;

	protected override TranscodingJobType TranscodingJobType => (TranscodingJobType)0;

	public BaseProgressiveStreamingService(IServerConfigurationManager serverConfig, IHttpClient httpClient, IUserManager userManager, ILibraryManager libraryManager, IIsoManager isoManager, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IDlnaManager dlnaManager, ISubtitleEncoder subtitleEncoder, IDeviceManager deviceManager, IMediaSourceManager mediaSourceManager, IZipClient zipClient, IJsonSerializer jsonSerializer, IAuthorizationContext authorizationContext, IImageProcessor imageProcessor, IEnvironmentInfo environmentInfo, IServerApplicationHost appHost, IAssemblyInfo assemblyInfo)
		: base(serverConfig, userManager, libraryManager, isoManager, mediaEncoder, fileSystem, dlnaManager, subtitleEncoder, deviceManager, mediaSourceManager, zipClient, jsonSerializer, authorizationContext, appHost, assemblyInfo)
	{
		ImageProcessor = imageProcessor;
		EnvironmentInfo = environmentInfo;
		HttpClient = httpClient;
	}

	protected override string GetOutputFileExtension(StreamState state)
	{
		string outputFileExtension = base.GetOutputFileExtension(state);
		if (!string.IsNullOrEmpty(outputFileExtension))
		{
			return outputFileExtension;
		}
		bool isVideoRequest = ((EncodingJobInfo)state).IsVideoRequest;
		if (isVideoRequest)
		{
			string videoCodec = ((BaseEncodingJobOptions)state.VideoRequest).VideoCodec;
			if (string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase))
			{
				return ".ts";
			}
			if (string.Equals(videoCodec, "theora", StringComparison.OrdinalIgnoreCase))
			{
				return ".ogv";
			}
			if (string.Equals(videoCodec, "vpx", StringComparison.OrdinalIgnoreCase))
			{
				return ".webm";
			}
			if (string.Equals(videoCodec, "wmv", StringComparison.OrdinalIgnoreCase))
			{
				return ".asf";
			}
		}
		if (!isVideoRequest)
		{
			string audioCodec = ((BaseEncodingJobOptions)state.Request).AudioCodec;
			if (string.Equals("aac", audioCodec, StringComparison.OrdinalIgnoreCase))
			{
				return ".aac";
			}
			if (string.Equals("mp3", audioCodec, StringComparison.OrdinalIgnoreCase))
			{
				return ".mp3";
			}
			if (string.Equals("vorbis", audioCodec, StringComparison.OrdinalIgnoreCase))
			{
				return ".ogg";
			}
			if (string.Equals("wma", audioCodec, StringComparison.OrdinalIgnoreCase))
			{
				return ".wma";
			}
		}
		return null;
	}

	protected async Task<object> ProcessRequest(StreamRequest request, bool isHeadRequest)
	{
		CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		StreamState state = await GetState(request, cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
		Dictionary<string, string> responseHeaders = new Dictionary<string, string>();
		if (((BaseEncodingJobOptions)request).Static && state.DirectStreamProvider != null)
		{
			AddDlnaHeaders(state, responseHeaders, isStaticallyStreamed: true);
			using (state)
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				dictionary["Content-Type"] = MimeTypes.GetMimeType("file.ts");
				return new ProgressiveFileCopier(state.DirectStreamProvider, dictionary, null, Logger, EnvironmentInfo)
				{
					AllowEndOfFile = false
				};
			}
		}
		if (((BaseEncodingJobOptions)request).Static && (int)((EncodingJobInfo)state).InputProtocol == 1)
		{
			AddDlnaHeaders(state, responseHeaders, isStaticallyStreamed: true);
			using (state)
			{
				return await GetStaticRemoteStreamResult(state, responseHeaders, isHeadRequest, cancellationTokenSource).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		if (((BaseEncodingJobOptions)request).Static && (int)((EncodingJobInfo)state).InputProtocol != 0)
		{
			throw new ArgumentException($"Input protocol {((EncodingJobInfo)state).InputProtocol} cannot be streamed statically.");
		}
		string outputFilePath = ((EncodingJobInfo)state).OutputFilePath;
		bool flag = FileSystem.FileExists(outputFilePath);
		TranscodingJob transcodingJob = ApiEntryPoint.Instance.GetTranscodingJob(outputFilePath, (TranscodingJobType)0);
		bool flag2 = flag && transcodingJob != null;
		AddDlnaHeaders(state, responseHeaders, ((BaseEncodingJobOptions)request).Static | flag2);
		if (((BaseEncodingJobOptions)request).Static)
		{
			string text = ((EncodingJobInfo)state).GetMimeType("." + ((EncodingJobInfo)state).OutputContainer, false) ?? ((EncodingJobInfo)state).GetMimeType(((EncodingJobInfo)state).MediaPath, true);
			using (state)
			{
				if (((EncodingJobInfo)state).MediaSource.IsInfiniteStream)
				{
					Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					dictionary2["Content-Type"] = text;
					return new ProgressiveFileCopier(FileSystem, ((EncodingJobInfo)state).MediaPath, dictionary2, null, Logger, EnvironmentInfo)
					{
						AllowEndOfFile = false
					};
				}
				TimeSpan? cacheDuration = null;
				if (!string.IsNullOrEmpty(request.Tag))
				{
					cacheDuration = TimeSpan.FromDays(365.0);
				}
				return await ResultFactory.GetStaticFileResult(Request, new StaticFileResultOptions
				{
					ResponseHeaders = responseHeaders,
					ContentType = text,
					IsHeadRequest = isHeadRequest,
					Path = ((EncodingJobInfo)state).MediaPath,
					CacheDuration = cacheDuration
				}).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		try
		{
			return await GetStreamResult(state, responseHeaders, isHeadRequest, cancellationTokenSource).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
			((EncodingJobInfo)state).Dispose();
			throw;
		}
	}

	private async Task<object> GetStaticRemoteStreamResult(StreamState state, Dictionary<string, string> responseHeaders, bool isHeadRequest, CancellationTokenSource cancellationTokenSource)
	{
		string value = null;
		((EncodingJobInfo)state).RemoteHttpHeaders.TryGetValue("User-Agent", out value);
		HttpRequestOptions val = new HttpRequestOptions
		{
			Url = ((EncodingJobInfo)state).MediaPath,
			UserAgent = value,
			BufferContent = false,
			CancellationToken = cancellationTokenSource.Token
		};
		string[] array = new string[1] { "Range" };
		foreach (string text in array)
		{
			string value2 = Request.Headers.Get(text);
			if (!string.IsNullOrWhiteSpace(value2))
			{
				val.RequestHeaders[text] = value2;
			}
		}
		HttpResponseInfo response = await HttpClient.GetResponse(val).ConfigureAwait(continueOnCapturedContext: false);
		long? contentLength = null;
		string value3 = null;
		if (response.Headers.TryGetValue("Content-Range", out value3))
		{
			string[] array2 = value3.Split('/');
			if (array2.Length == 2 && long.TryParse(array2[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
			{
				contentLength = result;
			}
		}
		if (!contentLength.HasValue && response.ContentLength.HasValue && response.ContentLength.Value >= 0)
		{
			contentLength = response.ContentLength;
		}
		object result2 = await ResultFactory.GetStaticResult(Request, new StaticResultOptions
		{
			ContentFactory = () => Task.FromResult(response.Content),
			ResponseHeaders = responseHeaders,
			IsHeadRequest = isHeadRequest,
			ContentType = response.ContentType,
			ContentLength = contentLength
		}).ConfigureAwait(continueOnCapturedContext: false);
		if (isHeadRequest)
		{
			HttpResponseInfo val2 = response;
			try
			{
				return result2;
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		return result2;
	}

	private async Task<object> GetStreamResult(StreamState state, IDictionary<string, string> responseHeaders, bool isHeadRequest, CancellationTokenSource cancellationTokenSource)
	{
		string outputPath = ((EncodingJobInfo)state).OutputFilePath;
		responseHeaders["Accept-Ranges"] = "none";
		string contentType = ((EncodingJobInfo)state).GetMimeType(outputPath, true);
		long num = ((state.Request.EstimateContentLength | isHeadRequest) ? GetEstimatedContentLength(state) : 0);
		if (num > 0)
		{
			responseHeaders["Content-Length"] = num.ToString(UsCulture);
		}
		if (isHeadRequest)
		{
			object result = ResultFactory.GetResult(Request, Array.Empty<byte>(), contentType, responseHeaders);
			IHasHeaders val = (IHasHeaders)((result is IHasHeaders) ? result : null);
			if (val != null)
			{
				if (num > 0)
				{
					val.Headers["Content-Length"] = num.ToString(CultureInfo.InvariantCulture);
				}
				else if (val.Headers.ContainsKey("Content-Length"))
				{
					val.Headers.Remove("Content-Length");
				}
			}
			return result;
		}
		SemaphoreSlim transcodingLock = ApiEntryPoint.Instance.GetTranscodingLock(outputPath);
		await transcodingLock.WaitAsync(cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			TranscodingJob job;
			if (!FileSystem.FileExists(outputPath))
			{
				job = await StartFfMpeg(state, outputPath, ApiEntryPoint.Instance.GetEncodingOptions(), cancellationTokenSource).ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				job = ApiEntryPoint.Instance.OnTranscodeBeginRequest(outputPath, (TranscodingJobType)0);
				((EncodingJobInfo)state).Dispose();
			}
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			dictionary["Content-Type"] = contentType;
			foreach (KeyValuePair<string, string> responseHeader in responseHeaders)
			{
				dictionary[responseHeader.Key] = responseHeader.Value;
			}
			return new ProgressiveFileCopier(FileSystem, outputPath, dictionary, job, Logger, EnvironmentInfo);
		}
		finally
		{
			transcodingLock.Release();
		}
	}

	private long GetEstimatedContentLength(StreamState state)
	{
		int num = ((EncodingJobInfo)state).TotalOutputBitrate ?? 0;
		if (num > 0 && ((EncodingJobInfo)state).RunTimeTicks.HasValue)
		{
			return Convert.ToInt64((double)num * TimeSpan.FromTicks(((EncodingJobInfo)state).RunTimeTicks.Value).TotalSeconds / 8.0);
		}
		return 0L;
	}
}
