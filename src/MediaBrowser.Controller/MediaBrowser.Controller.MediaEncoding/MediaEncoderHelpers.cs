using System;
using System.IO;
using System.Linq;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Controller.MediaEncoding;

public static class MediaEncoderHelpers
{
	public static string[] GetInputArgument(IFileSystem fileSystem, string videoPath, MediaProtocol protocol, IIsoMount isoMount, string[] playableStreamFileNames)
	{
		if (playableStreamFileNames.Length != 0)
		{
			if (isoMount == null)
			{
				return GetPlayableStreamFiles(fileSystem, videoPath, playableStreamFileNames);
			}
			return GetPlayableStreamFiles(fileSystem, isoMount.MountedPath, playableStreamFileNames);
		}
		return new string[1] { videoPath };
	}

	private static string[] GetPlayableStreamFiles(IFileSystem fileSystem, string rootPath, string[] filenames)
	{
		if (filenames.Length == 0)
		{
			return new string[0];
		}
		string[] allFiles = fileSystem.GetFilePaths(rootPath, true).ToArray();
		return (from name in filenames
			select allFiles.FirstOrDefault((string f) => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) into f
			where !string.IsNullOrEmpty(f)
			select f).ToArray();
	}
}
