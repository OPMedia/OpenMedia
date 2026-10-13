using System.IO;
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
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.System;

namespace Emby.Server.MediaEncoding.Api.Progressive;

public class VideoService : BaseProgressiveStreamingService
{
	public Task<object> Get(GetVideoStream request)
	{
		return ProcessRequest(request, isHeadRequest: false);
	}

	public Task<object> Get(GetFile request)
	{
		if (LibraryManager.FindByPath(request.Id, (bool?)null) == null)
		{
			throw new FileNotFoundException(request.Id);
		}
		return ResultFactory.GetStaticFileResult(Request, request.Id, (FileShareMode)1);
	}

	public Task<object> Head(GetVideoStream request)
	{
		return ProcessRequest(request, isHeadRequest: true);
	}

	protected override string GetCommandLineArguments(string outputPath, EncodingOptions encodingOptions, StreamState state, bool isEncoding)
	{
		return EncodingHelper.GetProgressiveVideoFullCommandLine((EncodingJobInfo)(object)state, encodingOptions, outputPath, GetDefaultH264Preset());
	}

	public VideoService(IServerConfigurationManager serverConfig, IHttpClient httpClient, IUserManager userManager, ILibraryManager libraryManager, IIsoManager isoManager, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IDlnaManager dlnaManager, ISubtitleEncoder subtitleEncoder, IDeviceManager deviceManager, IMediaSourceManager mediaSourceManager, IZipClient zipClient, IJsonSerializer jsonSerializer, IAuthorizationContext authorizationContext, IImageProcessor imageProcessor, IEnvironmentInfo environmentInfo, IServerApplicationHost appHost, IAssemblyInfo assemblyInfo)
		: base(serverConfig, httpClient, userManager, libraryManager, isoManager, mediaEncoder, fileSystem, dlnaManager, subtitleEncoder, deviceManager, mediaSourceManager, zipClient, jsonSerializer, authorizationContext, imageProcessor, environmentInfo, appHost, assemblyInfo)
	{
	}
}
