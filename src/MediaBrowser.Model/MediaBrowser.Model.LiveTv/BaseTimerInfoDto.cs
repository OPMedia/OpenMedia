using System;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.LiveTv;

public class BaseTimerInfoDto : IHasServerId
{
	public string Id { get; set; }

	public string Type { get; set; }

	public string ServerId { get; set; }

	public string ExternalId { get; set; }

	public Guid ChannelId { get; set; }

	public string ExternalChannelId { get; set; }

	public string ChannelName { get; set; }

	public string ChannelPrimaryImageTag { get; set; }

	public string ProgramId { get; set; }

	public string ExternalProgramId { get; set; }

	public string Name { get; set; }

	public string Overview { get; set; }

	public DateTime StartDate { get; set; }

	public DateTime EndDate { get; set; }

	public string ServiceName { get; set; }

	public int Priority { get; set; }

	public int PrePaddingSeconds { get; set; }

	public int PostPaddingSeconds { get; set; }

	public bool IsPrePaddingRequired { get; set; }

	public string ParentBackdropItemId { get; set; }

	public string[] ParentBackdropImageTags { get; set; }

	public bool IsPostPaddingRequired { get; set; }

	public KeepUntil KeepUntil { get; set; }
}
