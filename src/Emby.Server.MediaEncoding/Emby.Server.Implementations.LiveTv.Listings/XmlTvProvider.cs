using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.XmlTv.Classes;
using Emby.XmlTv.Entities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Progress;
using MediaBrowser.Common.Security;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Logging;

namespace Emby.Server.Implementations.LiveTv.Listings;

public class XmlTvProvider : IListingsProvider
{
	private readonly IServerConfigurationManager _config;

	private readonly IHttpClient _httpClient;

	private readonly ILogger _logger;

	private readonly IFileSystem _fileSystem;

	private readonly IZipClient _zipClient;

	private ISecurityManager _securityManager;

	private SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(1, 1);

	public string Name => "XmlTV";

	public string Type => "xmltv";

	public XmlTvProvider(IServerConfigurationManager config, IHttpClient httpClient, ILogger logger, IFileSystem fileSystem, IZipClient zipClient, ISecurityManager securityManager)
	{
		_config = config;
		_httpClient = httpClient;
		_logger = logger;
		_fileSystem = fileSystem;
		_zipClient = zipClient;
		_securityManager = securityManager;
	}

	private string GetLanguage(ListingsProviderInfo info)
	{
		if (!string.IsNullOrEmpty(info.PreferredLanguage))
		{
			return info.PreferredLanguage;
		}
		return _config.Configuration.PreferredMetadataLanguage;
	}

	private async Task<string> GetXml(ListingsProviderInfo info, string path, CancellationToken cancellationToken)
	{
		_logger.Info("xmltv path: {0}", new object[1] { path });
		if (!path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			return UnzipIfNeeded(path, path);
		}
		await _downloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		DateTime utcNow = DateTime.UtcNow;
		double num = utcNow.Minute;
		num /= 5.0;
		string path2 = BaseExtensions.GetMD5(path).ToString("N") + "-" + utcNow.DayOfYear.ToString(CultureInfo.InvariantCulture) + "-" + utcNow.Hour.ToString(CultureInfo.InvariantCulture) + "-" + Math.Floor(num).ToString(CultureInfo.InvariantCulture) + ".xml";
		string text = Path.Combine(((IApplicationPaths)_config.ApplicationPaths).CachePath, "xmltv", path2);
		if (_fileSystem.FileExists(text))
		{
			_downloadSemaphore.Release();
			return UnzipIfNeeded(path, text);
		}
		try
		{
			_logger.Info("Downloading xmltv listings from {0}", new object[1] { path });
			_fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(text));
			text = await _httpClient.GetTempFile(new HttpRequestOptions
			{
				CancellationToken = cancellationToken,
				Url = path,
				Progress = (IProgress<double>)new SimpleProgress<double>(),
				DecompressionMethod = (CompressionMethod)1,
				EnableHttpCompression = true,
				UserAgent = (string.IsNullOrEmpty(info.UserAgent) ? "Emby/3.0" : info.UserAgent),
				DownloadFilePath = text
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_downloadSemaphore.Release();
		}
		return UnzipIfNeeded(path, text);
	}

	private string UnzipIfNeeded(string originalUrl, string file)
	{
		Path.GetExtension(originalUrl.Split('?')[0]);
		try
		{
			string directory = ExtractGz(file);
			return FindXmlFile(directory);
		}
		catch (Exception)
		{
		}
		try
		{
			string directory2 = ExtractFirstFileFromGz(file);
			return FindXmlFile(directory2);
		}
		catch (Exception)
		{
			return file;
		}
	}

	private string ExtractFirstFileFromGz(string file)
	{
		using Stream stream = _fileSystem.OpenRead(file);
		string text = Path.Combine(((IApplicationPaths)_config.ApplicationPaths).TempDirectory, Guid.NewGuid().ToString());
		_fileSystem.CreateDirectory(text);
		_zipClient.ExtractFirstFileFromGz(stream, text, "data.xml");
		return text;
	}

	private string ExtractGz(string file)
	{
		using Stream stream = _fileSystem.OpenRead(file);
		string text = Path.Combine(((IApplicationPaths)_config.ApplicationPaths).TempDirectory, Guid.NewGuid().ToString());
		_fileSystem.CreateDirectory(text);
		_zipClient.ExtractAllFromGz(stream, text, true);
		return text;
	}

	private string FindXmlFile(string directory)
	{
		return (from i in _fileSystem.GetFiles(directory, true)
			where string.Equals(i.Extension, ".xml", StringComparison.OrdinalIgnoreCase)
			select i.FullName).FirstOrDefault();
	}

	public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(ListingsProviderInfo info, string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(channelId))
		{
			throw new ArgumentNullException("channelId");
		}
		if (!(await _securityManager.GetRegistrationStatus("xmltv").ConfigureAwait(continueOnCapturedContext: false)).IsValid && (endDateUtc - startDateUtc).TotalDays > 1.0)
		{
			endDateUtc = startDateUtc.AddDays(1.0);
		}
		_logger.Debug("Getting xmltv programs for channel {0}", new object[1] { channelId });
		string text = await GetXml(info, info.Path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		_logger.Debug("Opening XmlTvReader for {0}", new object[1] { text });
		return from p in new XmlTvReader(text, GetLanguage(info)).GetProgrammes(channelId, startDateUtc, endDateUtc, cancellationToken)
			select GetProgramInfo(p, info);
	}

	private ProgramInfo GetProgramInfo(XmlTvProgram p, ListingsProviderInfo info)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_020e: Unknown result type (might be due to invalid IL or missing references)
		//IL_023c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0273: Unknown result type (might be due to invalid IL or missing references)
		//IL_029d: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b6: Expected Obj, but got Unknown
		string text = ((p.Episode == null) ? null : p.Episode.Title);
		ProgramInfo val = new ProgramInfo
		{
			ChannelId = p.ChannelId,
			EndDate = GetDate(p.EndDate),
			EpisodeNumber = ((p.Episode == null) ? ((int?)null) : p.Episode.Episode),
			EpisodeTitle = text,
			Genres = p.Categories,
			StartDate = GetDate(p.StartDate),
			Name = p.Title,
			Overview = p.Description,
			ProductionYear = ((!p.CopyrightDate.HasValue) ? ((int?)null) : new int?(p.CopyrightDate.Value.Year)),
			SeasonNumber = ((p.Episode == null) ? ((int?)null) : p.Episode.Series),
			IsSeries = (p.Episode != null),
			IsRepeat = (p.IsPreviouslyShown && !p.IsNew),
			IsPremiere = (p.Premiere != null),
			IsKids = p.Categories.Any((string c) => info.KidsCategories.Contains(c, StringComparer.OrdinalIgnoreCase)),
			IsMovie = p.Categories.Any((string c) => info.MovieCategories.Contains(c, StringComparer.OrdinalIgnoreCase)),
			IsNews = p.Categories.Any((string c) => info.NewsCategories.Contains(c, StringComparer.OrdinalIgnoreCase)),
			IsSports = p.Categories.Any((string c) => info.SportsCategories.Contains(c, StringComparer.OrdinalIgnoreCase)),
			ImageUrl = ((p.Icon != null && !string.IsNullOrEmpty(p.Icon.Source)) ? p.Icon.Source : null),
			HasImage = (p.Icon != null && !string.IsNullOrEmpty(p.Icon.Source)),
			OfficialRating = ((p.Rating != null && !string.IsNullOrEmpty(p.Rating.Value)) ? p.Rating.Value : null),
			CommunityRating = (p.StarRating.HasValue ? new float?(p.StarRating.Value) : ((float?)null)),
			SeriesId = ((p.Episode != null) ? BaseExtensions.GetMD5(p.Title).ToString("N") : null),
			ProviderIds = p.ProviderIds,
			SeriesProviderIds = p.SeriesProviderIds
		};
		if (!string.IsNullOrWhiteSpace(p.ProgramId))
		{
			val.ShowId = p.ProgramId;
		}
		else
		{
			p.ProviderIds.TryGetValue("icetv", out var value);
			string text2 = (p.Title ?? string.Empty) + (text ?? string.Empty) + (value ?? string.Empty);
			if (val.SeasonNumber.HasValue)
			{
				text2 = text2 + "-" + val.SeasonNumber.Value.ToString(CultureInfo.InvariantCulture);
			}
			if (val.EpisodeNumber.HasValue)
			{
				text2 = text2 + "-" + val.EpisodeNumber.Value.ToString(CultureInfo.InvariantCulture);
			}
			val.ShowId = BaseExtensions.GetMD5(text2).ToString("N");
			if (val.IsSeries && !val.IsRepeat)
			{
				int? episodeNumber = val.EpisodeNumber;
				if (!episodeNumber.HasValue || episodeNumber.GetValueOrDefault() == 0)
				{
					val.ShowId += val.StartDate.Ticks.ToString(CultureInfo.InvariantCulture);
				}
			}
		}
		val.Id = $"{p.ChannelId}_{p.StartDate:O}";
		if (val.IsMovie)
		{
			val.IsSeries = false;
			val.EpisodeNumber = null;
			val.EpisodeTitle = null;
		}
		return val;
	}

