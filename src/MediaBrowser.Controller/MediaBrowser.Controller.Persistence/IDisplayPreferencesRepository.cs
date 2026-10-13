using System;
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Persistence;

public interface IDisplayPreferencesRepository : IRepository, IDisposable
{
	void SaveDisplayPreferences(DisplayPreferences displayPreferences, string userId, string client, CancellationToken cancellationToken);

	void SaveAllDisplayPreferences(IEnumerable<DisplayPreferences> displayPreferences, Guid userId, CancellationToken cancellationToken);

	DisplayPreferences GetDisplayPreferences(string displayPreferencesId, string userId, string client);

	IEnumerable<DisplayPreferences> GetAllDisplayPreferences(Guid userId);
}
