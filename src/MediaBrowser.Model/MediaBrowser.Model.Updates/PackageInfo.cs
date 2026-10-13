using System;

namespace MediaBrowser.Model.Updates;

public class PackageInfo
{
	public string id { get; set; }

	public string name { get; set; }

	public string shortDescription { get; set; }

	public string overview { get; set; }

	public bool isPremium { get; set; }

	public bool adult { get; set; }

	public string richDescUrl { get; set; }

	public string thumbImage { get; set; }

	public string previewImage { get; set; }

	public string type { get; set; }

	public string targetFilename { get; set; }

	public string owner { get; set; }

	public string category { get; set; }

	public string tileColor { get; set; }

	public string featureId { get; set; }

	public string regInfo { get; set; }

	public float price { get; set; }

	public PackageTargetSystem targetSystem { get; set; }

	public string guid { get; set; }

	public int? totalRatings { get; set; }

	public float avgRating { get; set; }

	public bool isRegistered { get; set; }

	public DateTime expDate { get; set; }

	public PackageVersionInfo[] versions { get; set; }

	public bool enableInAppStore { get; set; }

	public int installs { get; set; }

	public PackageInfo()
	{
		versions = new PackageVersionInfo[0];
	}
}
