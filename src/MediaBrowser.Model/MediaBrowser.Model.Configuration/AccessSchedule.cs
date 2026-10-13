namespace MediaBrowser.Model.Configuration;

public class AccessSchedule
{
	public DynamicDayOfWeek DayOfWeek { get; set; }

	public double StartHour { get; set; }

	public double EndHour { get; set; }
}
