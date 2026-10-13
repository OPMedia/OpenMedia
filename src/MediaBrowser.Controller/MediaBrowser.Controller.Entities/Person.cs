using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class Person : BaseItem, IItemByName, IHasLookupInfo<PersonLookupInfo>
{
	[IgnoreDataMember]
	public override string ContainingFolderPath => Path;

	[IgnoreDataMember]
	public override bool EnableAlphaNumericSorting => false;

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	[IgnoreDataMember]
	public override bool SupportsAncestors => false;

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		userDataKeys.Insert(0, GetType().Name + "-" + (Name ?? string.Empty).RemoveDiacritics());
		return userDataKeys;
	}

	public override string CreatePresentationUniqueKey()
	{
		return GetUserDataKeys()[0];
	}

	public PersonLookupInfo GetLookupInfo()
	{
		return GetItemLookupInfo<PersonLookupInfo>();
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 2.0 / 3.0;
	}

	public IList<BaseItem> GetTaggedItems(InternalItemsQuery query)
	{
		query.PersonIds = new Guid[1] { Id };
		return BaseItem.LibraryManager.GetItemList(query);
	}

	public override bool CanDelete()
	{
		return false;
	}

	public override bool IsSaveLocalMetadataEnabled()
	{
		return true;
	}

	public static string GetPath(string name)
	{
		return GetPath(name, normalizeName: true);
	}

	public static string GetPath(string name, bool normalizeName)
	{
		string text = (normalizeName ? BaseItem.FileSystem.GetValidFilename(name).Trim().TrimEnd(new char[1] { '.' }) : name);
		string text2 = null;
		string text3 = text;
		for (int i = 0; i < text3.Length; i++)
		{
			char c = text3[i];
			if (char.IsLetterOrDigit(c))
			{
				text2 = c.ToString();
				break;
			}
		}
		string peoplePath = BaseItem.ConfigurationManager.ApplicationPaths.PeoplePath;
		if (!string.IsNullOrEmpty(text2))
		{
			return System.IO.Path.Combine(peoplePath, text2, text);
		}
		return System.IO.Path.Combine(peoplePath, text);
	}
}
