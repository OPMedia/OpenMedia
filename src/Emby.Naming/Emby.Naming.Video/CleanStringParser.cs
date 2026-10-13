using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Emby.Naming.Video;

public class CleanStringParser
{
	public CleanStringResult Clean(string name, IEnumerable<Regex> expressions)
	{
		bool flag = false;
		foreach (Regex expression in expressions)
		{
			CleanStringResult cleanStringResult = Clean(name, expression);
			if (!string.IsNullOrEmpty(cleanStringResult.Name))
			{
				name = cleanStringResult.Name;
				flag = flag || cleanStringResult.HasChanged;
			}
		}
		return new CleanStringResult
		{
			Name = name,
			HasChanged = flag
		};
	}

	private CleanStringResult Clean(string name, Regex expression)
	{
		CleanStringResult cleanStringResult = new CleanStringResult();
		Match match = expression.Match(name);
		if (match.Success)
		{
			cleanStringResult.HasChanged = true;
			name = name.Substring(0, match.Index);
		}
		cleanStringResult.Name = name;
		return cleanStringResult;
	}
}
