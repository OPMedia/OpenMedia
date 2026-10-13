using System;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Users;

namespace MediaBrowser.Model.Dto;

public class UserDto : IItemDto, IHasServerId
{
	public string Name { get; set; }

	public string ServerId { get; set; }

	public string ServerName { get; set; }




	public Guid Id { get; set; }

	public string PrimaryImageTag { get; set; }

	public bool HasPassword { get; set; }

	public bool HasConfiguredPassword { get; set; }

	public bool HasConfiguredEasyPassword { get; set; }

	public bool? EnableAutoLogin { get; set; }

	public DateTime? LastLoginDate { get; set; }

	public DateTime? LastActivityDate { get; set; }

	public UserConfiguration Configuration { get; set; }

	public UserPolicy Policy { get; set; }

	public double? PrimaryImageAspectRatio { get; set; }

	public string ConnectLinkType { get; set; }

	public string ConnectUserName { get; set; }

	public UserDto()
	{
		Configuration = new UserConfiguration();
		Policy = new UserPolicy();
	}

	public override string ToString()
	{
		return Name ?? base.ToString();
	}
}
