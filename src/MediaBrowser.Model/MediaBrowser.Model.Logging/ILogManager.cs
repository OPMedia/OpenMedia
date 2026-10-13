using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Model.Logging;

public interface ILogManager
{
	LogSeverity LogSeverity { get; set; }

	string ExceptionMessagePrefix { get; set; }

	event EventHandler LoggerLoaded;

	ILogger GetLogger(string name);

	Task ReloadLogger(LogSeverity severity, CancellationToken cancellationToken);

	void Flush();

	void AddConsoleOutput();

	void RemoveConsoleOutput();
}
