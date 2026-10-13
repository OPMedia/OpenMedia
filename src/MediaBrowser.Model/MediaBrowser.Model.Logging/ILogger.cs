using System;
using System.Text;

namespace MediaBrowser.Model.Logging;

public interface ILogger
{
	void Info(string message, params object[] paramList);

	void Error(string message, params object[] paramList);

	void Warn(string message, params object[] paramList);

	void Debug(string message, params object[] paramList);

	void Fatal(string message, params object[] paramList);

	void FatalException(string message, Exception exception, params object[] paramList);

	void ErrorException(string message, Exception exception, params object[] paramList);

	void LogMultiline(string message, LogSeverity severity, StringBuilder additionalContent);

	void Log(LogSeverity severity, string message, params object[] paramList);
}
