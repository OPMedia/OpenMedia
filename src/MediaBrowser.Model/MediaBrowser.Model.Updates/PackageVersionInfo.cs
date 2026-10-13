using System;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Model.Updates;

public class PackageVersionInfo
{
	private Version _version;

	public string name { get; set; }

	public string guid { get; set; }

	public string versionStr { get; set; }

	[IgnoreDataMember]
	public Version version => _version ?? (_version = new Version(ValueOrDefault(versionStr, "0.0.0.1")));

	public PackageVersionClass classification { get; set; }

	public string description { get; set; }

	public string requiredVersionStr { get; set; }

	public string sourceUrl { get; set; }

	public string checksum { get; set; }

	public string targetFilename { get; set; }

	public string infoUrl { get; set; }

	public string runtimes { get; set; }

	private static string ValueOrDefault(string str, string def)
	{
		if (!string.IsNullOrEmpty(str))
		{
			return str;
		}
		return def;
	}
}
