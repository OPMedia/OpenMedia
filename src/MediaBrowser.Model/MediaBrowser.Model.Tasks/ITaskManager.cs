using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediaBrowser.Model.Events;

namespace MediaBrowser.Model.Tasks;

public interface ITaskManager : IDisposable
{
	IScheduledTaskWorker[] ScheduledTasks { get; }

	event EventHandler<GenericEventArgs<IScheduledTaskWorker>> TaskExecuting;

	event EventHandler<TaskCompletionEventArgs> TaskCompleted;

	void CancelIfRunningAndQueue<T>(TaskOptions options) where T : IScheduledTask;

	void CancelIfRunningAndQueue<T>() where T : IScheduledTask;

	void CancelIfRunning<T>() where T : IScheduledTask;

	void QueueScheduledTask<T>(TaskOptions options) where T : IScheduledTask;

	void QueueScheduledTask<T>() where T : IScheduledTask;

	void QueueIfNotRunning<T>() where T : IScheduledTask;

	void QueueScheduledTask(IScheduledTask task, TaskOptions options);

	void AddTasks(IEnumerable<IScheduledTask> tasks);

	void Cancel(IScheduledTaskWorker task);

	Task Execute(IScheduledTaskWorker task, TaskOptions options);

	void Execute<T>() where T : IScheduledTask;

	void RunTaskOnNextStartup(string key);
}
