using System;
using System.Collections.Generic;
using System.Reflection;
using Emby.Server.Implementations;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.System;

namespace MediaBrowser.Server.Mono
{
    public class MonoAppHost : ApplicationHost
    {
        public MonoAppHost(ServerApplicationPaths applicationPaths, ILogManager logManager, StartupOptions options, IFileSystem fileSystem, IPowerManagement powerManagement, string releaseAssetFilename, IEnvironmentInfo environmentInfo, MediaBrowser.Controller.Drawing.IImageEncoder imageEncoder, ISystemEvents systemEvents, MediaBrowser.Common.Net.INetworkManager networkManager) : base(applicationPaths, logManager, options, fileSystem, powerManagement, releaseAssetFilename, environmentInfo, imageEncoder, systemEvents, networkManager)
        {
        }

        public override bool CanSelfRestart
        {
            get
            {
                // A restart script must be provided
                return StartupOptions.ContainsOption("-restartpath");
            }
        }

        protected override void RestartInternal()
        {
            MainClass.Restart();
        }

        protected override List<Assembly> GetAssembliesWithPartsInternal()
        {
            var list = new List<Assembly>();

            list.Add(GetType().Assembly);

            return list;
        }

        protected override void ShutdownInternal()
        {
            MainClass.Shutdown();
        }
    }
}
