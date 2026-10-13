using System;
using System.IO;
using System.Threading.Tasks;

namespace MediaBrowser.Model.Diagnostics;

public interface IProcess : IDisposable
{
	int ExitCode { get; }

	StreamWriter StandardInput { get; }

	StreamReader StandardError { get; }

	StreamReader StandardOutput { get; }

	ProcessOptions StartInfo { get; }

	event EventHandler Exited;

	void Kill();

	bool WaitForExit(int timeMs);

	Task<bool> WaitForExitAsync(int timeMs);

	void Start();
}
