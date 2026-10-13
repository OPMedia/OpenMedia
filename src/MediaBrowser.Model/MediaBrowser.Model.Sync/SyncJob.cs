using System;

namespace MediaBrowser.Model.Sync;

public class SyncJob
{
	public string Id { get; set; }

	public string TargetId { get; set; }

	public string TargetName { get; set; }

	public string Quality { get; set; }

	public int? Bitrate { get; set; }

	public string Profile { get; set; }

	public SyncCategory? Category { get; set; }

	public string ParentId { get; set; }

	public double Progress { get; set; }

	public string Name { get; set; }

	public SyncJobStatus Status { get; set; }

	public string UserId { get; set; }

	public bool UnwatchedOnly { get; set; }

	public bool SyncNewContent { get; set; }

	public int? ItemLimit { get; set; }

	public Guid[] RequestedItemIds { get; set; }

	public DateTime DateCreated { get; set; }

	public DateTime DateLastModified { get; set; }

	public int ItemCount { get; set; }

	public string ParentName { get; set; }

	public string PrimaryImageItemId { get; set; }

	public string PrimaryImageTag { get; set; }

	public SyncJob()
	{
		RequestedItemIds = Array.Empty<Guid>();
	}
}
