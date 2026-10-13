using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Channels;

public interface IChannel
{
	string Name { get; }

	string Description { get; }

	string DataVersion { get; }

	string HomePageUrl { get; }

	ChannelParentalRating ParentalRating { get; }

	InternalChannelFeatures GetChannelFeatures();

	bool IsEnabledFor(string userId);

	Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken);

	Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken);

	IEnumerable<ImageType> GetSupportedChannelImages();
}
