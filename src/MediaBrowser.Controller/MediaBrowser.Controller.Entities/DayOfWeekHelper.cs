using System;
using System.Collections.Generic;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.Controller.Entities;

public static class DayOfWeekHelper
{
	public static List<DayOfWeek> GetDaysOfWeek(DynamicDayOfWeek day)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		return GetDaysOfWeek(new List<DynamicDayOfWeek> { day });
	}

	public static List<DayOfWeek> GetDaysOfWeek(List<DynamicDayOfWeek> days)
	{
		List<DayOfWeek> list = new List<DayOfWeek>();
		if (days.Contains((DynamicDayOfWeek)0) || days.Contains((DynamicDayOfWeek)9) || days.Contains((DynamicDayOfWeek)7))
		{
			list.Add(DayOfWeek.Sunday);
		}
		if (days.Contains((DynamicDayOfWeek)6) || days.Contains((DynamicDayOfWeek)9) || days.Contains((DynamicDayOfWeek)7))
		{
			list.Add(DayOfWeek.Saturday);
		}
		if (days.Contains((DynamicDayOfWeek)1) || days.Contains((DynamicDayOfWeek)8) || days.Contains((DynamicDayOfWeek)7))
		{
			list.Add(DayOfWeek.Monday);
		}
		if (days.Contains((DynamicDayOfWeek)2) || days.Contains((DynamicDayOfWeek)8) || days.Contains((DynamicDayOfWeek)7))
		{
			list.Add(DayOfWeek.Tuesday);
		}
		if (days.Contains((DynamicDayOfWeek)3) || days.Contains((DynamicDayOfWeek)8) || days.Contains((DynamicDayOfWeek)7))
		{
			list.Add(DayOfWeek.Wednesday);
		}
		if (days.Contains((DynamicDayOfWeek)4) || days.Contains((DynamicDayOfWeek)8) || days.Contains((DynamicDayOfWeek)7))
		{
			list.Add(DayOfWeek.Thursday);
		}
		if (days.Contains((DynamicDayOfWeek)5) || days.Contains((DynamicDayOfWeek)8) || days.Contains((DynamicDayOfWeek)7))
		{
			list.Add(DayOfWeek.Friday);
		}
		return list;
	}
}
