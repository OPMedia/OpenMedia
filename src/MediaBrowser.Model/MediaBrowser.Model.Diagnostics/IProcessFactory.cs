namespace MediaBrowser.Model.Diagnostics;

public interface IProcessFactory
{
	IProcess Create(ProcessOptions options);
}
