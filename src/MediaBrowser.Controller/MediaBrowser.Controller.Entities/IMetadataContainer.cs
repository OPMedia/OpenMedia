using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Providers;

namespace MediaBrowser.Controller.Entities;

public interface IMetadataContainer
{
	Task RefreshAllMetadata(MetadataRefreshOptions refreshOptions, IProgress<double> progress, CancellationToken cancellationToken);
}
