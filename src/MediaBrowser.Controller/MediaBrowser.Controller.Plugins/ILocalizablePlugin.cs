using System.IO;

namespace MediaBrowser.Controller.Plugins;

public interface ILocalizablePlugin
{
	Stream GetDictionary(string culture);
}
