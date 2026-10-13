using MediaBrowser.Model.Users;

namespace MediaBrowser.Controller.Authentication;

public interface IHasNewUserPolicy
{
	UserPolicy GetNewUserPolicy();
}
