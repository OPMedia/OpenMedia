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

public class AudioEncoder : BaseEncoder
{
	protected override bool IsVideoEncoder => false;

	public AudioEncoder(MediaEncoder mediaEncoder, ILogger logger, IServerConfigurationManager configurationManager, IFileSystem fileSystem, IIsoManager isoManager, ILibraryManager libraryManager, ISessionManager sessionManager, ISubtitleEncoder subtitleEncoder, IMediaSourceManager mediaSourceManager, IProcessFactory processFactory, IAssemblyInfo assemblyInfo, IJsonSerializer json, IApplicationHost appHost)
		: base(mediaEncoder, logger, configurationManager, fileSystem, isoManager, libraryManager, sessionManager, subtitleEncoder, mediaSourceManager, processFactory, assemblyInfo, json, appHost)
	{
	}

	protected override string GetCommandLineArguments(EncodingJobInfo state, EncodingOptions encodingOptions)
	{
		return EncodingHelper.GetProgressiveAudioFullCommandLine(state, encodingOptions, state.OutputFilePath);
	}

	protected override string GetOutputFileExtension(EncodingJobInfo state)
	{
		string outputFileExtension = base.GetOutputFileExtension(state);
		if (!string.IsNullOrEmpty(outputFileExtension))
		{
			return outputFileExtension;
		}
		string audioCodec = state.BaseRequest.AudioCodec;
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
		return null;
	}
}
