using System;
using MediaBrowser.Model.Events;

namespace MediaBrowser.Model.Tasks;

public interface IScheduledTaskWorker : IDisposable
{
	IScheduledTask ScheduledTask { get; }

	TaskResult LastExecutionResult { get; }

	string Name { get; }

	string Description { get; }

	string Category { get; }

	TaskState State { get; }

	double? CurrentProgress { get; }

	TaskTriggerInfo[] Triggers { get; set; }

	string Id { get; }

	event EventHandler<GenericEventArgs<double>> TaskProgress;

	void ReloadTriggerEvents();
}
