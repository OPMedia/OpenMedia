using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.TV;

public interface ITVSeriesManager
{
	QueryResult<BaseItem> GetNextUp(NextUpQuery query, DtoOptions options);

	QueryResult<BaseItem> GetNextUp(NextUpQuery request, BaseItem[] parentsFolders, DtoOptions options);
}
