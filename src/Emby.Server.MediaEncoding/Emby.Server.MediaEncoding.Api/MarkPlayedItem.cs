using System;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Services;

namespace Emby.Server.MediaEncoding.Api;

[Route("/Users/{UserId}/PlayedItems/{Id}", "POST", Summary = "Marks an item as played")]
public class MarkPlayedItem : IReturn<UserItemDataDto>, IReturn
{
	[ApiMember(Name = "UserId", Description = "User Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
	public Guid UserId { get; set; }

	[ApiMember(Name = "DatePlayed", Description = "The date the item was played (if any). Format = yyyyMMddHHmmss", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "POST")]
	public string DatePlayed { get; set; }

	[ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
	public Guid Id { get; set; }
}
