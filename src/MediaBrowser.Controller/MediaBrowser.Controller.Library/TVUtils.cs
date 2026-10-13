using System;

namespace MediaBrowser.Controller.Library;

public static class TVUtils
{
	public static readonly string TvdbApiKey = "B89CE93890E9419B";

	public static readonly string TvdbBaseUrl = "https://www.thetvdb.com/";

	public static readonly string BannerUrl = TvdbBaseUrl + "banners/";

	public static DayOfWeek[] GetAirDays(string day)
	{
		if (!string.IsNullOrEmpty(day))
		{
			if (string.Equals(day, "Daily", StringComparison.OrdinalIgnoreCase))
			{
				return new DayOfWeek[7]
				{
					DayOfWeek.Sunday,
					DayOfWeek.Monday,
					DayOfWeek.Tuesday,
					DayOfWeek.Wednesday,
					DayOfWeek.Thursday,
					DayOfWeek.Friday,
					DayOfWeek.Saturday
				};
			}
			if (Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var result))
			{
				return new DayOfWeek[1] { result };
			}
			return new DayOfWeek[0];
		}
		return null;
	}
}
