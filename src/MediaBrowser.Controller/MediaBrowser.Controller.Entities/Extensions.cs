using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Controller.Entities;

public static class Extensions
{
	public static void AddTrailerUrl(this BaseItem item, string url)
	{
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Expected Obj, but got Unknown
		if (string.IsNullOrEmpty(url))
		{
			throw new ArgumentNullException("url");
		}
		if (item.RemoteTrailers.FirstOrDefault((MediaUrl i) => string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase)) == null)
		{
			MediaUrl val = new MediaUrl
			{
				Url = url
			};
			if (item.RemoteTrailers.Length == 0)
			{
				item.RemoteTrailers = new MediaUrl[1] { val };
			}
			else
			{
				MediaUrl[] array = LinqExtensions.ToArray<MediaUrl>((IEnumerable<MediaUrl>)item.RemoteTrailers, item.RemoteTrailers.Length + 1);
				array[^1] = val;
				item.RemoteTrailers = array;
			}
		}
	}
}
