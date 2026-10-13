using System;

namespace MediaBrowser.Controller.Persistence;

public interface IRepository : IDisposable
{
	string Name { get; }
}
