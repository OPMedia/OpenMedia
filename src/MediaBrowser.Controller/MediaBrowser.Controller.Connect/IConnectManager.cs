using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Connect;

namespace MediaBrowser.Controller.Connect;

public interface IConnectManager
{
	string WanApiAddress { get; }

	Task<UserLinkResult> LinkUser(User user, string connectUsername);

	Task RemoveConnect(User user);

	User GetUserFromExchangeToken(string token);

	Task<ConnectAuthenticationResult> Authenticate(string username, string password);

	bool IsAuthorizationTokenValid(string token);
}
