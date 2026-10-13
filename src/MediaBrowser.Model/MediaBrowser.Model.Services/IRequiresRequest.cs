namespace MediaBrowser.Model.Services;

public interface IRequiresRequest
{
	IRequest Request { get; set; }
}
