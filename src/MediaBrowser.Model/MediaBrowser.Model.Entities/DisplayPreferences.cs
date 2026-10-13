using System.Collections.Generic;

namespace MediaBrowser.Model.Entities;

public class DisplayPreferences
{
	private const double ImageScale = 0.9;

	public string Id { get; set; }

	public string ViewType { get; set; }

	public string SortBy { get; set; }

	public string IndexBy { get; set; }

	public bool RememberIndexing { get; set; }

	public int PrimaryImageHeight { get; set; }

	public int PrimaryImageWidth { get; set; }

	public Dictionary<string, string> CustomPrefs { get; set; }

	public ScrollDirection ScrollDirection { get; set; }

	public bool ShowBackdrop { get; set; }

	public bool RememberSorting { get; set; }

	public SortOrder SortOrder { get; set; }

	public bool ShowSidebar { get; set; }

	public string Client { get; set; }

	public DisplayPreferences()
	{
		RememberIndexing = false;
		PrimaryImageHeight = 250;
		PrimaryImageWidth = 250;
		ShowBackdrop = true;
		CustomPrefs = new Dictionary<string, string>();
	}
}
