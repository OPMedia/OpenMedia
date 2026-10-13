using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;

namespace MediaBrowser.Controller.LiveTv;

public interface ITunerHost
{
	string Name { get; }

	string Type { get; }

	bool IsSupported { get; }

	Task<List<ChannelInfo>> GetChannels(bool enableCache, CancellationToken cancellationToken);

	Task<ILiveStream> GetChannelStream(string channelId, string streamId, List<ILiveStream> currentLiveStreams, CancellationToken cancellationToken);

	Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken);

	Task<List<TunerHostInfo>> DiscoverDevices(int discoveryDurationMs, CancellationToken cancellationToken);
}
