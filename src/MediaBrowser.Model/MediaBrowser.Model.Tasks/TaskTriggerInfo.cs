using System;

namespace MediaBrowser.Model.Tasks;

public class TaskTriggerInfo
{
	public const string TriggerDaily = "DailyTrigger";

	public const string TriggerWeekly = "WeeklyTrigger";

	public const string TriggerInterval = "IntervalTrigger";

	public const string TriggerSystemEvent = "SystemEventTrigger";

	public const string TriggerStartup = "StartupTrigger";

	public string Type { get; set; }

	public long? TimeOfDayTicks { get; set; }

	public long? IntervalTicks { get; set; }

	public SystemEvent? SystemEvent { get; set; }

	public DayOfWeek? DayOfWeek { get; set; }

	public long? MaxRuntimeTicks { get; set; }
}
