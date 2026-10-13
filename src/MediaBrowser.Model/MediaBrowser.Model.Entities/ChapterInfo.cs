using System;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Model.Entities;

public class ChapterInfo
{
	public long StartPositionTicks { get; set; }

	public string Name { get; set; }

	public string ImageTag { get; set; }

	[IgnoreDataMember]
	public string ImagePath { get; set; }

	[IgnoreDataMember]
	public DateTime ImageDateModified { get; set; }
}
