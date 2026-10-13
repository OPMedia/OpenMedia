using System;

namespace MediaBrowser.Model.IO;

public interface IIsoMount : IDisposable
{
	string IsoPath { get; }

	string MountedPath { get; }
}
