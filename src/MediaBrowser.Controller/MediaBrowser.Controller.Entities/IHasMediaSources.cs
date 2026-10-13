using System;
using System.Collections.Generic;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Entities;

public interface IHasMediaSources
{
	Guid Id { get; set; }

	long? RunTimeTicks { get; set; }

	string Path { get; }

	List<MediaSourceInfo> GetMediaSources(bool enablePathSubstitution);

	List<MediaStream> GetMediaStreams();
}