	private DateTime GetDate(DateTime date)
	{
		if (date.Kind != DateTimeKind.Utc)
		{
			date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
		}
		return date;
	}

	public Task Validate(ListingsProviderInfo info, bool validateLogin, bool validateListings)
	{
		if (!info.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !_fileSystem.FileExists(info.Path))
		{
			throw new FileNotFoundException("Could not find the XmlTv file specified:", info.Path);
		}
		return Task.CompletedTask;
	}

	public async Task<List<NameIdPair>> GetLineups(ListingsProviderInfo info, string country, string location)
	{
		return ((IEnumerable<ChannelInfo>)(await GetChannels(info, CancellationToken.None))).Select((Func<ChannelInfo, NameIdPair>)((ChannelInfo i) =>
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected Obj, but got Unknown
			return new NameIdPair
			{
				Name = i.Name,
				Id = i.Id
			};
		})).ToList();
	}

	public async Task<List<ChannelInfo>> GetChannels(ListingsProviderInfo info, CancellationToken cancellationToken)
	{
		string text = await GetXml(info, info.Path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		_logger.Debug("Opening XmlTvReader for {0}", new object[1] { text });
		IEnumerable<XmlTvChannel> channels = new XmlTvReader(text, GetLanguage(info)).GetChannels();
		List<ChannelInfo> list = new List<ChannelInfo>();
		foreach (XmlTvChannel item2 in channels)
		{
			ChannelInfo item = new ChannelInfo
			{
				Id = item2.Id,
				Name = item2.DisplayName,
				ImageUrl = ((item2.Icon != null && !string.IsNullOrWhiteSpace(item2.Icon.Source)) ? item2.Icon.Source : null),
				Number = (string.IsNullOrWhiteSpace(item2.Number) ? item2.Id : item2.Number)
			};
			list.Add(item);
		}
		return list;
	}
}
