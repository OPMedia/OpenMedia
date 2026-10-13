namespace MediaBrowser.Controller.Net;

public interface IAuthenticationAttributes
{
	bool EscapeParentalControl { get; }

	bool AllowBeforeStartupWizard { get; }

	bool AllowLocal { get; }

	bool AllowLocalOnly { get; }

	string[] GetRoles();
}
