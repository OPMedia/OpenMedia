using System;

namespace MediaBrowser.Model.Configuration;

public class UserConfiguration
{
	public string AudioLanguagePreference { get; set; }

	public bool PlayDefaultAudioTrack { get; set; }

	public string SubtitleLanguagePreference { get; set; }

	public bool DisplayMissingEpisodes { get; set; }

	public string[] GroupedFolders { get; set; }

	public SubtitlePlaybackMode SubtitleMode { get; set; }

	public bool DisplayCollectionsView { get; set; }

	public bool EnableLocalPassword { get; set; }

	public string[] OrderedViews { get; set; }

	public string[] LatestItemsExcludes { get; set; }

	public string[] MyMediaExcludes { get; set; }

	public bool HidePlayedInLatest { get; set; }

	public bool RememberAudioSelections { get; set; }

	public bool RememberSubtitleSelections { get; set; }

	public bool EnableNextEpisodeAutoPlay { get; set; }

	public UserConfiguration()
	{
		EnableNextEpisodeAutoPlay = true;
		RememberAudioSelections = true;
		RememberSubtitleSelections = true;
		HidePlayedInLatest = true;
		PlayDefaultAudioTrack = true;
		LatestItemsExcludes = Array.Empty<string>();
		OrderedViews = Array.Empty<string>();
		MyMediaExcludes = Array.Empty<string>();
		GroupedFolders = Array.Empty<string>();
	}
}
