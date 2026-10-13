using System;
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Library;

public interface IUserDataManager
{
	event EventHandler<UserDataSaveEventArgs> UserDataSaved;

	void SaveUserData(Guid userId, BaseItem item, UserItemData userData, UserDataSaveReason reason, CancellationToken cancellationToken);

	void SaveUserData(User user, BaseItem item, UserItemData userData, UserDataSaveReason reason, CancellationToken cancellationToken);

	UserItemData GetUserData(User user, BaseItem item);

	UserItemData GetUserData(string userId, BaseItem item);

	UserItemData GetUserData(Guid userId, BaseItem item);

	UserItemData GetUserData(User user, Guid itemId, List<string> keys);

	UserItemDataDto GetUserDataDto(BaseItem item, User user);

	UserItemDataDto GetUserDataDto(BaseItem item, BaseItemDto itemDto, User user, DtoOptions options);

	List<UserItemData> GetAllUserData(Guid userId);

	void SaveAllUserData(Guid userId, UserItemData[] userData, CancellationToken cancellationToken);

	bool UpdatePlayState(BaseItem item, UserItemData data, long? positionTicks);
}
