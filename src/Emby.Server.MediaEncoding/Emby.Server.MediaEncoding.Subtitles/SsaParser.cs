using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;

namespace Emby.Server.MediaEncoding.Subtitles;

public class SsaParser : ISubtitleParser
{
	public const string NewLine = "\r\n";

	public SubtitleTrackInfo Parse(Stream stream, CancellationToken cancellationToken)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected Obj, but got Unknown
		//IL_030f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0316: Expected Obj, but got Unknown
		SubtitleTrackInfo val = new SubtitleTrackInfo();
		List<SubtitleTrackEvent> list = new List<SubtitleTrackEvent>();
		using (StreamReader streamReader = new StreamReader(stream))
		{
			bool flag = false;
			string[] array = "Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text".Split(',');
			int num = 0;
			int num2 = 1;
			int num3 = 2;
			int num4 = 3;
			int num5 = 4;
			int num6 = 8;
			int num7 = 9;
			int num8 = 0;
			StringBuilder stringBuilder = new StringBuilder();
			string text;
			while ((text = streamReader.ReadLine()) != null)
			{
				cancellationToken.ThrowIfCancellationRequested();
				num8++;
				if (!flag)
				{
					stringBuilder.AppendLine(text);
				}
				if (text.Trim().ToLower() == "[events]")
				{
					flag = true;
				}
				else
				{
					if ((!string.IsNullOrEmpty(text) && text.Trim().StartsWith(";")) || !flag || text.Trim().Length <= 0)
					{
						continue;
					}
					string text2 = text.Trim().ToLower();
					if (text2.StartsWith("format:"))
					{
						if (text.Length <= 10)
						{
							continue;
						}
						array = text.ToLower().Substring(8).Split(',');
						for (int i = 0; i < array.Length; i++)
						{
							if (array[i].Trim().ToLower() == "layer")
							{
								num = i;
							}
							else if (array[i].Trim().ToLower() == "start")
							{
								num2 = i;
							}
							else if (array[i].Trim().ToLower() == "end")
							{
								num3 = i;
							}
							else if (array[i].Trim().ToLower() == "text")
							{
								num7 = i;
							}
							else if (array[i].Trim().ToLower() == "effect")
							{
								num6 = i;
							}
							else if (array[i].Trim().ToLower() == "style")
							{
								num4 = i;
							}
						}
					}
					else
					{
						if (string.IsNullOrEmpty(text2))
						{
							continue;
						}
						string text3 = string.Empty;
						string time = string.Empty;
						string time2 = string.Empty;
						_ = string.Empty;
						_ = string.Empty;
						_ = string.Empty;
						_ = string.Empty;
						string[] array2 = ((!text2.StartsWith("dialogue:")) ? text.Split(',') : text.Substring(10).Split(','));
						for (int j = 0; j < array2.Length; j++)
						{
							if (j == num2)
							{
								time = array2[j].Trim();
							}
							else if (j == num3)
							{
								time2 = array2[j].Trim();
							}
							else if (j == num)
							{
								_ = array2[j];
							}
							else if (j == num6)
							{
								_ = array2[j];
							}
							else if (j == num7)
							{
								text3 = array2[j];
							}
							else if (j == num4)
							{
								_ = array2[j];
							}
							else if (j == num5)
							{
								_ = array2[j];
							}
							else if (j > num7)
							{
								text3 = text3 + "," + array2[j];
							}
						}
						try
						{
							SubtitleTrackEvent val2 = new SubtitleTrackEvent();
							val2.StartPositionTicks = GetTimeCodeFromString(time);
							val2.EndPositionTicks = GetTimeCodeFromString(time2);
							val2.Text = GetFormattedText(text3);
							list.Add(val2);
						}
						catch
						{
						}
					}
				}
			}
		}
		val.TrackEvents = LinqExtensions.ToArray<SubtitleTrackEvent>((IEnumerable<SubtitleTrackEvent>)list, list.Count);
		return val;
	}

	private static long GetTimeCodeFromString(string time)
	{
		string[] array = time.Split(':', '.');
		return new TimeSpan(0, int.Parse(array[0]), int.Parse(array[1]), int.Parse(array[2]), int.Parse(array[3]) * 10).Ticks;
	}

	public static string GetFormattedText(string text)
	{
		text = StringHelper.Replace(text, "\\n", "\r\n", StringComparison.OrdinalIgnoreCase);
		bool italic = false;
		for (int i = 0; i < 10; i++)
		{
			if (text.Contains("{\\fn"))
			{
				int num = text.IndexOf("{\\fn");
				int num2 = text.IndexOf('}', num);
				if (num2 > 0 && !text.Substring(num).StartsWith("{\\fn}"))
				{
					string tagName = text.Substring(num + 4, num2 - (num + 4));
					string extraTags = string.Empty;
					CheckAndAddSubTags(ref tagName, ref extraTags, out italic);
					text = text.Remove(num, num2 - num + 1);
					text = ((!italic) ? text.Insert(num, "<font face=\"" + tagName + "\"" + extraTags + ">") : text.Insert(num, "<font face=\"" + tagName + "\"" + extraTags + "><i>"));
					int num3 = text.IndexOf("{\\fn}", num);
					text = ((num3 <= 0) ? (text + "</font>") : text.Remove(num3, "{\\fn}".Length).Insert(num3, "</font>"));
				}
			}
			if (text.Contains("{\\fs"))
			{
				int num4 = text.IndexOf("{\\fs");
				int num5 = text.IndexOf('}', num4);
				if (num5 > 0 && !text.Substring(num4).StartsWith("{\\fs}"))
				{
					string tagName2 = text.Substring(num4 + 4, num5 - (num4 + 4));
					string extraTags2 = string.Empty;
					CheckAndAddSubTags(ref tagName2, ref extraTags2, out italic);
					if (IsInteger(tagName2))
					{
						text = text.Remove(num4, num5 - num4 + 1);
						text = ((!italic) ? text.Insert(num4, "<font size=\"" + tagName2 + "\"" + extraTags2 + ">") : text.Insert(num4, "<font size=\"" + tagName2 + "\"" + extraTags2 + "><i>"));
						int num6 = text.IndexOf("{\\fs}", num4);
						text = ((num6 <= 0) ? (text + "</font>") : text.Remove(num6, "{\\fs}".Length).Insert(num6, "</font>"));
					}
				}
			}
			if (text.Contains("{\\c"))
			{
				int num7 = text.IndexOf("{\\c");
				int num8 = text.IndexOf('}', num7);
				if (num8 > 0 && !text.Substring(num7).StartsWith("{\\c}"))
				{
					string tagName3 = text.Substring(num7 + 4, num8 - (num7 + 4));
					string extraTags3 = string.Empty;
					CheckAndAddSubTags(ref tagName3, ref extraTags3, out italic);
					tagName3 = tagName3.Replace("&", string.Empty).TrimStart('H');
					tagName3 = tagName3.PadLeft(6, '0');
					tagName3 = "#" + tagName3.Remove(tagName3.Length - 6) + tagName3.Substring(tagName3.Length - 2, 2) + tagName3.Substring(tagName3.Length - 4, 2) + tagName3.Substring(tagName3.Length - 6, 2);
					tagName3 = tagName3.ToLower();
					text = text.Remove(num7, num8 - num7 + 1);
					text = ((!italic) ? text.Insert(num7, "<font color=\"" + tagName3 + "\"" + extraTags3 + ">") : text.Insert(num7, "<font color=\"" + tagName3 + "\"" + extraTags3 + "><i>"));
					int num9 = text.IndexOf("{\\c}", num7);
					text = ((num9 <= 0) ? (text + "</font>") : text.Remove(num9, "{\\c}".Length).Insert(num9, "</font>"));
				}
			}
			if (text.Contains("{\\1c"))
			{
				int num10 = text.IndexOf("{\\1c");
				int num11 = text.IndexOf('}', num10);
				if (num11 > 0 && !text.Substring(num10).StartsWith("{\\1c}"))
				{
					string tagName4 = text.Substring(num10 + 5, num11 - (num10 + 5));
					string extraTags4 = string.Empty;
					CheckAndAddSubTags(ref tagName4, ref extraTags4, out italic);
					tagName4 = tagName4.Replace("&", string.Empty).TrimStart('H');
					tagName4 = tagName4.PadLeft(6, '0');
					tagName4 = "#" + tagName4.Remove(tagName4.Length - 6) + tagName4.Substring(tagName4.Length - 2, 2) + tagName4.Substring(tagName4.Length - 4, 2) + tagName4.Substring(tagName4.Length - 6, 2);
					tagName4 = tagName4.ToLower();
					text = text.Remove(num10, num11 - num10 + 1);
					text = ((!italic) ? text.Insert(num10, "<font color=\"" + tagName4 + "\"" + extraTags4 + ">") : text.Insert(num10, "<font color=\"" + tagName4 + "\"" + extraTags4 + "><i>"));
					text += "</font>";
				}
			}
		}
		text = text.Replace("{\\i1}", "<i>");
		text = text.Replace("{\\i0}", "</i>");
		text = text.Replace("{\\i}", "</i>");
		if (CountTagInText(text, "<i>") > CountTagInText(text, "</i>"))
		{
			text += "</i>";
		}
		text = text.Replace("{\\u1}", "<u>");
		text = text.Replace("{\\u0}", "</u>");
		text = text.Replace("{\\u}", "</u>");
		if (CountTagInText(text, "<u>") > CountTagInText(text, "</u>"))
		{
			text += "</u>";
		}
		text = text.Replace("{\\b1}", "<b>");
		text = text.Replace("{\\b0}", "</b>");
		text = text.Replace("{\\b}", "</b>");
		if (CountTagInText(text, "<b>") > CountTagInText(text, "</b>"))
		{
			text += "</b>";
		}
		return text;
	}

	private static bool IsInteger(string s)
	{
		if (int.TryParse(s, out var _))
		{
			return true;
		}
		return false;
	}

	private static int CountTagInText(string text, string tag)
	{
		int num = 0;
		for (int num2 = text.IndexOf(tag); num2 >= 0; num2 = text.IndexOf(tag, num2 + 1))
		{
			num++;
			if (num2 == text.Length)
			{
				return num;
			}
		}
		return num;
	}

	private static void CheckAndAddSubTags(ref string tagName, ref string extraTags, out bool italic)
	{
		italic = false;
		int num = tagName.IndexOf("\\");
		if (num <= 0)
		{
			return;
		}
		string text = tagName.Substring(num).TrimStart('\\');
		tagName = tagName.Remove(num);
		for (int i = 0; i < 10; i++)
		{
			if (text.StartsWith("fs") && text.Length > 2)
			{
				num = text.IndexOf("\\");
				string text2 = text;
				if (num > 0)
				{
					text2 = text.Substring(0, num);
					text = text.Substring(num).TrimStart('\\');
				}
				else
				{
					text = string.Empty;
				}
				extraTags = extraTags + " size=\"" + text2.Substring(2) + "\"";
			}
			else if (text.StartsWith("fn") && text.Length > 2)
			{
				num = text.IndexOf("\\");
				string text3 = text;
				if (num > 0)
				{
					text3 = text.Substring(0, num);
					text = text.Substring(num).TrimStart('\\');
				}
				else
				{
					text = string.Empty;
				}
				extraTags = extraTags + " face=\"" + text3.Substring(2) + "\"";
			}
			else if (text.StartsWith("c") && text.Length > 2)
			{
				num = text.IndexOf("\\");
				string text4 = text;
				if (num > 0)
				{
					text4 = text.Substring(0, num);
					text = text.Substring(num).TrimStart('\\');
				}
				else
				{
					text = string.Empty;
				}
				string text5 = text4.Substring(2);
				text5 = text5.Replace("&", string.Empty).TrimStart('H');
				text5 = text5.PadLeft(6, '0');
				text5 = "#" + text5.Remove(text5.Length - 6) + text5.Substring(text5.Length - 2, 2) + text5.Substring(text5.Length - 4, 2) + text5.Substring(text5.Length - 6, 2);
				text5 = text5.ToLower();
				extraTags = extraTags + " color=\"" + text5 + "\"";
			}
			else if (text.StartsWith("i1") && text.Length > 1)
			{
				num = text.IndexOf("\\");
				italic = true;
				text = ((num <= 0) ? string.Empty : text.Substring(num).TrimStart('\\'));
			}
			else if (text.Length > 0 && text.Contains("\\"))
			{
				num = text.IndexOf("\\");
				text = text.Substring(num).TrimStart('\\');
			}
		}
	}
}
