using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.IO;

namespace Emby.Server.MediaEncoding.Configuration;

public class EncodingConfigurationFactory : IConfigurationFactory
{
	private readonly IFileSystem _fileSystem;

	public EncodingConfigurationFactory(IFileSystem fileSystem)
	{
		_fileSystem = fileSystem;
	}

	public IEnumerable<ConfigurationStore> GetConfigurations()
	{
		return (IEnumerable<ConfigurationStore>)(object)new EncodingConfigurationStore[1]
		{
			new EncodingConfigurationStore(_fileSystem)
		};
	}
}
