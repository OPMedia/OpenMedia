using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Model.Dto;

public class BaseItemPerson
{
	public string Name { get; set; }

	public string Id { get; set; }

	public string Role { get; set; }

	public string Type { get; set; }

	public string PrimaryImageTag { get; set; }

	[IgnoreDataMember]
	public bool HasPrimaryImage => PrimaryImageTag != null;
}
