using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Library;

public class UserDataSaveEventArgs : EventArgs
{
	public Guid UserId { get; set; }

	public List<string> Keys { get; set; }

	public UserDataSaveReason SaveReason
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

	public UserItemData UserData { get; set; }

	public BaseItem Item { get; set; }
}
