using System;

namespace MediaBrowser.Controller.Plugins;

public interface IServerEntryPoint : IDisposable
{
	void Run();
}
