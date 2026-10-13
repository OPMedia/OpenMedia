using System;
using System.Text.RegularExpressions;
using MediaBrowser.Model.Cryptography;

namespace MediaBrowser.Common.Extensions;

public static class BaseExtensions
{
	public static ICryptoProvider CryptographyProvider { get; set; }

	public static string StripHtml(this string htmlString)
	{
		return Regex.Replace(htmlString, "<(.|\\n)*?>", string.Empty).Trim();
	}

	public static Guid GetMD5(this string str)
	{
		return CryptographyProvider.GetMD5(str);
	}

	[Obsolete("Use LibraryManager.GetNewItemId")]
	public static Guid GetMBId(this string str, Type type)
	{
		return (type.FullName + str.ToLower()).GetMD5();
	}
}
