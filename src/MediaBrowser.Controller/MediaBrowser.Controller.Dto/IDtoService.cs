using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Dto;

public interface IDtoService
{
	string GetDtoId(BaseItem item);

	void AttachPrimaryImageAspectRatio(IItemDto dto, BaseItem item);

	double? GetPrimaryImageAspectRatio(BaseItem item);

	BaseItemDto GetBaseItemDto(BaseItem item, ItemFields[] fields, User user = null, BaseItem owner = null);

	BaseItemDto GetBaseItemDto(BaseItem item, DtoOptions options, User user = null, BaseItem owner = null);

	BaseItemDto[] GetBaseItemDtos(BaseItem[] items, DtoOptions options, User user = null, BaseItem owner = null);

	BaseItemDto[] GetBaseItemDtos(List<BaseItem> items, DtoOptions options, User user = null, BaseItem owner = null);

	BaseItemDto GetItemByNameDto(BaseItem item, DtoOptions options, List<BaseItem> taggedItems, User user = null);
}
