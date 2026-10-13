using System;

namespace MediaBrowser.Model.Updates;

public class InstallationInfo
{
	public Guid Id { get; set; }

	public string Name { get; set; }

	public string AssemblyGuid { get; set; }

	public string Version { get; set; }

	public PackageVersionClass UpdateClass { get; set; }

	public double? PercentComplete { get; set; }
}
