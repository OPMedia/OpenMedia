using System;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Common.Events;

public static class EventHelper
{
	public static void QueueEventIfNotNull(EventHandler handler, object sender, EventArgs args, ILogger logger)
	{
		if (handler == null)
		{
			return;
		}
		// 事件派发是即发即忘语义：显式丢弃 Task，避免未观察告警；
		// 异常已在 lambda 内捕获记录，不会变成未处理异常。
		_ = Task.Run(() =>
		{
			try
			{
				handler(sender, args);
			}
			catch (Exception exception)
			{
				logger.ErrorException("Error in event handler", exception);
			}
		});
	}

	public static void QueueEventIfNotNull<T>(EventHandler<T> handler, object sender, T args, ILogger logger)
	{
		if (handler == null)
		{
			return;
		}
		// 事件派发是即发即忘语义：显式丢弃 Task，避免未观察告警；
		// 异常已在 lambda 内捕获记录，不会变成未处理异常。
		_ = Task.Run(() =>
		{
			try
			{
				handler(sender, args);
			}
			catch (Exception exception)
			{
				logger.ErrorException("Error in event handler", exception);
			}
		});
	}

	public static void FireEventIfNotNull(EventHandler handler, object sender, EventArgs args, ILogger logger)
	{
		if (handler != null)
		{
			try
			{
				handler(sender, args);
			}
			catch (Exception exception)
			{
				logger.ErrorException("Error in event handler", exception);
			}
		}
	}

	public static void FireEventIfNotNull<T>(EventHandler<T> handler, object sender, T args, ILogger logger)
	{
		if (handler != null)
		{
			try
			{
				handler(sender, args);
			}
			catch (Exception exception)
			{
				logger.ErrorException("Error in event handler", exception);
			}
		}
	}
}
