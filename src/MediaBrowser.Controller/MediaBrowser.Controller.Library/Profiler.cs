using System;
using System.Diagnostics;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Controller.Library;

public class Profiler : IDisposable
{
	private readonly string _name;

	private readonly Stopwatch _stopwatch;

	private readonly ILogger _logger;

	public Profiler(string name, ILogger logger)
	{
		_name = name;
		_logger = logger;
		_stopwatch = new Stopwatch();
		_stopwatch.Start();
	}

	public void Dispose()
	{
		Dispose(dispose: true);
	}

	protected virtual void Dispose(bool dispose)
	{
		if (dispose)
		{
			_stopwatch.Stop();
			string text = ((_stopwatch.ElapsedMilliseconds <= 300000) ? string.Format("{0} took {1} seconds.", _name, ((float)_stopwatch.ElapsedMilliseconds / 1000f).ToString("#0.000")) : string.Format("{0} took {1} minutes.", _name, ((float)_stopwatch.ElapsedMilliseconds / 60000f).ToString("F")));
			_logger.Info(text, Array.Empty<object>());
		}
	}
}
