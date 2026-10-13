using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Playlists;

namespace MediaBrowser.Controller.Playlists;

public interface IPlaylistManager
{
	IEnumerable<Playlist> GetPlaylists(Guid userId);

	Task<PlaylistCreationResult> CreatePlaylist(PlaylistCreationRequest options);

	void AddToPlaylist(string playlistId, IEnumerable<Guid> itemIds, Guid userId);

	void RemoveFromPlaylist(string playlistId, IEnumerable<string> entryIds);

	Folder GetPlaylistsFolder(Guid userId);

	void MoveItem(string playlistId, string entryId, int newIndex);
}
