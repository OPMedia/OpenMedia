using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace MediaBrowser.Controller.Sorting;

public interface IUserBaseItemComparer : IBaseItemComparer, IComparer<BaseItem>
{
	User User { get; set; }

	IUserManager UserManager { get; set; }

	IUserDataManager UserDataRepository { get; set; }
}
