using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MediaBrowser.Model.LiveTv;

namespace MediaBrowser.Controller.LiveTv;

public class LiveTvTunerInfo
{
	public string SourceType { get; set; }

	public string Name { get; set; }

	public string Id { get; set; }

	public string Url { get; set; }

	public LiveTvTunerStatus Status
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public string ChannelId { get; set; }

	public string RecordingId { get; set; }

	public string ProgramName { get; set; }

	public List<string> Clients { get; set; }

	public bool CanReset { get; set; }

	public LiveTvTunerInfo()
	{
		Clients = new List<string>();
	}
}
