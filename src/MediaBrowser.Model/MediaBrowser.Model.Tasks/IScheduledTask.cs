using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Model.Tasks;

public interface IScheduledTask
{
	string Name { get; }

	string Key { get; }

	string Description { get; }

	string Category { get; }

	Task Execute(CancellationToken cancellationToken, IProgress<double> progress);

	IEnumerable<TaskTriggerInfo> GetDefaultTriggers();
}
