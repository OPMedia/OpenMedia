using System;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class LinkedChild
{
	public string Path { get; set; }

	public LinkedChildType Type { get; set; }

	public string LibraryItemId { get; set; }

	[IgnoreDataMember]
	public string Id { get; set; }

	public Guid? ItemId { get; set; }

	public static LinkedChild Create(BaseItem item)
	{
		LinkedChild linkedChild = new LinkedChild
		{
			Path = item.Path,
			Type = LinkedChildType.Manual
		};
		if (string.IsNullOrEmpty(linkedChild.Path))
		{
			linkedChild.LibraryItemId = item.Id.ToString("N");
		}
		return linkedChild;
	}

	public LinkedChild()
	{
		Id = Guid.NewGuid().ToString("N");
	}
}
