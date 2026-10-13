using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Controller.LiveTv;

public interface ILiveTvService
{
	string Name { get; }

	string HomePageUrl { get; }

	event EventHandler DataSourceChanged;

	Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken);

	Task CancelTimerAsync(string timerId, CancellationToken cancellationToken);

	Task CancelSeriesTimerAsync(string timerId, CancellationToken cancellationToken);

	Task CreateTimerAsync(TimerInfo info, CancellationToken cancellationToken);

	Task CreateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken);

	Task UpdateTimerAsync(TimerInfo info, CancellationToken cancellationToken);

	Task UpdateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken);

	Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken);

	Task<SeriesTimerInfo> GetNewTimerDefaultsAsync(CancellationToken cancellationToken, ProgramInfo program = null);

	Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken);

	Task<IEnumerable<ProgramInfo>> GetProgramsAsync(string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken);

	Task<MediaSourceInfo> GetChannelStream(string channelId, string streamId, CancellationToken cancellationToken);

	Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken);

	Task CloseLiveStream(string id, CancellationToken cancellationToken);

	Task RecordLiveStream(string id, CancellationToken cancellationToken);

	Task ResetTuner(string id, CancellationToken cancellationToken);
}
