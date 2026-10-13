using System;
using MediaBrowser.Common;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;

namespace Emby.Server.MediaEncoding.Encoder;

public class VideoEncoder : BaseEncoder
{
	protected override bool IsVideoEncoder => true;

	public VideoEncoder(MediaEncoder mediaEncoder, ILogger logger, IServerConfigurationManager configurationManager, IFileSystem fileSystem, IIsoManager isoManager, ILibraryManager libraryManager, ISessionManager sessionManager, ISubtitleEncoder subtitleEncoder, IMediaSourceManager mediaSourceManager, IProcessFactory processFactory, IAssemblyInfo assemblyInfo, IJsonSerializer json, IApplicationHost appHost)
		: base(mediaEncoder, logger, configurationManager, fileSystem, isoManager, libraryManager, sessionManager, subtitleEncoder, mediaSourceManager, processFactory, assemblyInfo, json, appHost)
	{
	}

	protected override string GetCommandLineArguments(EncodingJobInfo state, EncodingOptions encodingOptions)
	{
		return EncodingHelper.GetProgressiveVideoFullCommandLine(state, encodingOptions, state.OutputFilePath, "superfast");
	}

	protected override string GetOutputFileExtension(EncodingJobInfo state)
	{
		string outputFileExtension = base.GetOutputFileExtension(state);
		if (!string.IsNullOrEmpty(outputFileExtension))
		{
			return outputFileExtension;
		}
		string videoCodec = state.BaseRequest.VideoCodec;
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
		return null;
	}
}
