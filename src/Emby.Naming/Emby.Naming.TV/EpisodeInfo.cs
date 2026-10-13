namespace Emby.Naming.TV;

public class EpisodeInfo
{
	public string Path { get; set; }

	public string Container { get; set; }

	public string SeriesName { get; set; }

	public string Format3D { get; set; }

	public bool Is3D { get; set; }

	public bool IsStub { get; set; }

	public string StubType { get; set; }

	public int? SeasonNumber { get; set; }

	public int? EpisodeNumber { get; set; }

	public int? EndingEpsiodeNumber { get; set; }

	public int? Year { get; set; }

	public int? Month { get; set; }

	public int? Day { get; set; }

	public bool IsByDate { get; set; }
}
