namespace MediaBrowser.Controller.Channels;

public interface IHasCacheKey
{
	string GetCacheKey(string userId);
}
