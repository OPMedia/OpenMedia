using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Emby.XmlTv.Entities;

namespace Emby.XmlTv.Classes;

public class XmlTvReader
{
	private readonly string _fileName;

	private readonly string _language;

	public static Regex _regDateWithOffset = new Regex("^(?<dateDigits>[0-9]{4,14})(\\s(?<dateOffset>[+-]*[0-9]{1,4}))?$");

	public XmlTvReader(string fileName, string language = null)
	{
		_fileName = fileName;
		if (string.IsNullOrWhiteSpace(language))
		{
			language = null;
		}
		_language = language;
	}

	private XmlReader CreateXmlTextReader(string path)
	{
		XmlReaderSettings xmlReaderSettings = new XmlReaderSettings();
		xmlReaderSettings.DtdProcessing = DtdProcessing.Ignore;
		xmlReaderSettings.CheckCharacters = false;
		xmlReaderSettings.IgnoreProcessingInstructions = true;
		xmlReaderSettings.IgnoreComments = true;
		return XmlReader.Create(path, xmlReaderSettings);
	}

	public IEnumerable<XmlTvChannel> GetChannels()
	{
		List<XmlTvChannel> list = new List<XmlTvChannel>();
		using (XmlReader xmlReader = CreateXmlTextReader(_fileName))
		{
			if (xmlReader.ReadToDescendant("tv") && xmlReader.ReadToDescendant("channel"))
			{
				do
				{
					XmlTvChannel channel = GetChannel(xmlReader);
					if (channel != null)
					{
						list.Add(channel);
					}
				}
				while (xmlReader.ReadToFollowing("channel"));
			}
		}
		return list;
	}

	private XmlTvChannel GetChannel(XmlReader reader)
	{
		string attribute = reader.GetAttribute("id");
		if (string.IsNullOrEmpty(attribute))
		{
			return null;
		}
		XmlTvChannel result = new XmlTvChannel
		{
			Id = attribute
		};
		using (XmlReader xmlReader = reader.ReadSubtree())
		{
			xmlReader.MoveToContent();
			xmlReader.Read();
			while (!xmlReader.EOF && xmlReader.ReadState == ReadState.Interactive)
			{
				if (xmlReader.NodeType == XmlNodeType.Element)
				{
					switch (xmlReader.Name)
					{
					case "display-name":
						ProcessNode(xmlReader, (string s) =>
						{
							result.DisplayName = s;
						}, _language, (string s) =>
						{
							SetChannelNumber(result, s);
						});
						break;
					case "url":
						result.Url = xmlReader.ReadElementContentAsString();
						break;
					case "icon":
						result.Icon = ProcessIconNode(xmlReader);
						xmlReader.Skip();
						break;
					default:
						xmlReader.Skip();
						break;
					}
				}
				else
				{
					xmlReader.Read();
				}
			}
		}
		if (string.IsNullOrEmpty(result.DisplayName))
		{
			return null;
		}
		return result;
	}

