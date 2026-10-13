using MediaBrowser.Common.Configuration;

namespace MediaBrowser.Controller;

public interface IServerApplicationPaths : IApplicationPaths
{
	string RootFolderPath { get; }

	string ApplicationResourcesPath { get; }

	string DefaultUserViewsPath { get; }

	string LocalizationPath { get; }

	string PeoplePath { get; }

	string GenrePath { get; }

	string MusicGenrePath { get; }

	string GameGenrePath { get; }

	string StudioPath { get; }

	string YearPath { get; }

	string GeneralPath { get; }

	string RatingsPath { get; }

	string MediaInfoImagesPath { get; }

	string UserConfigurationDirectoryPath { get; }

	string TranscodingTempPath { get; }

	string InternalMetadataPath { get; }

	string VirtualInternalMetadataPath { get; }

	string ArtistsPath { get; }

	string GetTranscodingTempPath();
}
