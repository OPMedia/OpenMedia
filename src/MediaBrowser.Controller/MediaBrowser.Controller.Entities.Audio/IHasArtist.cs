namespace MediaBrowser.Controller.Entities.Audio;

public interface IHasArtist
{
	string[] AllArtists { get; }

	string[] Artists { get; set; }
}
