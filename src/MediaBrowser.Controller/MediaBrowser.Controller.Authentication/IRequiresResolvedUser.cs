using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Authentication;

public interface IRequiresResolvedUser
{
	Task<ProviderAuthenticationResult> Authenticate(string username, string password, User resolvedUser);
}
