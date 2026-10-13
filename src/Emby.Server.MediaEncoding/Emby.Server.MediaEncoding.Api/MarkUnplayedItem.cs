using System;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Users/{UserId}/PlayedItems/{Id}", "DELETE", Summary = "Marks an item as unplayed")]
public class MarkUnplayedItem : IReturn<UserItemDataDto>, IReturn
{
	[ApiMember(Name = "UserId", Description = "User Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "DELETE")]
	public Guid UserId { get; set; }

	[ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "DELETE")]
	public Guid Id { get; set; }
}
