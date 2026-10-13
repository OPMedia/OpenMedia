using System;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.Model.Users;

public class UserPolicy
{
	public bool IsAdministrator { get; set; }

	public bool IsHidden { get; set; }

	public bool IsDisabled { get; set; }

	public int? MaxParentalRating { get; set; }

	public string[] BlockedTags { get; set; }

	public bool EnableUserPreferenceAccess { get; set; }

	public AccessSchedule[] AccessSchedules { get; set; }

	public UnratedItem[] BlockUnratedItems { get; set; }

	public bool EnableRemoteControlOfOtherUsers { get; set; }

	public bool EnableSharedDeviceControl { get; set; }

	public bool EnableRemoteAccess { get; set; }

	public bool EnableLiveTvManagement { get; set; }

	public bool EnableLiveTvAccess { get; set; }

	public bool EnableMediaPlayback { get; set; }

	public bool EnableAudioPlaybackTranscoding { get; set; }

	public bool EnableVideoPlaybackTranscoding { get; set; }

	public bool EnablePlaybackRemuxing { get; set; }

	public bool EnableContentDeletion { get; set; }

	public string[] EnableContentDeletionFromFolders { get; set; }

	public bool EnableContentDownloading { get; set; }

	public bool EnableSyncTranscoding { get; set; }

	public bool EnableMediaConversion { get; set; }

	public string[] EnabledDevices { get; set; }

	public bool EnableAllDevices { get; set; }

	public string[] EnabledChannels { get; set; }

	public bool EnableAllChannels { get; set; }

	public string[] EnabledFolders { get; set; }

	public bool EnableAllFolders { get; set; }

	public int InvalidLoginAttemptCount { get; set; }

	public bool EnablePublicSharing { get; set; }

	public string[] BlockedMediaFolders { get; set; }

	public string[] BlockedChannels { get; set; }

	public int RemoteClientBitrateLimit { get; set; }

	public string AuthenticationProviderId { get; set; }

	public UserPolicy()
	{
		EnableContentDeletion = true;
		EnableContentDeletionFromFolders = Array.Empty<string>();
		EnableSyncTranscoding = true;
		EnableMediaConversion = true;
		EnableMediaPlayback = true;
		EnableAudioPlaybackTranscoding = true;
		EnableVideoPlaybackTranscoding = true;
		EnablePlaybackRemuxing = true;
		EnableLiveTvManagement = true;
		EnableLiveTvAccess = true;
		EnableLiveTvManagement = true;
		EnableSharedDeviceControl = true;
		BlockedTags = Array.Empty<string>();
		BlockUnratedItems = new UnratedItem[0];
		EnableUserPreferenceAccess = true;
		AccessSchedules = new AccessSchedule[0];
		EnableAllChannels = true;
		EnabledChannels = Array.Empty<string>();
		EnableAllFolders = true;
		EnabledFolders = Array.Empty<string>();
		EnabledDevices = Array.Empty<string>();
		EnableAllDevices = true;
		EnableContentDownloading = true;
		EnablePublicSharing = true;
		EnableRemoteAccess = true;
	}
}
