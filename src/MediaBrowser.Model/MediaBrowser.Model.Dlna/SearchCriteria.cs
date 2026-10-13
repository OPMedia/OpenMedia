using System.Text.RegularExpressions;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Model.Dlna;

public class SearchCriteria
{
	public SearchType SearchType { get; set; }

	private string[] RegexSplit(string str, string term, int limit)
	{
		return new Regex(term).Split(str, limit);
	}

	private string[] RegexSplit(string str, string term)
	{
		return Regex.Split(str, term, RegexOptions.IgnoreCase);
	}

	public SearchCriteria(string search)
	{
		SearchType = SearchType.Unknown;
		string[] array = RegexSplit(search, "(and|or)");
		foreach (string text in array)
		{
			string[] array2 = RegexSplit(text.Trim().Trim(new char[1] { '(' }).Trim(new char[1] { ')' })
				.Trim(), "\\s", 3);
			if (array2.Length == 3 && StringHelper.EqualsIgnoreCase("upnp:class", array2[0]) && (StringHelper.EqualsIgnoreCase("=", array2[1]) || StringHelper.EqualsIgnoreCase("derivedfrom", array2[1])))
			{
				if (StringHelper.EqualsIgnoreCase("\"object.item.imageItem\"", array2[2]) || StringHelper.EqualsIgnoreCase("\"object.item.imageItem.photo\"", array2[2]))
				{
					SearchType = SearchType.Image;
				}
				else if (StringHelper.EqualsIgnoreCase("\"object.item.videoItem\"", array2[2]))
				{
					SearchType = SearchType.Video;
				}
				else if (StringHelper.EqualsIgnoreCase("\"object.container.playlistContainer\"", array2[2]))
				{
					SearchType = SearchType.Playlist;
				}
				else if (StringHelper.EqualsIgnoreCase("\"object.container.album.musicAlbum\"", array2[2]))
				{
					SearchType = SearchType.MusicAlbum;
				}
			}
		}
	}
}
