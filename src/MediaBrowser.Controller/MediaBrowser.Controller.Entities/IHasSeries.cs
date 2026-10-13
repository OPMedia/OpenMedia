using System;

namespace MediaBrowser.Controller.Entities;

public interface IHasSeries : IHasSeriesName
{
	Guid SeriesId { get; set; }

	string SeriesPresentationUniqueKey { get; set; }

	string FindSeriesName();

	string FindSeriesSortName();

	Guid FindSeriesId();

	string FindSeriesPresentationUniqueKey();
}
