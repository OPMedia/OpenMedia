using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Controller.Library;

public interface IMediaSourceManager
{
	void AddParts(IEnumerable<IMediaSourceProvider> providers);

	List<MediaStream> GetMediaStreams(Guid itemId);

	List<MediaStream> GetMediaStreams(string mediaSourceId);

	List<MediaStream> GetMediaStreams(MediaStreamQuery query);

	Task<List<MediaSourceInfo>> GetPlayackMediaSources(BaseItem item, User user, bool allowMediaProbe, bool enablePathSubstitution, CancellationToken cancellationToken);

	List<MediaSourceInfo> GetStaticMediaSources(BaseItem item, bool enablePathSubstitution, User user = null);

	Task<MediaSourceInfo> GetMediaSource(BaseItem item, string mediaSourceId, string liveStreamId, bool enablePathSubstitution, CancellationToken cancellationToken);

	Task<LiveStreamResponse> OpenLiveStream(LiveStreamRequest request, CancellationToken cancellationToken);

	Task<Tuple<LiveStreamResponse, IDirectStreamProvider>> OpenLiveStreamInternal(LiveStreamRequest request, CancellationToken cancellationToken);

	Task<MediaSourceInfo> GetLiveStream(string id, CancellationToken cancellationToken);

	Task<Tuple<MediaSourceInfo, IDirectStreamProvider>> GetLiveStreamWithDirectStreamProvider(string id, CancellationToken cancellationToken);

	Task CloseLiveStream(string id);

	Task<MediaSourceInfo> GetLiveStreamMediaInfo(string id, CancellationToken cancellationToken);

	bool SupportsDirectStream(string path, MediaProtocol protocol);

	MediaProtocol GetPathProtocol(string path);

	void SetDefaultAudioAndSubtitleStreamIndexes(BaseItem item, MediaSourceInfo source, User user);

	Task AddMediaInfoWithProbe(MediaSourceInfo mediaSource, bool isAudio, string cacheKey, bool addProbeDelay, bool isLiveStream, CancellationToken cancellationToken);

	Task<IDirectStreamProvider> GetDirectStreamProviderByUniqueId(string uniqueId, CancellationToken cancellationToken);
}
