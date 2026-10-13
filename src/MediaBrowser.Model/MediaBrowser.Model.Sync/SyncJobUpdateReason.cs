namespace MediaBrowser.Model.Sync;

public enum SyncJobUpdateReason
{
	SettingsUpdated,
	StatusChange,
	TranscodingProgress,
	TransferProgress
}
