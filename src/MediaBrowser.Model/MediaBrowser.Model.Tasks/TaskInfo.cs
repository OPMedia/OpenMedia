namespace MediaBrowser.Model.Tasks;

public class TaskInfo
{
	public string Name { get; set; }

	public TaskState State { get; set; }

	public double? CurrentProgressPercentage { get; set; }

	public string Id { get; set; }

	public TaskResult LastExecutionResult { get; set; }

	public TaskTriggerInfo[] Triggers { get; set; }

	public string Description { get; set; }

	public string Category { get; set; }

	public bool IsHidden { get; set; }

	public string Key { get; set; }

	public TaskInfo()
	{
		Triggers = new TaskTriggerInfo[0];
	}
}
