using System;

namespace MediaBrowser.Model.System;

public class LogFile
{
	public DateTime DateCreated { get; set; }

	public DateTime DateModified { get; set; }

	public long Size { get; set; }

	public string Name { get; set; }
}
