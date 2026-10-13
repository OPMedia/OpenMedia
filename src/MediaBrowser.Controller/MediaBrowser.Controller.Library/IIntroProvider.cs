using System.Collections.Generic;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Library;

public interface IIntroProvider
{
	string Name { get; }

	Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user);

	IEnumerable<string> GetAllIntroFiles();
}
