namespace MediaBrowser.Model.Dto;

public class NameValuePair
{
	public string Name { get; set; }

	public string Value { get; set; }

	public NameValuePair()
	{
	}

	public NameValuePair(string name, string value)
	{
		Name = name;
		Value = value;
	}
}
