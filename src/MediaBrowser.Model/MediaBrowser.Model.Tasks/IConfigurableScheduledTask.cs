namespace MediaBrowser.Model.Tasks;

public interface IConfigurableScheduledTask
{
	bool IsHidden { get; }

	bool IsEnabled { get; }

	bool IsLogged { get; }
}
