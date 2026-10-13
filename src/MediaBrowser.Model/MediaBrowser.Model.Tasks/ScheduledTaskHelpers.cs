namespace MediaBrowser.Model.Tasks;

public static class ScheduledTaskHelpers
{
	public static TaskInfo GetTaskInfo(IScheduledTaskWorker task)
	{
		bool isHidden = false;
		if (task.ScheduledTask is IConfigurableScheduledTask configurableScheduledTask)
		{
			isHidden = configurableScheduledTask.IsHidden;
		}
		string key = task.ScheduledTask.Key;
		return new TaskInfo
		{
			Name = task.Name,
			CurrentProgressPercentage = task.CurrentProgress,
			State = task.State,
			Id = task.Id,
			LastExecutionResult = task.LastExecutionResult,
			Triggers = task.Triggers,
			Description = task.Description,
			Category = task.Category,
			IsHidden = isHidden,
			Key = key
		};
	}
}
