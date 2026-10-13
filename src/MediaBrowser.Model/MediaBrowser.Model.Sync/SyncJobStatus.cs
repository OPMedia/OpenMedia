namespace MediaBrowser.Model.Sync;

public enum SyncJobStatus
{
	Queued,
	Converting,
	ReadyToTransfer,
	Transferring,
	Completed,
	CompletedWithError,
	Failed
}
