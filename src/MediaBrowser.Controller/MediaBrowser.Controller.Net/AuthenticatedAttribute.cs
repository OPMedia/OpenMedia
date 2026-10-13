using System;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Controller.Net;

public class AuthenticatedAttribute : Attribute, IHasRequestFilter, IAuthenticationAttributes
{
	public static IAuthService AuthService { get; set; }

	public string Roles { get; set; }

	public bool EscapeParentalControl { get; set; }

	public bool AllowBeforeStartupWizard { get; set; }

	public bool AllowLocal { get; set; }

	public bool AllowLocalOnly { get; set; }

	public int Priority => 0;

	public void RequestFilter(IRequest request, IResponse response, object requestDto)
	{
		AuthService.Authenticate(request, this);
	}

	public string[] GetRoles()
	{
		return (Roles ?? string.Empty).Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
	}
}
