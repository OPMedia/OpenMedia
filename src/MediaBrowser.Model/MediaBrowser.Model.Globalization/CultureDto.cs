using System;

namespace MediaBrowser.Model.Globalization;

public class CultureDto
{
	public string Name { get; set; }

	public string DisplayName { get; set; }

	public string TwoLetterISOLanguageName { get; set; }

	public string ThreeLetterISOLanguageName
	{
		get
		{
			string[] threeLetterISOLanguageNames = ThreeLetterISOLanguageNames;
			if (threeLetterISOLanguageNames.Length != 0)
			{
				return threeLetterISOLanguageNames[0];
			}
			return null;
		}
	}

	public string[] ThreeLetterISOLanguageNames { get; set; }

	public CultureDto()
	{
		ThreeLetterISOLanguageNames = Array.Empty<string>();
	}
}
