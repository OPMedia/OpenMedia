using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Controller.Net;

public class StaticResultOptions
{
	public string ContentType { get; set; }

	public TimeSpan? CacheDuration { get; set; }

	public DateTime? DateLastModified { get; set; }

	public Guid CacheKey { get; set; }

	public Func<Task<Stream>> ContentFactory { get; set; }

	public bool IsHeadRequest { get; set; }

	public IDictionary<string, string> ResponseHeaders { get; set; }

	public Action OnComplete { get; set; }

	public Action OnError { get; set; }

	public string Path { get; set; }

	public long? ContentLength { get; set; }

	public FileShareMode FileShare
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public StaticResultOptions()
	{
		ResponseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		FileShare = (FileShareMode)1;
	}
}
