namespace MediaBrowser.Model.Entities;

public enum UserDataSaveReason
{
	PlaybackStart = 1,
	PlaybackProgress,
	PlaybackFinished,
	TogglePlayed,
	UpdateUserRating,
	Import
}
