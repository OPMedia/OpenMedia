using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Naming.Common;
using MediaBrowser.Model.IO;

namespace Emby.Naming.Video;

public class StackResolver
{
	private readonly NamingOptions _options;

	public StackResolver(NamingOptions options)
	{
		_options = options;
	}

	public StackResult ResolveDirectories(IEnumerable<string> files)
	{
		return Resolve(files.Select((Func<string, FileSystemMetadata>)((string i) =>
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Expected Obj, but got Unknown
			return new FileSystemMetadata
			{
				FullName = i,
				IsDirectory = true
			};
		})));
	}

	public StackResult ResolveFiles(IEnumerable<string> files)
	{
		return Resolve(files.Select((Func<string, FileSystemMetadata>)((string i) =>
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Expected Obj, but got Unknown
			return new FileSystemMetadata
			{
				FullName = i,
				IsDirectory = false
			};
		})));
	}

	public StackResult ResolveAudioBooks(IEnumerable<FileSystemMetadata> files)
	{
		StackResult stackResult = new StackResult();
		foreach (IGrouping<string, FileSystemMetadata> item in from file in files
			group file by (!file.IsDirectory) ? Path.GetDirectoryName(file.FullName) : file.FullName)
		{
			FileStack fileStack = new FileStack();
			fileStack.Name = Path.GetFileName(item.Key);
			fileStack.IsDirectoryStack = false;
			foreach (FileSystemMetadata item2 in item)
			{
				if (!item2.IsDirectory)
				{
					fileStack.Files.Add(item2.FullName);
				}
			}
			stackResult.Stacks.Add(fileStack);
		}
		return stackResult;
	}

	public StackResult Resolve(IEnumerable<FileSystemMetadata> files)
	{
		StackResult stackResult = new StackResult();
		VideoResolver resolver = new VideoResolver(_options);
		List<FileSystemMetadata> list = (from i in files
			where i.IsDirectory || resolver.IsVideoFile(i.FullName) || resolver.IsStubFile(i.FullName)
			orderby i.FullName
			select i).ToList();
		Regex[] videoFileStackingRegexes = _options.VideoFileStackingRegexes;
		for (int num = 0; num < list.Count; num++)
		{
			int offset = 0;
			FileSystemMetadata val = list[num];
			int num2 = 0;
			while (num2 < videoFileStackingRegexes.Length)
			{
				Regex regex = videoFileStackingRegexes[num2];
				FileStack fileStack = new FileStack();
				Match match = FindMatch(val, regex, offset);
				if (match.Success)
				{
					string value = match.Groups[1].Value;
					string value2 = match.Groups[2].Value;
					string value3 = match.Groups[3].Value;
					string value4 = match.Groups[4].Value;
					int num3 = num + 1;
					while (num3 < list.Count)
					{
						FileSystemMetadata val2 = list[num3];
						if (val.IsDirectory != val2.IsDirectory)
						{
							num3++;
							continue;
						}
						Match match2 = FindMatch(val2, regex, offset);
						if (match2.Success)
						{
							string value5 = match2.Groups[1].Value;
							string value6 = match2.Groups[2].Value;
							string value7 = match2.Groups[3].Value;
							string value8 = match2.Groups[4].Value;
							if (string.Equals(value, value5, StringComparison.OrdinalIgnoreCase))
							{
								if (!string.Equals(value2, value6, StringComparison.OrdinalIgnoreCase))
								{
									if (string.Equals(value3, value7, StringComparison.OrdinalIgnoreCase) && string.Equals(value4, value8, StringComparison.OrdinalIgnoreCase))
									{
										if (fileStack.Files.Count == 0)
										{
											fileStack.Name = value + value3;
											fileStack.IsDirectoryStack = val.IsDirectory;
											fileStack.Files.Add(val.FullName);
										}
										fileStack.Files.Add(val2.FullName);
										num3++;
										continue;
									}
									offset = 0;
									num2++;
									break;
								}
								if (!string.Equals(value3, value7, StringComparison.OrdinalIgnoreCase))
								{
									offset = match.Groups[3].Index;
									break;
								}
								offset = 0;
								num2++;
								break;
							}
							offset = 0;
							num2++;
							break;
						}
						offset = 0;
						num2++;
						break;
					}
					if (num3 == list.Count)
					{
						num2 = videoFileStackingRegexes.Length;
					}
				}
				else
				{
					offset = 0;
					num2++;
				}
				if (fileStack.Files.Count > 1)
				{
					stackResult.Stacks.Add(fileStack);
					num += fileStack.Files.Count - 1;
					break;
				}
			}
		}
		return stackResult;
	}

	private string GetRegexInput(FileSystemMetadata file)
	{
		return Path.GetFileName((!file.IsDirectory) ? file.FullName : (file.FullName + ".mkv"));
	}

	private Match FindMatch(FileSystemMetadata input, Regex regex, int offset)
	{
		string regexInput = GetRegexInput(input);
		if (offset < 0 || offset >= regexInput.Length)
		{
			return Match.Empty;
		}
		return regex.Match(regexInput, offset);
	}
}
