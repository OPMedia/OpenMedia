using System;

namespace MediaBrowser.Model.Dto;

public class GameSystemSummary
{
	public string Name { get; set; }

	public string DisplayName { get; set; }

	public int GameCount { get; set; }

	public string[] GameFileExtensions { get; set; }

	public int ClientInstalledGameCount { get; set; }

	public GameSystemSummary()
	{
		GameFileExtensions = Array.Empty<string>();
	}
}
