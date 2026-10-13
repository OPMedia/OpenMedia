using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Authentication;

public interface IAuthenticationProvider
{
	string Name { get; }

	bool IsEnabled { get; }

	Task<ProviderAuthenticationResult> Authenticate(string username, string password);

	Task<bool> HasPassword(User user);

	Task ChangePassword(User user, string newPassword);
}
