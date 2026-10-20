using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Configuration;
using Emby.Server.Implementations.EnvironmentInfo;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using EnvironmentInfoType = Emby.Server.Implementations.EnvironmentInfo.EnvironmentInfo;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// PgItemRepository 测试装配用的最小 stub（Task A/B 的仓储实例化路径）。
    /// 只实现 PgItemRepository 真正读取的成员：三个配置开关 + ILogger。
    /// </summary>
    internal sealed class FakeLogManager : ILogManager
    {
        public LogSeverity LogSeverity { get; set; }

        public string ExceptionMessagePrefix { get; set; }

        public event EventHandler LoggerLoaded;

        public ILogger GetLogger(string name)
        {
            return new NullLogger();
        }

        public Task ReloadLogger(LogSeverity severity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Flush()
        {
        }

        public void AddConsoleOutput()
        {
        }

        public void RemoveConsoleOutput()
        {
        }
    }

    internal sealed class FakeXmlSerializer : IXmlSerializer
    {
        public object DeserializeFromStream(Type type, Stream stream)
        {
            return Activator.CreateInstance(type);
        }

        public void SerializeToStream(object obj, Stream stream)
        {
        }

        public void SerializeToFile(object obj, string file)
        {
        }

        public object DeserializeFromFile(Type type, string file)
        {
            return Activator.CreateInstance(type);
        }

        public object DeserializeFromBytes(Type type, byte[] buffer)
        {
            return Activator.CreateInstance(type);
        }
    }

    /// <summary>
    /// 临时目录型 IApplicationPaths：ServerConfigurationManager 只在构造期读路径，
    /// 测试不落真实配置，用完后由 [TestCleanup] 统一清理临时根。
    /// </summary>
    internal sealed class TempApplicationPaths : Emby.Server.Implementations.ServerApplicationPaths
    {
        public TempApplicationPaths()
            : base(Make("data"), Make("system"), Make("resources"))
        {
        }

        private static string Make(string suffix)
        {
            var p = Path.Combine(Path.GetTempPath(), "openmedia-test-" + Guid.NewGuid().ToString("N") + "-" + suffix);
            Directory.CreateDirectory(p);
            return p;
        }
    }

    /// <summary>
    /// 最小 ILocalizationManager：查询引擎的 GetCleanValue -> RemoveDiacritics 需要它。
    /// 生产里由 ApplicationHost 注入 StringExtensions.LocalizationManager；测试里手工接上，
    /// 否则 Tags/Genres/NameContains 分支直接 NullReferenceException。
    /// </summary>
    internal sealed class FakeLocalizationManager : MediaBrowser.Model.Globalization.ILocalizationManager
    {
        public MediaBrowser.Model.Globalization.CultureDto[] GetCultures()
        {
            return Array.Empty<MediaBrowser.Model.Globalization.CultureDto>();
        }

        public MediaBrowser.Model.Globalization.CountryInfo[] GetCountries()
        {
            return Array.Empty<MediaBrowser.Model.Globalization.CountryInfo>();
        }

        public MediaBrowser.Model.Entities.ParentalRating[] GetParentalRatings()
        {
            return Array.Empty<MediaBrowser.Model.Entities.ParentalRating>();
        }

        public int? GetRatingLevel(string rating)
        {
            return null;
        }

        public string GetLocalizedString(string phrase, string culture)
        {
            return phrase;
        }

        public string GetLocalizedString(string phrase)
        {
            return phrase;
        }

        public MediaBrowser.Model.Globalization.LocalizatonOption[] GetLocalizationOptions()
        {
            return Array.Empty<MediaBrowser.Model.Globalization.LocalizatonOption>();
        }

        public string RemoveDiacritics(string text)
        {
            return text ?? string.Empty;
        }

        public string NormalizeFormKD(string text)
        {
            return text ?? string.Empty;
        }

        public bool HasUnicodeCategory(string value, System.Globalization.UnicodeCategory category)
        {
            return false;
        }

        public MediaBrowser.Model.Globalization.CultureDto FindLanguageInfo(string language)
        {
            return null;
        }
    }
}
