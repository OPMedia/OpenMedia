using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Providers;

public class MetadataResult<T>
{
	public List<LocalImageInfo> Images { get; set; }

	public List<UserItemData> UserDataList { get; set; }

	public List<PersonInfo> People { get; set; }

	public bool HasMetadata { get; set; }

	public T Item { get; set; }

	public string ResultLanguage { get; set; }

	public string Provider { get; set; }

	public bool QueriedById { get; set; }

	public MetadataResult()
	{
		Images = new List<LocalImageInfo>();
		ResultLanguage = "en";
	}

	public void AddPerson(PersonInfo p)
	{
		if (People == null)
		{
			People = new List<PersonInfo>();
		}
		PeopleHelper.AddPerson(People, p);
	}

	public void ResetPeople()
	{
		if (People == null)
		{
			People = new List<PersonInfo>();
		}
		People.Clear();
	}
}
