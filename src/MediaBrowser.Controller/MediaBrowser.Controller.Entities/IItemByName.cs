using System.Collections.Generic;

namespace MediaBrowser.Controller.Entities;

public interface IItemByName
{
	IList<BaseItem> GetTaggedItems(InternalItemsQuery query);
}
