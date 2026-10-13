using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Naming.Common;
using MediaBrowser.Model.IO;

namespace Emby.Naming.Video;

public class VideoListResolver
{
	private readonly NamingOptions _options;

	public VideoListResolver(NamingOptions options)
	{
		_options = options;
	}

	public IEnumerable<VideoInfo> Resolve(List<FileSystemMetadata> files, bool supportMultiVersion = true)
	{
		VideoResolver videoResolver = new VideoResolver(_options);
		List<VideoFileInfo> source = (from i in files
			select videoResolver.Resolve(i.FullName, i.IsDirectory) into i
			where i != null
			select i).ToList();
		IEnumerable<FileSystemMetadata> files2 = source.Where((VideoFileInfo i) => string.IsNullOrEmpty(i.ExtraType)).Select((Func<VideoFileInfo, FileSystemMetadata>)((VideoFileInfo i) =>
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
		StackResult stackResult = new StackResolver(_options).Resolve(files2);
		List<VideoFileInfo> list = source.Where((VideoFileInfo i) => !stackResult.Stacks.Any((FileStack s) => s.ContainsFile(i.Path, i.IsDirectory))).ToList();
		List<VideoInfo> list2 = new List<VideoInfo>();
		foreach (FileStack stack in stackResult.Stacks)
		{
			VideoInfo videoInfo = new VideoInfo
			{
				Files = stack.Files.Select((string i) => videoResolver.Resolve(i, stack.IsDirectoryStack)).ToList(),
				Name = stack.Name
			};
			videoInfo.Year = videoInfo.Files.First().Year;
			List<string> baseNames = new List<string>
			{
				stack.Name,
				Path.GetFileNameWithoutExtension(stack.Files[0])
			};
			List<VideoFileInfo> extras = GetExtras(list, baseNames);
			if (extras.Count > 0)
			{
				list = list.Except(extras).ToList();
				videoInfo.Extras = extras;
			}
			list2.Add(videoInfo);
		}
		foreach (VideoFileInfo item in list.Where((VideoFileInfo i) => string.IsNullOrEmpty(i.ExtraType)).ToList())
		{
			VideoInfo videoInfo2 = new VideoInfo
			{
				Files = new List<VideoFileInfo> { item },
				Name = item.Name
			};
			videoInfo2.Year = videoInfo2.Files.First().Year;
			List<VideoFileInfo> extras2 = GetExtras(list, new List<string> { item.FileNameWithoutExtension });
			list = list.Except(extras2.Concat(new VideoFileInfo[1] { item })).ToList();
			videoInfo2.Extras = extras2;
			list2.Add(videoInfo2);
		}
		if (supportMultiVersion)
		{
			list2 = GetVideosGroupedByVersion(list2).ToList();
		}
		if (list2.Count == 1)
		{
			VideoInfo videoInfo3 = list2[0];
			string path = list2[0].Files[0].Path;
			if (!string.IsNullOrEmpty(Path.GetDirectoryName(path)))
			{
				string fileName = Path.GetFileName(Path.GetDirectoryName(path));
				if (!string.IsNullOrEmpty(fileName))
				{
					List<VideoFileInfo> extras3 = GetExtras(list, new List<string> { fileName });
					list = list.Except(extras3).ToList();
					videoInfo3.Extras.AddRange(extras3);
				}
			}
			List<VideoFileInfo> list3 = list.Where((VideoFileInfo i) => i.ExtraRule != null && i.ExtraRule.RuleType == ExtraRuleType.Filename).ToList();
			list = list.Except(list3).ToList();
			videoInfo3.Extras.AddRange(list3);
		}
		if (list2.Count == 1)
		{
			List<VideoFileInfo> list4 = list.Where((VideoFileInfo i) => string.Equals(i.ExtraType, "trailer", StringComparison.OrdinalIgnoreCase)).ToList();
			list2[0].Extras.AddRange(list4);
			list = list.Except(list4).ToList();
		}
		list2.AddRange(list.Select((VideoFileInfo i) => new VideoInfo
		{
			Files = new List<VideoFileInfo> { i },
			Name = i.Name,
			Year = i.Year
		}));
		return list2.OrderBy((VideoInfo i) => i.Name);
	}

	private IEnumerable<VideoInfo> GetVideosGroupedByVersion(List<VideoInfo> videos)
	{
		if (videos.Count == 0)
		{
			return videos;
		}
		List<VideoInfo> list = new List<VideoInfo>();
		string folderName = Path.GetFileName(Path.GetDirectoryName(videos[0].Files[0].Path));
		if (!string.IsNullOrEmpty(folderName) && folderName.Length > 1 && videos.All((VideoInfo i) => i.Files.Count == 1 && IsEligibleForMultiVersion(folderName, i.Files[0].Path)) && videos.Count <= 8 && HaveSameYear(videos))
		{
			List<VideoInfo> list2 = videos.OrderBy((VideoInfo i) => i.Name).ToList();
			list.Add(list2[0]);
			list[0].AlternateVersions = (from i in list2.Skip(1)
				select i.Files[0]).ToList();
			list[0].Name = folderName;
			list[0].Extras.AddRange(list2.Skip(1).SelectMany((VideoInfo i) => i.Extras));
			return list;
		}
		return videos;
	}

	private bool HaveSameYear(List<VideoInfo> videos)
	{
		return videos.Select((VideoInfo i) => i.Year ?? (-1)).Distinct().Count() < 2;
	}

	private bool IsEligibleForMultiVersion(string folderName, string testFilename)
	{
		testFilename = Path.GetFileNameWithoutExtension(testFilename);
		if (string.Equals(folderName, testFilename, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (testFilename.StartsWith(folderName, StringComparison.OrdinalIgnoreCase))
		{
			testFilename = testFilename.Substring(folderName.Length).Trim();
			if (!testFilename.StartsWith("-", StringComparison.OrdinalIgnoreCase))
			{
				return Regex.Replace(testFilename, "\\[([^]]*)\\]", "").Trim() == string.Empty;
			}
			return true;
		}
		return false;
	}

	private List<VideoFileInfo> GetExtras(IEnumerable<VideoFileInfo> remainingFiles, List<string> baseNames)
	{
		foreach (string item2 in baseNames.ToList())
		{
			string item = item2.TrimEnd(Array.Empty<char>()).TrimEnd(_options.VideoFlagDelimiters).TrimEnd(Array.Empty<char>());
			baseNames.Add(item);
		}
		return (from i in remainingFiles
			where !string.IsNullOrEmpty(i.ExtraType)
			where baseNames.Any((string b) => i.FileNameWithoutExtension.StartsWith(b, StringComparison.OrdinalIgnoreCase))
			select i).ToList();
	}
}
