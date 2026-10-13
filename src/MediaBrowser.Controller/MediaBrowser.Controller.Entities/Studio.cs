using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class Studio : BaseItem, IItemByName
{
	[IgnoreDataMember]
	public override string ContainingFolderPath => Path;

	[IgnoreDataMember]
	public override bool IsDisplayedAsFolder => true;

	[IgnoreDataMember]
	public override bool SupportsAncestors => false;

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

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

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 16.0 / 9.0;
	}

	public override bool CanDelete()
	{
		return false;
	}

	public override bool IsSaveLocalMetadataEnabled()
	{
		return true;
	}

	public IList<BaseItem> GetTaggedItems(InternalItemsQuery query)
	{
		query.StudioIds = new Guid[1] { Id };
		return BaseItem.LibraryManager.GetItemList(query);
	}

	public static string GetPath(string name)
	{
		return GetPath(name, normalizeName: true);
	}

	public static string GetPath(string name, bool normalizeName)
	{
		string path = (normalizeName ? BaseItem.FileSystem.GetValidFilename(name).Trim().TrimEnd(new char[1] { '.' }) : name);
		return System.IO.Path.Combine(BaseItem.ConfigurationManager.ApplicationPaths.StudioPath, path);
	}
}
