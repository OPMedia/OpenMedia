using System;
using System.IO;
using System.Linq;
using System.Threading;
using MediaBrowser.Common.Progress;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Controller.Channels;

public class Channel : Folder
{
	[IgnoreDataMember]
	public override bool SupportsInheritedParentImages => false;

	[IgnoreDataMember]
	public override SourceType SourceType => SourceType.Channel;

	public override bool IsVisible(User user)
	{
		if (user.Policy.BlockedChannels != null)
		{
			if (user.Policy.BlockedChannels.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		else if (!user.Policy.EnableAllChannels && !user.Policy.EnabledChannels.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		return base.IsVisible(user);
	}

	protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
	{
		try
		{
			query.Parent = this;
			query.ChannelIds = new Guid[1] { Id };
			return BaseItem.ChannelManager.GetChannelItemsInternal(query, (IProgress<double>)new SimpleProgress<double>(), CancellationToken.None).Result;
		}
		catch
		{
			return new QueryResult<BaseItem>();
		}
	}

	protected override string GetInternalMetadataPath(string basePath)
	{
		return GetInternalMetadataPath(basePath, Id);
	}

	public static string GetInternalMetadataPath(string basePath, Guid id)
	{
		return System.IO.Path.Combine(basePath, "channels", id.ToString("N"), "metadata");
	}

	public override bool CanDelete()
	{
		return false;
	}

	protected override bool IsAllowTagFilterEnforced()
	{
		return false;
	}

	internal static bool IsChannelVisible(BaseItem channelItem, User user)
	{
		return BaseItem.LibraryManager.GetItemById(channelItem.ChannelId).IsVisible(user);
	}
}
