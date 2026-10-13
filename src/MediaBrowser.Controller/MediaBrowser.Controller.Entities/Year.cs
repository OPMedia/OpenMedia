using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Entities;

public class Year : BaseItem, IItemByName
{
	[IgnoreDataMember]
	public override string ContainingFolderPath => Path;

	[IgnoreDataMember]
	public override bool SupportsAncestors => false;

	[IgnoreDataMember]
	public override bool SupportsPeople => false;

	public override List<string> GetUserDataKeys()
	{
		List<string> userDataKeys = base.GetUserDataKeys();
		userDataKeys.Insert(0, "Year-" + Name);
		return userDataKeys;
	}

	public override double GetDefaultPrimaryImageAspectRatio()
	{
		return 2.0 / 3.0;
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
		CultureInfo provider = new CultureInfo("en-US");
		if (!int.TryParse(Name, NumberStyles.Integer, provider, out var result))
		{
			return new List<BaseItem>();
		}
		query.Years = new int[1] { result };
		return BaseItem.LibraryManager.GetItemList(query);
	}

	public int? GetYearValue()
	{
		if (int.TryParse(Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return result;
		}
		return null;
	}

	public static string GetPath(string name)
	{
		return GetPath(name, normalizeName: true);
	}

	public static string GetPath(string name, bool normalizeName)
	{
		string path = (normalizeName ? BaseItem.FileSystem.GetValidFilename(name).Trim().TrimEnd(new char[1] { '.' }) : name);
		return System.IO.Path.Combine(BaseItem.ConfigurationManager.ApplicationPaths.YearPath, path);
	}
}
