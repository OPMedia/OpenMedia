using System;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Model.Activity;

public class ActivityLogEntry
{
	public long Id { get; set; }

	public string Name { get; set; }

	public string Overview { get; set; }

	public string ShortOverview { get; set; }

	public string Type { get; set; }

	public string ItemId { get; set; }

	public DateTime Date { get; set; }

	public Guid UserId { get; set; }

	public string UserPrimaryImageTag { get; set; }

	public LogSeverity Severity { get; set; }
}
