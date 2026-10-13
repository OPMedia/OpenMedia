using System.Collections.Generic;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;

namespace MediaBrowser.Controller.Library;

public interface IMusicManager
{
	List<BaseItem> GetInstantMixFromItem(BaseItem item, User user, DtoOptions dtoOptions);

	List<BaseItem> GetInstantMixFromArtist(MusicArtist artist, User user, DtoOptions dtoOptions);

	List<BaseItem> GetInstantMixFromGenres(IEnumerable<string> genres, User user, DtoOptions dtoOptions);
}
