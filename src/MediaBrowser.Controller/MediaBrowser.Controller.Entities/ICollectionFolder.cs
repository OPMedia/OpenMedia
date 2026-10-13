using System;

namespace MediaBrowser.Controller.Entities;

public interface ICollectionFolder : IHasCollectionType
{
	string Path { get; }

	string Name { get; }

	Guid Id { get; }

	string[] PhysicalLocations { get; }
}
