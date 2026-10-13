using System.Threading.Tasks;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Common.Security;

public interface ISecurityManager
{
	string SupporterKey { get; }

	Task<bool> IsSupporter();

	Task<MBRegistrationRecord> GetRegistrationStatus(string feature);

	Task RegisterAppStoreSale(string parameters);

	Task UpdateSupporterKey(string newValue);
}
