using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.Session;

public class UserDataChangeInfo
{
	public string UserId { get; set; }

	public UserItemDataDto[] UserDataList { get; set; }
}
