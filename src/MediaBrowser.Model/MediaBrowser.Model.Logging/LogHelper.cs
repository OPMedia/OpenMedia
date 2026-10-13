using System;
using System.Text;

namespace MediaBrowser.Model.Logging;

public static class LogHelper
{
	public static StringBuilder GetLogMessage(Exception exception)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(exception.ToString());
		stringBuilder.AppendLine(exception.GetType().FullName);
		LogExceptionData(stringBuilder, exception);
		stringBuilder.AppendLine(exception.StackTrace ?? "No Stack Trace Available");
		AppendInnerExceptions(stringBuilder, exception);
		stringBuilder.AppendLine(string.Empty);
		return stringBuilder;
	}

	private static void AppendInnerExceptions(StringBuilder messageText, Exception e)
	{
		if (e is AggregateException { InnerExceptions: not null } ex)
		{
			{
				foreach (Exception innerException in ex.InnerExceptions)
				{
					AppendInnerException(messageText, innerException);
					AppendInnerExceptions(messageText, innerException);
				}
				return;
			}
		}
		if (e.InnerException != null)
		{
			AppendInnerException(messageText, e.InnerException);
			AppendInnerExceptions(messageText, e.InnerException);
		}
	}

	private static void AppendInnerException(StringBuilder messageText, Exception e)
	{
		messageText.AppendLine("InnerException: " + e.GetType().FullName);
		messageText.AppendLine(e.ToString());
		LogExceptionData(messageText, e);
		if (e.StackTrace != null)
		{
			messageText.AppendLine(e.StackTrace);
		}
	}

	private static void LogExceptionData(StringBuilder messageText, Exception e)
	{
		foreach (object key in e.Data.Keys)
		{
			messageText.AppendLine(string.Concat(key, ": ", e.Data[key]));
		}
	}
}
