namespace MediaBrowser.Common.Configuration;

public interface IValidatingConfiguration
{
	void Validate(object oldConfig, object newConfig);
}
