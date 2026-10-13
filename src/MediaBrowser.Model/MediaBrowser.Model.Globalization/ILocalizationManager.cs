using System.Globalization;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Globalization;

public interface ILocalizationManager
{
	CultureDto[] GetCultures();

	CountryInfo[] GetCountries();

	ParentalRating[] GetParentalRatings();

	int? GetRatingLevel(string rating);

	string GetLocalizedString(string phrase, string culture);

	string GetLocalizedString(string phrase);

	LocalizatonOption[] GetLocalizationOptions();

	string RemoveDiacritics(string text);

	string NormalizeFormKD(string text);

	bool HasUnicodeCategory(string value, UnicodeCategory category);

	CultureDto FindLanguageInfo(string language);
}