	private void SetChannelNumber(XmlTvChannel channel, string value)
	{
		value = value.Replace("-", ".");
		if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var _))
		{
			channel.Number = value;
		}
	}

	public IEnumerable<XmlTvProgram> GetProgrammes(string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
	{
		List<XmlTvProgram> list = new List<XmlTvProgram>();
		using (XmlReader xmlReader = CreateXmlTextReader(_fileName))
		{
			if (xmlReader.ReadToDescendant("tv") && xmlReader.ReadToDescendant("programme"))
			{
				do
				{
					if (!cancellationToken.IsCancellationRequested)
					{
						XmlTvProgram programme = GetProgramme(xmlReader, channelId, startDateUtc, endDateUtc);
						if (programme != null)
						{
							list.Add(programme);
						}
					}
				}
				while (xmlReader.ReadToFollowing("programme"));
			}
		}
		return list;
	}

	public XmlTvProgram GetProgramme(XmlReader reader, string channelId, DateTime startDateUtc, DateTime endDateUtc)
	{
		XmlTvProgram xmlTvProgram = new XmlTvProgram();
		PopulateHeader(reader, xmlTvProgram);
		using (XmlReader xmlReader = reader.ReadSubtree())
		{
			if (!string.Equals(xmlTvProgram.ChannelId, channelId, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			if (xmlTvProgram.EndDate < startDateUtc || xmlTvProgram.StartDate >= endDateUtc)
			{
				return null;
			}
			xmlReader.MoveToContent();
			xmlReader.Read();
			while (!xmlReader.EOF && xmlReader.ReadState == ReadState.Interactive)
			{
				if (xmlReader.NodeType == XmlNodeType.Element)
				{
					switch (xmlReader.Name)
					{
					case "title":
						ProcessTitleNode(xmlReader, xmlTvProgram);
						break;
					case "category":
						ProcessCategory(xmlReader, xmlTvProgram);
						break;
					case "country":
						ProcessCountry(xmlReader, xmlTvProgram);
						break;
					case "desc":
						ProcessDescription(xmlReader, xmlTvProgram);
						break;
					case "sub-title":
						ProcessSubTitle(xmlReader, xmlTvProgram);
						break;
					case "new":
						ProcessNew(xmlReader, xmlTvProgram);
						break;
					case "previously-shown":
						ProcessPreviouslyShown(xmlReader, xmlTvProgram);
						break;
					case "episode-num":
						ProcessEpisodeNum(xmlReader, xmlTvProgram);
						break;
					case "date":
						ProcessCopyrightDate(xmlReader, xmlTvProgram);
						break;
					case "star-rating":
						ProcessStarRating(xmlReader, xmlTvProgram);
						break;
					case "rating":
						ProcessRating(xmlReader, xmlTvProgram);
						break;
					case "credits":
					{
						if (xmlReader.IsEmptyElement)
						{
							xmlReader.Skip();
							break;
						}
						using (XmlReader creditsXml = xmlReader.ReadSubtree())
						{
							ProcessCredits(creditsXml, xmlTvProgram);
						}
						break;
					}
					case "icon":
						xmlTvProgram.Icon = ProcessIconNode(xmlReader);
						xmlReader.Skip();
						break;
					case "premiere":
						ProcessPremiereNode(xmlReader, xmlTvProgram);
						xmlReader.Skip();
						break;
					default:
						xmlReader.Skip();
						break;
					}
				}
				else
				{
					xmlReader.Read();
				}
			}
		}
		return xmlTvProgram;
	}

	public List<XmlTvLanguage> GetLanguages(CancellationToken cancellationToken)
	{
		Dictionary<string, int> results = new Dictionary<string, int>();
		using (XmlReader xmlReader = CreateXmlTextReader(_fileName))
		{
			while (xmlReader.Read())
			{
				if (cancellationToken.IsCancellationRequested || xmlReader.NodeType != XmlNodeType.Element)
				{
					continue;
				}
				string attribute = xmlReader.GetAttribute("lang");
				if (!string.IsNullOrEmpty(attribute))
				{
					if (!results.ContainsKey(attribute))
					{
						results[attribute] = 0;
					}
					results[attribute]++;
				}
			}
		}
		return (from k in results.Keys
			select new XmlTvLanguage
			{
				Name = k,
				Relevance = results[k]
			} into l
			orderby l.Relevance descending
			select l).ToList();
	}

	private void ProcessCopyrightDate(XmlReader xmlProg, XmlTvProgram result)
	{
		string text = xmlProg.ReadElementContentAsString();
		if (string.IsNullOrEmpty(text))
		{
			result.CopyrightDate = null;
			return;
		}
		DateTime? copyrightDate = ParseDate(text);
		if (copyrightDate.HasValue)
		{
			result.CopyrightDate = copyrightDate;
		}
	}

	public void ProcessCredits(XmlReader creditsXml, XmlTvProgram result)
	{
		creditsXml.MoveToContent();
		creditsXml.Read();
		while (!creditsXml.EOF && creditsXml.ReadState == ReadState.Interactive)
		{
			if (creditsXml.NodeType == XmlNodeType.Element)
			{
				XmlTvCredit xmlTvCredit = null;
				switch (creditsXml.Name)
				{
				case "director":
					xmlTvCredit = new XmlTvCredit
					{
						Type = XmlTvCreditType.Director
					};
					break;
				case "actor":
					xmlTvCredit = new XmlTvCredit
					{
						Type = XmlTvCreditType.Actor
					};
					break;
				case "writer":
					xmlTvCredit = new XmlTvCredit
					{
						Type = XmlTvCreditType.Writer
					};
					break;
				case "adapter":
					xmlTvCredit = new XmlTvCredit
					{
						Type = XmlTvCreditType.Adapter
					};
					break;
				case "producer":
					xmlTvCredit = new XmlTvCredit
					{
						Type = XmlTvCreditType.Producer
					};
					break;
				case "composer":
					xmlTvCredit = new XmlTvCredit
					{
						Type = XmlTvCreditType.Composer
					};
					break;
				case "editor":
					xmlTvCredit = new XmlTvCredit
					{
						Type = XmlTvCreditType.Editor
					};
					break;
				case "presenter":
					xmlTvCredit = new XmlTvCredit
					{
						Type = XmlTvCreditType.Presenter
					};
					break;
				case "commentator":
					xmlTvCredit = new XmlTvCredit
					{
						Type = XmlTvCreditType.Commentator
					};
					break;
				case "guest":
					xmlTvCredit = new XmlTvCredit
					{
						Type = XmlTvCreditType.Guest
					};
					break;
				}
				if (xmlTvCredit != null)
				{
					xmlTvCredit.Name = creditsXml.ReadElementContentAsString();
					result.Credits.Add(xmlTvCredit);
				}
				else
				{
					creditsXml.Skip();
				}
			}
			else
			{
				creditsXml.Read();
			}
		}
	}

	public void ProcessStarRating(XmlReader reader, XmlTvProgram result)
	{
		reader.ReadToDescendant("value");
		if (reader.Name == "value")
		{
			string text = reader.ReadElementContentAsString();
			if (text.Contains("/") && float.TryParse(text.Split('/')[0], out var result2))
			{
				result.StarRating = result2;
			}
		}
		else
		{
			reader.Skip();
		}
	}

	public void ProcessRating(XmlReader reader, XmlTvProgram result)
	{
		string attribute = reader.GetAttribute("system");
		reader.ReadToDescendant("value");
		if (reader.Name == "value")
		{
			result.Rating = new XmlTvRating
			{
				System = attribute,
				Value = reader.ReadElementContentAsString()
			};
		}
		else
		{
			reader.Skip();
		}
	}

	public void ProcessEpisodeNum(XmlReader reader, XmlTvProgram result)
	{
		switch (reader.GetAttribute("system"))
		{
		case "dd_progid":
			ParseEpisodeDataForProgramId(reader, result);
			break;
		case "icetv":
			result.ProviderIds["icetv"] = reader.ReadElementContentAsString();
			break;
		case "xmltv_ns":
			ParseEpisodeDataForXmlTvNs(reader, result);
			break;
		case "onscreen":
			ParseEpisodeDataForOnScreen(reader, result);
			break;
		case "thetvdb.com":
			ParseTvdbSystem(reader, result);
			break;
		case "imdb.com":
			ParseImdbSystem(reader, result);
			break;
		case "themoviedb.org":
			ParseMovieDbSystem(reader, result);
			break;
		case "SxxExx":
			reader.Skip();
			break;
		default:
			reader.Skip();
			break;
		}
	}

	public void ParseMovieDbSystem(XmlReader reader, XmlTvProgram result)
	{
		string[] array = reader.ReadElementContentAsString().Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries);
		if (string.Equals(array[0], "series", StringComparison.OrdinalIgnoreCase))
		{
			result.SeriesProviderIds["tmdb"] = array[1];
		}
		else if (array.Length == 1 || string.Equals(array[0], "episode", StringComparison.OrdinalIgnoreCase))
		{
			result.ProviderIds["tmdb"] = array.Last();
		}
	}

	public void ParseImdbSystem(XmlReader reader, XmlTvProgram result)
	{
		string text = reader.ReadElementContentAsString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		string[] array = text.Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 2)
		{
			if (string.Equals(array[0], "series", StringComparison.OrdinalIgnoreCase))
			{
				result.SeriesProviderIds["imdb"] = array[1];
			}
			else if (string.Equals(array[0], "episode", StringComparison.OrdinalIgnoreCase))
			{
				result.ProviderIds["imdb"] = array[1];
			}
		}
	}

	public void ParseTvdbSystem(XmlReader reader, XmlTvProgram result)
	{
		string[] array = reader.ReadElementContentAsString().Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 2)
		{
			if (string.Equals(array[0], "series", StringComparison.OrdinalIgnoreCase))
			{
				result.SeriesProviderIds["tvdb"] = array[1];
			}
			else if (string.Equals(array[0], "episode", StringComparison.OrdinalIgnoreCase))
			{
				result.ProviderIds["tvdb"] = array[1];
			}
		}
	}

	public void ParseEpisodeDataForOnScreen(XmlReader reader, XmlTvProgram result)
	{
		reader.ReadElementContentAsString().Replace(" ", "").IndexOf("#", StringComparison.Ordinal);
		_ = -1;
	}

	public void ParseEpisodeDataForProgramId(XmlReader reader, XmlTvProgram result)
	{
		string text = reader.ReadElementContentAsString();
		if (!string.IsNullOrWhiteSpace(text))
		{
			result.ProgramId = text;
		}
	}

	public void ParseEpisodeDataForXmlTvNs(XmlReader reader, XmlTvProgram result)
	{
		string[] array = reader.ReadElementContentAsString().Replace(" ", "").Split(new string[1] { "." }, StringSplitOptions.None);
		int result2;
		if (!string.IsNullOrEmpty(array[0]))
		{
			string[] array2 = array[0].Split(new string[1] { "/" }, StringSplitOptions.None);
			if (int.TryParse(array2[0], out result2))
			{
				result.Episode.Series = result2 + 1;
				if (array2.Length == 2 && int.TryParse(array2[1], out result2))
				{
					result.Episode.SeriesCount = result2;
				}
			}
		}
		if (array.Length >= 2 && !string.IsNullOrEmpty(array[1]))
		{
			string[] array3 = array[1].Split(new string[1] { "/" }, StringSplitOptions.None);
			if (int.TryParse(array3[0], out result2))
			{
				result.Episode.Episode = result2 + 1;
				if (array3.Count() == 2 && int.TryParse(array3[1], out result2))
				{
					result.Episode.EpisodeCount = result2;
				}
			}
		}
		if (array.Length < 3 || string.IsNullOrEmpty(array[2]))
		{
			return;
		}
		string[] array4 = array[2].Split(new string[1] { "/" }, StringSplitOptions.None);
		if (int.TryParse(array4[0], out result2))
		{
			result.Episode.Part = result2 + 1;
			if (array4.Count() == 2 && int.TryParse(array4[1], out result2))
			{
				result.Episode.PartCount = result2;
			}
		}
	}

	public void ProcessPreviouslyShown(XmlReader reader, XmlTvProgram result)
	{
		string attribute = reader.GetAttribute("start");
		if (!string.IsNullOrEmpty(attribute))
		{
			result.PreviouslyShown = ParseDate(attribute);
			if (result.PreviouslyShown != result.StartDate)
			{
				result.IsPreviouslyShown = true;
			}
		}
		else
		{
			result.IsPreviouslyShown = true;
		}
		reader.Skip();
	}

	public void ProcessNew(XmlReader reader, XmlTvProgram result)
	{
		result.IsNew = true;
		reader.Skip();
	}

	public void ProcessCategory(XmlReader reader, XmlTvProgram result)
	{
		result.Categories = result.Categories ?? new List<string>();
		ProcessMultipleNodes(reader, (string s) =>
		{
			result.Categories.Add(s);
		}, _language);
	}

	public void ProcessCountry(XmlReader reader, XmlTvProgram result)
	{
		result.Countries = result.Countries ?? new List<string>();
		ProcessNode(reader, (string s) =>
		{
			result.Countries.Add(s);
		}, _language);
	}

	public void ProcessSubTitle(XmlReader reader, XmlTvProgram result)
	{
		ProcessNode(reader, (string s) =>
		{
			result.Episode.Title = s;
		}, _language);
	}

	public void ProcessDescription(XmlReader reader, XmlTvProgram result)
	{
		ProcessNode(reader, (string s) =>
		{
			result.Description = s;
		}, _language);
	}

	public void ProcessTitleNode(XmlReader reader, XmlTvProgram result)
	{
		ProcessNode(reader, (string s) =>
		{
			result.Title = s;
		}, _language);
	}

	public void ProcessPremiereNode(XmlReader reader, XmlTvProgram result)
	{
		ProcessNode(reader, (string s) =>
		{
			if (result.Premiere == null)
			{
				result.Premiere = new XmlTvPremiere
				{
					Details = s
				};
			}
			else
			{
				result.Premiere.Details = s;
			}
		}, _language);
	}

	public XmlTvIcon ProcessIconNode(XmlReader reader)
	{
		XmlTvIcon xmlTvIcon = new XmlTvIcon();
		bool flag = false;
		string attribute = reader.GetAttribute("src");
		if (!string.IsNullOrEmpty(attribute))
		{
			xmlTvIcon.Source = attribute;
			flag = true;
		}
		string attribute2 = reader.GetAttribute("width");
		int result = 0;
		if (!string.IsNullOrEmpty(attribute2) && int.TryParse(attribute2, out result))
		{
			xmlTvIcon.Width = result;
			flag = true;
		}
		string attribute3 = reader.GetAttribute("height");
		int result2 = 0;
		if (!string.IsNullOrEmpty(attribute3) && int.TryParse(attribute3, out result2))
		{
			xmlTvIcon.Height = result2;
			flag = true;
		}
		if (!flag)
		{
			return null;
		}
		return xmlTvIcon;
	}

	public void ProcessNode(XmlReader reader, Action<string> setter, string languageRequired = null, Action<string> allOccurrencesSetter = null)
	{
		List<Tuple<string, string>> list = new List<Tuple<string, string>>();
		string name = reader.Name;
		string item = (reader.HasAttributes ? reader.GetAttribute("lang") : null);
		string text = reader.ReadElementContentAsString();
		list.Add(new Tuple<string, string>(text, item));
		allOccurrencesSetter?.Invoke(text);
		while (!reader.EOF && reader.ReadState == ReadState.Interactive)
		{
			if (reader.NodeType == XmlNodeType.Element)
			{
				if (!(reader.Name == name))
				{
					break;
				}
				item = (reader.HasAttributes ? reader.GetAttribute("lang") : null);
				text = reader.ReadElementContentAsString();
				allOccurrencesSetter?.Invoke(text);
				list.Add(new Tuple<string, string>(text, item));
			}
			else
			{
				reader.Read();
			}
		}
		if (languageRequired != null)
		{
			foreach (Tuple<string, string> item2 in list)
			{
				if (string.Equals(languageRequired, item2.Item2, StringComparison.OrdinalIgnoreCase))
				{
					setter(item2.Item1);
					return;
				}
			}
		}
		foreach (Tuple<string, string> item3 in list)
		{
			if (string.IsNullOrWhiteSpace(item3.Item2))
			{
				setter(item3.Item1);
				return;
			}
		}
		using List<Tuple<string, string>>.Enumerator enumerator = list.GetEnumerator();
		if (enumerator.MoveNext())
		{
			Tuple<string, string> current3 = enumerator.Current;
			setter(current3.Item1);
		}
	}

	public void ProcessMultipleNodes(XmlReader reader, Action<string> setter, string languageRequired = null)
	{
		string name = reader.Name;
		var list = new[]
		{
			new
			{
				Language = reader.GetAttribute("lang"),
				Value = reader.ReadElementContentAsString()
			}
		}.ToList();
		while (reader.Read() && (reader.NodeType != XmlNodeType.Element || !(reader.Name != name)))
		{
			if (reader.NodeType == XmlNodeType.Element && reader.Name == name)
			{
				list.Add(new
				{
					Language = reader.GetAttribute("lang"),
					Value = reader.ReadElementContentAsString()
				});
			}
		}
		if (list.Count(v => v.Language == languageRequired) > 0)
		{
			list.RemoveAll(v => v.Language != languageRequired);
		}
		foreach (var item in list)
		{
			setter(item.Value);
		}
	}

	public void ProcessMultipleNodesWithLanguage(XmlReader reader, Action<string> setter)
	{
		string name = reader.Name;
		while (reader.Name == name)
		{
			string attribute = reader.GetAttribute("lang");
			if (string.IsNullOrEmpty(_language) || string.IsNullOrEmpty(attribute) || attribute == _language)
			{
				setter(reader.ReadElementContentAsString());
			}
			reader.Skip();
		}
	}

	private void PopulateHeader(XmlReader reader, XmlTvProgram result)
	{
		result.ChannelId = reader.GetAttribute("channel");
		string attribute = reader.GetAttribute("start");
		if (string.IsNullOrEmpty(attribute))
		{
			result.StartDate = DateTime.MinValue;
		}
		else
		{
			result.StartDate = ParseDate(attribute).GetValueOrDefault();
		}
		string attribute2 = reader.GetAttribute("stop");
		if (string.IsNullOrEmpty(attribute2))
		{
			result.EndDate = DateTime.MinValue;
		}
		else
		{
			result.EndDate = ParseDate(attribute2).GetValueOrDefault();
		}
	}

	public DateTime? ParseDate(string dateValue)
	{
		DateTime? result = null;
		if (!string.IsNullOrEmpty(dateValue))
		{
			string text = "20000101000000";
			string text2 = string.Empty;
			string text3 = "+00:00";
			Match match = _regDateWithOffset.Match(dateValue);
			if (match.Success)
			{
				text2 = match.Groups["dateDigits"].Value;
				if (!string.IsNullOrEmpty(match.Groups["dateOffset"].Value))
				{
					text3 = match.Groups["dateOffset"].Value;
					text3 = ((text3.Length != 5) ? "+00:00" : text3.Insert(3, ":"));
				}
			}
			if (text2.Length < 14)
			{
				text2 += text.Substring(text2.Length, text.Length - text2.Length);
			}
			if (DateTimeOffset.TryParseExact(string.Format("{0} {1}", new object[2] { text2, text3 }), "yyyyMMddHHmmss zzz", CultureInfo.CurrentCulture, DateTimeStyles.None, out var result2))
			{
				return result2.UtcDateTime;
			}
		}
		return result;
	}

	public string StandardiseDate(string value)
	{
		string text = "20000101000000";
		string text2 = string.Empty;
		string text3 = "+0000";
		Match match = _regDateWithOffset.Match(value);
		if (match.Success)
		{
			text2 = match.Groups["dateDigits"].Value;
			text3 = match.Groups["dateOffset"].Value;
		}
		if (text2.Length < 14)
		{
			text2 += text.Substring(text2.Length, text.Length - text2.Length);
		}
		return string.Format("{0} {1}", new object[2] { text2, text3 });
	}
}
