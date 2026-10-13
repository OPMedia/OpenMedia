using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;

namespace MediaBrowser.Controller.Collections;

public class CollectionModifiedEventArgs : EventArgs
{
	public BoxSet Collection { get; set; }

	public List<BaseItem> ItemsChanged { get; set; }
}
