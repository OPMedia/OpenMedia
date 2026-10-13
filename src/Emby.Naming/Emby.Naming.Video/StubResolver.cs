using System;
using System.IO;
using System.Linq;
using Emby.Naming.Common;

namespace Emby.Naming.Video;

public class StubResolver
{
	private readonly NamingOptions _options;

	public StubResolver(NamingOptions options)
	{
		_options = options;
	}

	public StubResult ResolveFile(string path)
	{
		StubResult stubResult = new StubResult();
		string value = Path.GetExtension(path) ?? string.Empty;
		if (_options.StubFileExtensions.Contains(value, StringComparer.OrdinalIgnoreCase))
		{
			stubResult.IsStub = true;
			path = Path.GetFileNameWithoutExtension(path);
			string text = (Path.GetExtension(path) ?? string.Empty).TrimStart(new char[1] { '.' });
			StubTypeRule[] stubTypes = _options.StubTypes;
			foreach (StubTypeRule stubTypeRule in stubTypes)
			{
				if (string.Equals(stubTypeRule.Token, text, StringComparison.OrdinalIgnoreCase))
				{
					stubResult.StubType = stubTypeRule.StubType;
					stubResult.Tokens.Add(text);
					break;
				}
			}
		}
		return stubResult;
	}
}
