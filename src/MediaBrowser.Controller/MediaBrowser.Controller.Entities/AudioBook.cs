using System;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class AudioBook : MediaBrowser.Controller.Entities.Audio.Audio, IHasSeries, IHasSeriesName, IHasLookupInfo<SongInfo>
{
	[IgnoreDataMember]
	public override bool SupportsPositionTicksResume => true;

	[IgnoreDataMember]
	public override bool SupportsPlayedStatus => true;

	[IgnoreDataMember]
	public string SeriesPresentationUniqueKey { get; set; }

	[IgnoreDataMember]
	public string SeriesName { get; set; }

	[IgnoreDataMember]
	public Guid SeriesId { get; set; }

	public string FindSeriesSortName()
	{
		return SeriesName;
	}

	public string FindSeriesName()
	{
		return SeriesName;
	}

	public string FindSeriesPresentationUniqueKey()
	{
		return SeriesPresentationUniqueKey;
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 0.0;
	}

	public Guid FindSeriesId()
	{
		return SeriesId;
	}

	public override UnratedItem GetBlockUnratedType()
	{
		return (UnratedItem)5;
	}
}
