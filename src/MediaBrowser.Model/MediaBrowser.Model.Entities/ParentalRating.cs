namespace MediaBrowser.Model.Entities;

public class ParentalRating
{
	public string Name { get; set; }

	public int Value { get; set; }

	public ParentalRating()
	{
	}

	public ParentalRating(string name, int value)
	{
		Name = name;
		Value = value;
	}
}
