using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Dto;

public class DtoOptions
{
	private static readonly ItemFields[] DefaultExcludedFields = new ItemFields[2]
	{
		(ItemFields)39,
		(ItemFields)47
	};

	private Dictionary<ItemFields, bool> _fieldsDictionary;

	private ItemFields[] _fields;

	private static readonly ImageType[] AllImageTypes = ((IEnumerable<string>)Enum.GetNames(typeof(ImageType))).Select((Func<string, ImageType>)((string i) =>
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		return (ImageType)Enum.Parse(typeof(ImageType), i, ignoreCase: true);
	})).ToArray();

	private static readonly ItemFields[] AllItemFields = ((IEnumerable<string>)Enum.GetNames(typeof(ItemFields))).Select((Func<string, ItemFields>)((string i) =>
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		return (ItemFields)Enum.Parse(typeof(ItemFields), i, ignoreCase: true);
	})).Except(DefaultExcludedFields).ToArray();

	public ItemFields[] Fields
	{
		get
		{
			return _fields;
		}
		set
		{
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			_fields = value;
			Dictionary<ItemFields, bool> dictionary = new Dictionary<ItemFields, bool>();
			foreach (ItemFields key in value)
			{
				dictionary[key] = true;
			}
			_fieldsDictionary = dictionary;
		}
	}

	public ImageType[] ImageTypes { get; set; }

	public int ImageTypeLimit { get; set; }

	public bool EnableImages { get; set; }

	public bool AddProgramRecordingInfo { get; set; }

	public bool EnableUserData { get; set; }

	public bool AddCurrentProgram { get; set; }

	public DtoOptions()
		: this(allFields: true)
	{
	}

	public bool ContainsField(ItemFields field)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		return _fieldsDictionary.ContainsKey(field);
	}

	public DtoOptions(bool allFields)
	{
		ImageTypeLimit = int.MaxValue;
		EnableImages = true;
		EnableUserData = true;
		AddCurrentProgram = true;
		if (allFields)
		{
			Fields = AllItemFields;
		}
		else
		{
			Fields = new ItemFields[0];
		}
		ImageTypes = AllImageTypes;
	}

	public int GetImageLimit(ImageType type)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		if (EnableImages && Enumerable.Contains(ImageTypes, type))
		{
			return ImageTypeLimit;
		}
		return 0;
	}
}
