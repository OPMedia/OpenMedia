using System;
using System.Runtime.CompilerServices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Providers;

public class ItemInfo
{
	public Type ItemType { get; set; }

	public string Path { get; set; }

	public string ContainingFolderPath { get; set; }

	public VideoType VideoType
	{
		[CompilerGenerated]
		get
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return field;
		}
		[CompilerGenerated]
		set
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			field = value;
		}
	}

	public bool IsInMixedFolder { get; set; }

	public bool IsPlaceHolder { get; set; }

	public ItemInfo(BaseItem item)
	{
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		Path = item.Path;
		ContainingFolderPath = item.ContainingFolderPath;
		IsInMixedFolder = item.IsInMixedFolder;
		if (item is Video video)
		{
			VideoType = video.VideoType;
			IsPlaceHolder = video.IsPlaceHolder;
		}
		ItemType = item.GetType();
	}
}
