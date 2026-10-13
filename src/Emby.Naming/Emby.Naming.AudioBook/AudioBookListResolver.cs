using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Naming.Common;
using Emby.Naming.Video;
using MediaBrowser.Model.IO;

namespace Emby.Naming.AudioBook;

public class AudioBookListResolver
{
	private readonly NamingOptions _options;

	public AudioBookListResolver(NamingOptions options)
	{
		_options = options;
	}

	public IEnumerable<AudioBookInfo> Resolve(List<FileSystemMetadata> files)
	{
		AudioBookResolver audioBookResolver = new AudioBookResolver(_options);
		IEnumerable<FileSystemMetadata> files2 = ((IEnumerable<AudioBookFileInfo>)(from i in files
			select audioBookResolver.Resolve(i.FullName, i.IsDirectory) into i
			where i != null
			select i).ToList()).Select((Func<AudioBookFileInfo, FileSystemMetadata>)((AudioBookFileInfo i) =>
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected Obj, but got Unknown
			return new FileSystemMetadata
			{
				FullName = i.Path,
				IsDirectory = i.IsDirectory
			};
		}));
		StackResult stackResult = new StackResolver(_options).ResolveAudioBooks(files2);
		List<AudioBookInfo> list = new List<AudioBookInfo>();
		foreach (FileStack stack in stackResult.Stacks)
		{
			List<AudioBookFileInfo> list2 = stack.Files.Select((string i) => audioBookResolver.Resolve(i, stack.IsDirectoryStack)).ToList();
			list2.Sort();
			AudioBookInfo item = new AudioBookInfo
			{
				Files = list2,
				Name = stack.Name
			};
			list.Add(item);
		}
		return list.OrderBy((AudioBookInfo i) => i.Name);
	}
}
