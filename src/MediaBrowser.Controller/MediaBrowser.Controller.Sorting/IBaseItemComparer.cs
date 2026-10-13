using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Sorting;

public interface IBaseItemComparer : IComparer<BaseItem>
{
	string Name { get; }
}
