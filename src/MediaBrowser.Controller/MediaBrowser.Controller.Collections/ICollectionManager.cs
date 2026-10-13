using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;

namespace MediaBrowser.Controller.Collections;

public interface ICollectionManager
{
	event EventHandler<CollectionCreatedEventArgs> CollectionCreated;

	event EventHandler<CollectionModifiedEventArgs> ItemsAddedToCollection;

	event EventHandler<CollectionModifiedEventArgs> ItemsRemovedFromCollection;

	BoxSet CreateCollection(CollectionCreationOptions options);

	void AddToCollection(Guid collectionId, IEnumerable<string> itemIds);

	void RemoveFromCollection(Guid collectionId, IEnumerable<string> itemIds);

	void AddToCollection(Guid collectionId, IEnumerable<Guid> itemIds);

	void RemoveFromCollection(Guid collectionId, IEnumerable<Guid> itemIds);

	IEnumerable<BaseItem> CollapseItemsWithinBoxSets(IEnumerable<BaseItem> items, User user);
}
