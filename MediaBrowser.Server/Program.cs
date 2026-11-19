using MediaBrowser.Model.Logging;
using MediaBrowser.Server.Mono.Native;
using MediaBrowser.Server.Startup.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Emby.Drawing;
using Emby.Server.HttpServer;
using Emby.Server.Implementations;
using Emby.Server.Implementations.EnvironmentInfo;
using Emby.Server.Implementations.IO;
using Emby.Server.Implementations.Logging;
using Emby.Server.Implementations.Networking;
using MediaBrowser.Controller;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.System;
using Npgsql;
using ILogger = MediaBrowser.Model.Logging.ILogger;
using X509Certificate = System.Security.Cryptography.X509Certificates.X509Certificate;

namespace MediaBrowser.Server.Mono
{
    public class MainClass
    {
        private static ILogger _logger;
        private static IFileSystem FileSystem;
        private static IServerApplicationPaths _appPaths;
        private static ILogManager _logManager;

        private static readonly TaskCompletionSource<bool> ApplicationTaskCompletionSource = new TaskCompletionSource<bool>();
        private static readonly List<System.Runtime.InteropServices.PosixSignalRegistration> _signalRegistrations = new List<System.Runtime.InteropServices.PosixSignalRegistration>();
        private static int _signalHandled;
        private static volatile string _shutdownReason = "application request";
        private static bool _restartOnShutdown;

        public static async Task Main(string[] args)
        {
            // .NET Core 起 BCL 只内置 UTF-8 / UTF-16 / UTF-32 / ASCII。
            // windows-125x、gb18030、shift_jis、euc-kr、tis-620 这些传统代码页
            // 必须显式注册 provider，否则 Encoding.GetEncoding("gb18030") 直接抛
            // ArgumentException。字幕字符集检测的产出正是这些名字
            // （TextEncoding.GetEncodingFromCharset），不注册的话所有非 UTF-8
            // 字幕（中文 GBK、日文 Shift-JIS、俄文 cp1251）整条链路都会失败。
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var applicationPath = Assembly.GetEntryAssembly().Location;


            var options = new StartupOptions(Environment.GetCommandLineArgs());

            // Allow this to be specified on the command line.
            var customProgramDataPath = options.GetOption("-programdata");

            var appPaths = CreateApplicationPaths(applicationPath, customProgramDataPath);
            _appPaths = appPaths;

            using (var logManager = new SimpleLogManager(appPaths.LogDirectoryPath, "server"))
            {
                _logManager = logManager;

                await logManager.ReloadLogger(LogSeverity.Debug, CancellationToken.None).ConfigureAwait(false);
                logManager.AddConsoleOutput();

                var logger = _logger = logManager.GetLogger("Main");

                ApplicationHost.LogEnvironmentInfo(logger, appPaths, true);

                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                RegisterShutdownSignals(logger);

                await RunApplication(appPaths, logManager, options).ConfigureAwait(false);

                _logger.Info("Disposing app host");

                if (_restartOnShutdown)
                {
                    StartNewInstance(options);
                }
            }
        }


        private static ServerApplicationPaths CreateApplicationPaths(string applicationPath, string programDataPath)
        {
            if (string.IsNullOrEmpty(programDataPath))
            {
                programDataPath = ApplicationPathHelper.GetProgramDataPath(applicationPath);
            }

            var appFolderPath = Path.GetDirectoryName(applicationPath);

            return new ServerApplicationPaths(programDataPath, appFolderPath, Path.GetDirectoryName(applicationPath));
        }

        private static async Task RunApplication(ServerApplicationPaths appPaths, ILogManager logManager, StartupOptions options)
        {
            var environmentInfo = GetEnvironmentInfo(options);

            var fileSystem = new ManagedFileSystem(logManager.GetLogger("FileSystem"), environmentInfo, null, appPaths.TempDirectory, true);

            FileSystem = fileSystem;

            var appHost = new MonoAppHost(appPaths,
                logManager,
                options,
                fileSystem,
                new PowerManagement(),
                "embyserver-mono_{version}.zip",
                environmentInfo,
                new NullImageEncoder(),
                new SystemEvents(logManager.GetLogger("SystemEvents")),
                new NetworkManager(logManager.GetLogger("NetworkManager"), environmentInfo));

            try
            {
                if (options.ContainsOption("-v"))
                {
                    Console.WriteLine(appHost.ApplicationVersion.ToString());
                    return;
                }

                Console.WriteLine("appHost.Init");

                appHost.Init();

                appHost.ImageProcessor.ImageEncoder = ImageEncoderHelper.GetImageEncoder(_logger, logManager, fileSystem, options, () => appHost.HttpClient, appPaths, environmentInfo, appHost.LocalizationManager);

                Console.WriteLine("Running startup tasks");

                await appHost.RunStartupTasks().ConfigureAwait(false);

                KestrelHost.Instance?.SetApplicationReady(true);
                _logger.Info("Startup complete; readiness probes can pass");

                await ApplicationTaskCompletionSource.Task.WaitAsync(CancellationToken.None).ConfigureAwait(false);

                await ShutdownGracefully(appHost).ConfigureAwait(false);
            }
            finally
            {
                // 正常停机路径已在 ShutdownGracefully 内释放；这里兜住启动/运行异常路径。
                _logger.Info("Disposing app host");
                appHost.Dispose();

                foreach (var registration in _signalRegistrations)
                {
                    registration.Dispose();
                }
            }
        }

