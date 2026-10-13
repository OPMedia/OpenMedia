using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.System;

namespace Emby.Server.MediaEncoding.Api.Progressive;

public class ProgressiveFileCopier : IAsyncStreamWriter, IHasHeaders
{
	private readonly IFileSystem _fileSystem;

	private readonly TranscodingJob _job;

	private readonly ILogger _logger;

	private readonly string _path;

	private readonly Dictionary<string, string> _outputHeaders;

	private const int StreamCopyToBufferSize = 81920;

	private long _bytesWritten;

	public bool AllowEndOfFile = true;

	private readonly IDirectStreamProvider _directStreamProvider;

	private readonly IEnvironmentInfo _environment;

	public long StartPosition { get; set; }

	public IDictionary<string, string> Headers => _outputHeaders;

	public ProgressiveFileCopier(IFileSystem fileSystem, string path, Dictionary<string, string> outputHeaders, TranscodingJob job, ILogger logger, IEnvironmentInfo environment)
	{
		_fileSystem = fileSystem;
		_path = path;
		_outputHeaders = outputHeaders;
		_job = job;
		_logger = logger;
		_environment = environment;
	}

	public ProgressiveFileCopier(IDirectStreamProvider directStreamProvider, Dictionary<string, string> outputHeaders, TranscodingJob job, ILogger logger, IEnvironmentInfo environment)
	{
		_directStreamProvider = directStreamProvider;
		_outputHeaders = outputHeaders;
		_job = job;
		_logger = logger;
		_environment = environment;
	}

	private Stream GetInputStream(bool allowAsyncFileRead)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		FileOpenOptions val = (FileOpenOptions)134217728;
		if (allowAsyncFileRead)
		{
			val = (FileOpenOptions)((int)val | 0x40000000);
		}
		return _fileSystem.GetFileStream(_path, (FileOpenMode)3, (FileAccessMode)1, (FileShareMode)3, val);
	}

	public async Task WriteToAsync(Stream outputStream, CancellationToken cancellationToken)
	{
		_ = 3;
		try
		{
			if (_directStreamProvider != null)
			{
				await _directStreamProvider.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				return;
			}
			int eofCount = 0;
			bool allowAsyncFileRead = (int)_environment.OperatingSystem > 0;
			using Stream inputStream = GetInputStream(allowAsyncFileRead);
			if (StartPosition > 0)
			{
				inputStream.Position = StartPosition;
			}
			int emptyReadLimit = (AllowEndOfFile ? 20 : 100);
			while (eofCount < emptyReadLimit)
			{
				if (((!allowAsyncFileRead) ? (await CopyToInternalAsyncWithSyncRead(inputStream, outputStream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)) : (await CopyToInternalAsync(inputStream, outputStream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))) == 0)
				{
					if (_job == null || _job.HasExited)
					{
						eofCount++;
					}
					await Task.Delay(100, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				else
				{
					eofCount = 0;
				}
			}
		}
		finally
		{
			if (_job != null)
			{
				ApiEntryPoint.Instance.OnTranscodeEndRequest(_job);
			}
		}
	}

	private async Task<int> CopyToInternalAsyncWithSyncRead(Stream source, Stream destination, CancellationToken cancellationToken)
	{
		byte[] array = new byte[81920];
		int totalBytesRead = 0;
		while (true)
		{
			int num;
			int bytesRead = (num = source.Read(array, 0, array.Length));
			if (num == 0)
			{
				break;
			}
			int num2 = bytesRead;
			if (num2 > 0)
			{
				await destination.WriteAsync(array, 0, Convert.ToInt32(num2), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				_bytesWritten += bytesRead;
				totalBytesRead += bytesRead;
				if (_job != null)
				{
					_job.BytesDownloaded = Math.Max(_job.BytesDownloaded ?? _bytesWritten, _bytesWritten);
				}
			}
		}
		return totalBytesRead;
	}

	private async Task<int> CopyToInternalAsync(Stream source, Stream destination, CancellationToken cancellationToken)
	{
		byte[] array = new byte[81920];
		int totalBytesRead = 0;
		while (true)
		{
			int num;
			int bytesRead = (num = await source.ReadAsync(array, 0, array.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));
			if (num == 0)
			{
				break;
			}
			int num2 = bytesRead;
			if (num2 > 0)
			{
				await destination.WriteAsync(array, 0, Convert.ToInt32(num2), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				_bytesWritten += bytesRead;
				totalBytesRead += bytesRead;
				if (_job != null)
				{
					_job.BytesDownloaded = Math.Max(_job.BytesDownloaded ?? _bytesWritten, _bytesWritten);
				}
			}
		}
		return totalBytesRead;
	}
}
