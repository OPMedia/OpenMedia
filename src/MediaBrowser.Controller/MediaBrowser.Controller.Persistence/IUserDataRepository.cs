using System;
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Persistence;

public interface IUserDataRepository : IRepository, IDisposable
{
	void SaveUserData(long internalUserId, string key, UserItemData userData, CancellationToken cancellationToken);

	UserItemData GetUserData(long internalUserId, string key);

	UserItemData GetUserData(long internalUserId, List<string> keys);

	List<UserItemData> GetAllUserData(long internalUserId);

	void SaveAllUserData(long internalUserId, UserItemData[] userData, CancellationToken cancellationToken);
}
