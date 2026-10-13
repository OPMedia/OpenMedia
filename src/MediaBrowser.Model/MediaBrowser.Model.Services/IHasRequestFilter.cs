namespace MediaBrowser.Model.Services;

public interface IHasRequestFilter
{
	int Priority { get; }

	void RequestFilter(IRequest req, IResponse res, object requestDto);
}
