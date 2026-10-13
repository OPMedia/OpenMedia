using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Entities;

public static class PeopleHelper
{
	public static void AddPerson(List<PersonInfo> people, PersonInfo person)
	{
		if (person == null)
		{
			throw new ArgumentNullException("person");
		}
		if (string.IsNullOrEmpty(person.Name))
		{
			throw new ArgumentNullException();
		}
		if (string.Equals(person.Role, "GuestStar", StringComparison.OrdinalIgnoreCase))
		{
			person.Type = "GuestStar";
		}
		else if (string.Equals(person.Role, "Director", StringComparison.OrdinalIgnoreCase))
		{
			person.Type = "Director";
		}
		else if (string.Equals(person.Role, "Producer", StringComparison.OrdinalIgnoreCase))
		{
			person.Type = "Producer";
		}
		else if (string.Equals(person.Role, "Writer", StringComparison.OrdinalIgnoreCase))
		{
			person.Type = "Writer";
		}
		if (string.Equals(person.Type, "GuestStar", StringComparison.OrdinalIgnoreCase))
		{
			PersonInfo personInfo = people.FirstOrDefault((PersonInfo p) => p.Name.Equals(person.Name, StringComparison.OrdinalIgnoreCase) && p.Type.Equals("Actor", StringComparison.OrdinalIgnoreCase));
			if (personInfo != null)
			{
				personInfo.Type = "GuestStar";
				MergeExisting(personInfo, person);
				return;
			}
		}
		if (string.Equals(person.Type, "Actor", StringComparison.OrdinalIgnoreCase))
		{
			PersonInfo personInfo2 = people.FirstOrDefault((PersonInfo p) => p.Name.Equals(person.Name, StringComparison.OrdinalIgnoreCase) && (p.Type.Equals("Actor", StringComparison.OrdinalIgnoreCase) || p.Type.Equals("GuestStar", StringComparison.OrdinalIgnoreCase)));
			if (personInfo2 == null)
			{
				people.Add(person);
				return;
			}
			if (string.IsNullOrEmpty(personInfo2.Role) && !string.IsNullOrEmpty(person.Role))
			{
				personInfo2.Role = person.Role;
			}
			MergeExisting(personInfo2, person);
		}
		else
		{
			PersonInfo personInfo3 = people.FirstOrDefault((PersonInfo p) => string.Equals(p.Name, person.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(p.Type, person.Type, StringComparison.OrdinalIgnoreCase));
			if (personInfo3 == null)
			{
				people.Add(person);
			}
			else
			{
				MergeExisting(personInfo3, person);
			}
		}
	}

	private static void MergeExisting(PersonInfo existing, PersonInfo person)
	{
		existing.SortOrder = person.SortOrder ?? existing.SortOrder;
		existing.ImageUrl = person.ImageUrl ?? existing.ImageUrl;
		foreach (KeyValuePair<string, string> providerId in person.ProviderIds)
		{
			ProviderIdsExtensions.SetProviderId((IHasProviderIds)(object)existing, providerId.Key, providerId.Value);
		}
	}

	public static bool ContainsPerson(List<PersonInfo> people, string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			throw new ArgumentNullException("name");
		}
		foreach (PersonInfo person in people)
		{
			if (string.Equals(person.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
