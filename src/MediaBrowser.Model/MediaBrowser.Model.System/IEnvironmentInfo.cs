namespace MediaBrowser.Model.System;

public interface IEnvironmentInfo
{
	OperatingSystem OperatingSystem { get; }

	string OperatingSystemName { get; }

	string OperatingSystemVersion { get; }

	Architecture SystemArchitecture { get; }

	string StackTrace { get; }

	char PathSeparator { get; }

	string GetEnvironmentVariable(string name);

	void SetProcessEnvironmentVariable(string name, string value);
}
