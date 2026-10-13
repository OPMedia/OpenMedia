using System;

namespace MediaBrowser.Controller.Library;

public interface ILibraryMonitor : IDisposable
{
	void Start();

	void Stop();

	void ReportFileSystemChangeBeginning(string path);

	void ReportFileSystemChangeComplete(string path, bool refreshPath);

	void ReportFileSystemChanged(string path);

	bool IsPathLocked(string path);
}
