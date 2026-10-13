using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Library;

public class ItemChangeEventArgs
{
	public BaseItem Item { get; set; }

	public BaseItem Parent { get; set; }

	public ItemUpdateType UpdateReason { get; set; }
}
