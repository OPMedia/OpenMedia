using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Querying;

namespace MediaBrowser.Controller.LiveTv;

public interface ILiveTvManager
{
	IReadOnlyList<ILiveTvService> Services { get; }

	IListingsProvider[] ListingProviders { get; }

	event EventHandler<GenericEventArgs<TimerEventInfo>> SeriesTimerCancelled;

	event EventHandler<GenericEventArgs<TimerEventInfo>> TimerCancelled;

	event EventHandler<GenericEventArgs<TimerEventInfo>> TimerCreated;

	event EventHandler<GenericEventArgs<TimerEventInfo>> SeriesTimerCreated;

	Task<SeriesTimerInfoDto> GetNewTimerDefaults(CancellationToken cancellationToken);

	Task<SeriesTimerInfoDto> GetNewTimerDefaults(string programId, CancellationToken cancellationToken);

	Task CancelTimer(string id);

	Task CancelSeriesTimer(string id);

	void AddParts(IEnumerable<ILiveTvService> services, IEnumerable<ITunerHost> tunerHosts, IEnumerable<IListingsProvider> listingProviders);

	Task<TimerInfoDto> GetTimer(string id, CancellationToken cancellationToken);

	Task<SeriesTimerInfoDto> GetSeriesTimer(string id, CancellationToken cancellationToken);

	QueryResult<BaseItemDto> GetRecordings(RecordingQuery query, DtoOptions options);

	Task<QueryResult<TimerInfoDto>> GetTimers(TimerQuery query, CancellationToken cancellationToken);

	Task<QueryResult<SeriesTimerInfoDto>> GetSeriesTimers(SeriesTimerQuery query, CancellationToken cancellationToken);

	Task<Tuple<MediaSourceInfo, ILiveStream>> GetChannelStream(string id, string mediaSourceId, List<ILiveStream> currentLiveStreams, CancellationToken cancellationToken);

	Task<BaseItemDto> GetProgram(string id, CancellationToken cancellationToken, User user = null);

	Task<QueryResult<BaseItemDto>> GetPrograms(InternalItemsQuery query, DtoOptions options, CancellationToken cancellationToken);

	Task UpdateTimer(TimerInfoDto timer, CancellationToken cancellationToken);

	Task UpdateSeriesTimer(SeriesTimerInfoDto timer, CancellationToken cancellationToken);

	Task CreateTimer(TimerInfoDto timer, CancellationToken cancellationToken);

	Task CreateSeriesTimer(SeriesTimerInfoDto timer, CancellationToken cancellationToken);

	GuideInfo GetGuideInfo();

	QueryResult<BaseItemDto> GetRecommendedPrograms(InternalItemsQuery query, DtoOptions options, CancellationToken cancellationToken);

	LiveTvInfo GetLiveTvInfo(CancellationToken cancellationToken);

	Task ResetTuner(string id, CancellationToken cancellationToken);

	Folder GetInternalLiveTvFolder(CancellationToken cancellationToken);

	IEnumerable<User> GetEnabledUsers();

	QueryResult<BaseItem> GetInternalChannels(LiveTvChannelQuery query, DtoOptions dtoOptions, CancellationToken cancellationToken);

	Task<IEnumerable<MediaSourceInfo>> GetChannelMediaSources(BaseItem item, CancellationToken cancellationToken);

	Task AddInfoToProgramDto(List<Tuple<BaseItem, BaseItemDto>> programs, ItemFields[] fields, User user = null);

	Task<TunerHostInfo> SaveTunerHost(TunerHostInfo info, bool dataSourceChanged = true);

	Task<ListingsProviderInfo> SaveListingProvider(ListingsProviderInfo info, bool validateLogin, bool validateListings);

	void DeleteListingsProvider(string id);

	Task<TunerChannelMapping> SetChannelMapping(string providerId, string tunerChannelNumber, string providerChannelNumber);

	TunerChannelMapping GetTunerChannelMapping(ChannelInfo channel, NameValuePair[] mappings, List<ChannelInfo> providerChannels);

	Task<List<NameIdPair>> GetLineups(string providerType, string providerId, string country, string location);

	void AddChannelInfo(List<Tuple<BaseItemDto, LiveTvChannel>> items, DtoOptions options, User user);

	Task<List<ChannelInfo>> GetChannelsForListingsProvider(string id, CancellationToken cancellationToken);

	Task<List<ChannelInfo>> GetChannelsFromListingsProviderData(string id, CancellationToken cancellationToken);

	List<NameIdPair> GetTunerHostTypes();

	Task<List<TunerHostInfo>> DiscoverTuners(bool newDevicesOnly, CancellationToken cancellationToken);

	string GetEmbyTvActiveRecordingPath(string id);

	ActiveRecordingInfo GetActiveRecordingInfo(string path);

	void AddInfoToRecordingDto(BaseItem item, BaseItemDto dto, ActiveRecordingInfo activeRecordingInfo, User user = null);

	List<BaseItem> GetRecordingFolders(User user);
}