        /// <summary>
        /// SIGTERM/SIGINT 只负责置位；真正的停机步骤在 Main 的异步流程中按序执行，
        /// 避免信号回调里跑异步代码或被 .NET 运行时默认行为直接终止进程。
        /// </summary>
        private static void OnShutdownSignal(System.Runtime.InteropServices.PosixSignalContext context, string signal)
        {
            context.Cancel = true;

            if (Interlocked.CompareExchange(ref _signalHandled, 1, 0) == 0)
            {
                _shutdownReason = signal;
                _logger?.Info("Received {0}; requesting graceful shutdown", signal);
                ApplicationTaskCompletionSource.TrySetResult(true);
            }
        }

        private static void RegisterShutdownSignals(ILogger logger)
        {
            RegisterSignal(System.Runtime.InteropServices.PosixSignal.SIGTERM, "SIGTERM", logger);
            RegisterSignal(System.Runtime.InteropServices.PosixSignal.SIGINT, "SIGINT", logger);
        }

        private static void RegisterSignal(System.Runtime.InteropServices.PosixSignal signal, string name, ILogger logger)
        {
            try
            {
                _signalRegistrations.Add(System.Runtime.InteropServices.PosixSignalRegistration.Create(signal, context => OnShutdownSignal(context, name)));
                logger.Info("Registered {0} handler for graceful shutdown", name);
            }
            catch (Exception ex)
            {
                // Windows 等平台可能不支持某个 POSIX 信号，不能因此阻止启动。
                logger.Warn("Cannot register {0} handler: {1}", name, ex.Message);
            }
        }

        /// <summary>
        /// 停机顺序：置停机标记（新请求 503）→ Kestrel 排空在途请求 →
        /// 释放服务（其中 ApiEntryPoint.Dispose 停止 ffmpeg）→ 清空 PG 连接池。
        /// </summary>
        private static async Task ShutdownGracefully(MonoAppHost appHost)
        {
            var budgetSeconds = appHost.ServerConfigurationManager.Configuration.ShutdownTimeoutSeconds;
            if (budgetSeconds < 1 || budgetSeconds > 300)
            {
                budgetSeconds = 10;
            }

            _logger.Info("Graceful shutdown started ({0}); budget {1}s", _shutdownReason, budgetSeconds);

            try
            {
                await appHost.Shutdown().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error sending shutdown notification", ex);
            }

            KestrelHost.Instance?.SetApplicationReady(false);

            _logger.Info("Shutdown 1/4: stop accepting requests, drain in-flight HTTP (limit {0}s)", budgetSeconds);
            try
            {
                KestrelHost.Instance?.Stop();
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error stopping HTTP server", ex);
            }

            _logger.Info("Shutdown 2/4: stop transcoding processes and dispose registered services");
            appHost.Dispose();

            _logger.Info("Shutdown 3/4: clear PostgreSQL connection pools");
            try
            {
                NpgsqlConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error clearing PostgreSQL connection pools", ex);
            }

            _logger.Info("Shutdown 4/4: exit");
        }

        private static EnvironmentInfo GetEnvironmentInfo(StartupOptions options)
        {
            var operatingSystem = Model.System.OperatingSystem.Linux;

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                operatingSystem = Model.System.OperatingSystem.Windows;
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                operatingSystem = Model.System.OperatingSystem.OSX;
            }
            else if (string.Equals(options.GetOption("-os"), "freebsd", StringComparison.OrdinalIgnoreCase))
            {
                operatingSystem = Model.System.OperatingSystem.BSD;
            }

            return new EnvironmentInfo()
            {
                OperatingSystem = operatingSystem,
                SystemArchitecture = Environment.Is64BitOperatingSystem ? Architecture.X64 : Architecture.Arm
            };
        }

        /// <summary>
        /// Handles the UnhandledException event of the CurrentDomain control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="UnhandledExceptionEventArgs"/> instance containing the event data.</param>
        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = (Exception)e.ExceptionObject;

            new UnhandledExceptionWriter(_appPaths, _logger, _logManager, FileSystem, new ConsoleLogger()).Log(exception);

            if (!Debugger.IsAttached)
            {
                var message = LogHelper.GetLogMessage(exception).ToString();

                if (message.IndexOf("InotifyWatcher", StringComparison.OrdinalIgnoreCase) == -1 &&
                    message.IndexOf("_IOCompletionCallback", StringComparison.OrdinalIgnoreCase) == -1)
                {
                    Environment.Exit(System.Runtime.InteropServices.Marshal.GetHRForException(exception));
                }
            }
        }

        public static void Shutdown()
        {
            // API 触发的停机和 SIGTERM 可能同时到达；TrySetResult 保证幂等。
            ApplicationTaskCompletionSource.TrySetResult(true);
        }

        public static void Restart()
        {
            _restartOnShutdown = true;

            Shutdown();
        }

        private static void StartNewInstance(StartupOptions startupOptions)
        {
            _logger.Info("Starting new instance");

            string module = startupOptions.GetOption("-restartpath");
            string commandLineArgsString = startupOptions.GetOption("-restartargs") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(module))
            {
                module = Environment.GetCommandLineArgs().First();
            }
            if (!startupOptions.ContainsOption("-restartargs"))
            {
                var args = Environment.GetCommandLineArgs()
                    .Skip(1)
                    .Select(NormalizeCommandLineArgument)
                    .ToArray();

                commandLineArgsString = string.Join(" ", args);
            }

            _logger.Info("Executable: {0}", module);
            _logger.Info("Arguments: {0}", commandLineArgsString);

            Process.Start(module, commandLineArgsString);
        }

        private static string NormalizeCommandLineArgument(string arg)
        {
            if (arg.IndexOf(" ", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return arg;
            }

            return "\"" + arg + "\"";
        }
    }
}
