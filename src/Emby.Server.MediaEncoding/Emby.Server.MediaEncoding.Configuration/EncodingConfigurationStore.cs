using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;

namespace Emby.Server.MediaEncoding.Configuration;

public class EncodingConfigurationStore : ConfigurationStore, IValidatingConfiguration
{
	private readonly IFileSystem _fileSystem;

	public EncodingConfigurationStore(IFileSystem fileSystem)
	{
		((ConfigurationStore)this).ConfigurationType = typeof(EncodingOptions);
		((ConfigurationStore)this).Key = "encoding";
		_fileSystem = fileSystem;
	}

	public void Validate(object oldConfig, object newConfig)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected Obj, but got Unknown
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		EncodingOptions val = (EncodingOptions)oldConfig;
		string transcodingTempPath = ((EncodingOptions)newConfig).TranscodingTempPath;
		if (!string.IsNullOrWhiteSpace(transcodingTempPath) && !string.Equals(val.TranscodingTempPath ?? string.Empty, transcodingTempPath) && !_fileSystem.DirectoryExists(transcodingTempPath))
		{
			throw new FileNotFoundException($"{transcodingTempPath} does not exist.");
		}
	}
}
