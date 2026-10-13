namespace MediaBrowser.Controller.Library;

public class PlaybackStopEventArgs : PlaybackProgressEventArgs
{
	public bool PlayedToCompletion { get; set; }
}
