using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.Channels;

public interface IChannelManager
{
	void AddParts(IEnumerable<IChannel> channels);

	ChannelFeatures GetChannelFeatures(string id);

	bool SupportsExternalTransfer(Guid channelId);

	ChannelFeatures[] GetAllChannelFeatures();

	bool EnableMediaSourceDisplay(BaseItem item);

	bool CanDelete(BaseItem item);

	Task DeleteItem(BaseItem item);

	Channel GetChannel(string id);

	QueryResult<Channel> GetChannelsInternal(ChannelQuery query);

	QueryResult<BaseItemDto> GetChannels(ChannelQuery query);

	Task<QueryResult<BaseItemDto>> GetLatestChannelItems(InternalItemsQuery query, CancellationToken cancellationToken);

	Task<QueryResult<BaseItem>> GetLatestChannelItemsInternal(InternalItemsQuery query, CancellationToken cancellationToken);

	Task<QueryResult<BaseItemDto>> GetChannelItems(InternalItemsQuery query, CancellationToken cancellationToken);

	Task<QueryResult<BaseItem>> GetChannelItemsInternal(InternalItemsQuery query, IProgress<double> progress, CancellationToken cancellationToken);

	IEnumerable<MediaSourceInfo> GetStaticMediaSources(BaseItem item, CancellationToken cancellationToken);

	bool EnableMediaProbe(BaseItem item);
}
