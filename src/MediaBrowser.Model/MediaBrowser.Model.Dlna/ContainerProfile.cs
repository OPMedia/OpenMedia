using System;
using System.Linq;
using System.Xml.Serialization;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Model.Dlna;

public class ContainerProfile
{
	private static readonly string[] EmptyStringArray = Array.Empty<string>();

	[XmlAttribute("type")]
	public DlnaProfileType Type { get; set; }

	public ProfileCondition[] Conditions { get; set; }

	[XmlAttribute("container")]
	public string Container { get; set; }

	public ContainerProfile()
	{
		Conditions = new ProfileCondition[0];
	}

	public string[] GetContainers()
	{
		return SplitValue(Container);
	}

	public static string[] SplitValue(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return EmptyStringArray;
		}
		return value.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
	}

	public bool ContainsContainer(string container)
	{
		return ContainsContainer(GetContainers(), container);
	}

	public static bool ContainsContainer(string profileContainers, string inputContainer)
	{
		bool isNegativeList = false;
		if (profileContainers != null && profileContainers.StartsWith("-"))
		{
			isNegativeList = true;
			profileContainers = profileContainers.Substring(1);
		}
		return ContainsContainer(SplitValue(profileContainers), isNegativeList, inputContainer);
	}

	public static bool ContainsContainer(string[] profileContainers, string inputContainer)
	{
		return ContainsContainer(profileContainers, isNegativeList: false, inputContainer);
	}

	public static bool ContainsContainer(string[] profileContainers, bool isNegativeList, string inputContainer)
	{
		if (profileContainers.Length == 0)
		{
			return true;
		}
		string[] array;
		if (isNegativeList)
		{
			array = SplitValue(inputContainer);
			foreach (string value in array)
			{
				if (profileContainers.Contains(value, StringComparer.OrdinalIgnoreCase))
				{
					return false;
				}
			}
			return true;
		}
		array = SplitValue(inputContainer);
		foreach (string value2 in array)
		{
			if (ListHelper.ContainsIgnoreCase(profileContainers, value2))
			{
				return true;
			}
		}
		return false;
	}
}
