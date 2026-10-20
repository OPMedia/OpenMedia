using System;
using System.IO;
using Emby.Server.Implementations.Configuration;

namespace MediaBrowser.Tests.PgData
{
    /// <summary>
    /// IServerConfigurationManager 最小 stub：ServerConfigurationManager 的构造期会写
    /// 缓存目录，这里退到临时目录；PgItemRepository 只读三个开关与 PeoplePath，
    /// 默认值即 ServerConfiguration 默认（SkipDeserializationForBasicTypes=true 等）。
    /// </summary>
    internal sealed class FakeConfigManager : ServerConfigurationManager
    {
        public FakeConfigManager()
            : base(
                new TempApplicationPaths(),
                new FakeLogManager(),
                new FakeXmlSerializer(),
                new Emby.Server.Implementations.IO.ManagedFileSystem(
                    new MediaBrowser.Model.Logging.NullLogger(),
                    new Emby.Server.Implementations.EnvironmentInfo.EnvironmentInfo(),
                    ".",
                    Path.GetTempPath(),
                    false))
        {
        }
    }
}