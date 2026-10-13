using System;

namespace MediaBrowser.Common.Configuration;

public class ConfigurationUpdateEventArgs : EventArgs
{
	public string Key { get; set; }

	public object NewConfiguration { get; set; }
}
