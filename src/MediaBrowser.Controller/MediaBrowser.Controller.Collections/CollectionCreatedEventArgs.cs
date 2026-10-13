using System;
using MediaBrowser.Controller.Entities.Movies;

namespace MediaBrowser.Controller.Collections;

public class CollectionCreatedEventArgs : EventArgs
{
	public BoxSet Collection { get; set; }

	public CollectionCreationOptions Options { get; set; }
}
