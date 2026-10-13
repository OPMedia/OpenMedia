namespace MediaBrowser.Controller.Entities;

public interface IHasDualAccess : IItemByName
{
	bool IsAccessedByName { get; }
}
