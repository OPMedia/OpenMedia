using System;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Model.Tasks;

public interface ITaskTrigger
{
	TaskOptions TaskOptions { get; set; }

	event EventHandler<EventArgs> Triggered;

	void Start(TaskResult lastResult, ILogger logger, string taskName, bool isApplicationStartup);

	void Stop();
}
