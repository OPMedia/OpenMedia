using Emby.Naming.Common;

namespace Emby.Naming.Video;

public class ExtraRule
{
	public string Token { get; set; }

	public string ExtraType { get; set; }

	public ExtraRuleType RuleType { get; set; }

	public MediaType MediaType { get; set; }
}
