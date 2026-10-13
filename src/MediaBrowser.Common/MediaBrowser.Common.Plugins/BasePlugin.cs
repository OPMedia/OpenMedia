using System;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Common.Plugins;

public abstract class BasePlugin : IPlugin, IPluginAssembly
{
	public abstract string Name { get; }

	public virtual string Description => string.Empty;

	public virtual Guid Id { get; private set; }

	public Version Version { get; private set; }

	public string AssemblyFilePath { get; private set; }

	public string DataFolderPath { get; private set; }

	public virtual PluginInfo GetPluginInfo()
	{
		return new PluginInfo
		{
			Name = Name,
			Version = Version.ToString(),
			Description = Description,
			Id = Id.ToString()
		};
	}

	public virtual void OnUninstalling()
	{
	}

	public void SetAttributes(string assemblyFilePath, string dataFolderPath, Version assemblyVersion)
	{
		AssemblyFilePath = assemblyFilePath;
		DataFolderPath = dataFolderPath;
		Version = assemblyVersion;
	}

	public void SetId(Guid assemblyId)
	{
		Id = assemblyId;
	}
}
public abstract class BasePlugin<TConfigurationType> : BasePlugin, IHasPluginConfiguration where TConfigurationType : BasePluginConfiguration
{
	private Action<string> _directoryCreateFn;

	private readonly object _configurationSyncLock = new object();

	private TConfigurationType _configuration;

	private readonly object _configurationSaveLock = new object();

	protected IApplicationPaths ApplicationPaths { get; private set; }

	protected IXmlSerializer XmlSerializer { get; private set; }

	public bool IsFirstRun { get; private set; }

	public Type ConfigurationType => typeof(TConfigurationType);

	protected string AssemblyFileName => Path.GetFileName(AssemblyFilePath);

	public TConfigurationType Configuration
	{
		get
		{
			if (_configuration == null)
			{
				lock (_configurationSyncLock)
				{
					if (_configuration == null)
					{
						_configuration = LoadConfiguration();
					}
				}
			}
			return _configuration;
		}
		protected set
		{
			_configuration = value;
		}
	}

	public virtual string ConfigurationFileName => Path.ChangeExtension(AssemblyFileName, ".xml");

	public string ConfigurationFilePath => Path.Combine(ApplicationPaths.PluginConfigurationsPath, ConfigurationFileName);

	BasePluginConfiguration IHasPluginConfiguration.Configuration => Configuration;

	public void SetStartupInfo(Action<string> directoryCreateFn)
	{
		_directoryCreateFn = directoryCreateFn;
	}

	private TConfigurationType LoadConfiguration()
	{
		string configurationFilePath = ConfigurationFilePath;
		try
		{
			return (TConfigurationType)XmlSerializer.DeserializeFromFile(typeof(TConfigurationType), configurationFilePath);
		}
		catch
		{
			return (TConfigurationType)Activator.CreateInstance(typeof(TConfigurationType));
		}
	}

	protected BasePlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
	{
		ApplicationPaths = applicationPaths;
		XmlSerializer = xmlSerializer;
	}

	public virtual void SaveConfiguration()
	{
		lock (_configurationSaveLock)
		{
			_directoryCreateFn(Path.GetDirectoryName(ConfigurationFilePath));
			XmlSerializer.SerializeToFile(Configuration, ConfigurationFilePath);
		}
	}

	public virtual void UpdateConfiguration(BasePluginConfiguration configuration)
	{
		if (configuration == null)
		{
			throw new ArgumentNullException("configuration");
		}
		Configuration = (TConfigurationType)configuration;
		SaveConfiguration();
	}

	public override PluginInfo GetPluginInfo()
	{
		PluginInfo pluginInfo = base.GetPluginInfo();
		pluginInfo.ConfigurationFileName = ConfigurationFileName;
		return pluginInfo;
	}
}
