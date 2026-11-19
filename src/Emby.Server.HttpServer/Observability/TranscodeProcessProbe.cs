using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Prometheus;

namespace Emby.Server.HttpServer.Observability
{
    /// <summary>
    /// 转码会话指标：转码引擎的会话表是私有的（不能改 MediaEncoding 工程），
    /// 因此以本进程直接 fork 的 ffmpeg 子进程为口径：gauge = 当前活跃进程数，
    /// counter = 进程启动以来观察到的不同 pid 数。短于两次抓取间隔的会话可能漏计，
    /// 这一点在运维文档/报告里明确说明，不伪称是引擎层的会话计数。
    /// </summary>
    public static class TranscodeProcessProbe
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<int> SeenPids = new HashSet<int>();
        private static readonly Gauge Active =
            Metrics.CreateGauge(OpsMetrics.TranscodingActiveName, "Active ffmpeg child processes of this server.");

        private static readonly Counter Started =
            Metrics.CreateCounter(OpsMetrics.TranscodingTotalName, "Distinct ffmpeg child processes observed since start.");

        public static void Refresh()
        {
            var pids = new List<int>();
            if (OperatingSystem.IsLinux())
            {
                var self = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
                var childrenPath = "/proc/self/task/" + self + "/children";

                try
                {
                    var children = File.ReadAllText(childrenPath);
                    foreach (var part in children.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                        {
                            pids.Add(pid);
                        }
                    }
                }
                catch (IOException)
                {
                    // /proc 不可读（容器隐藏 pid namespace 等）：保持上一次/零值，不让抓取失败。
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            var ffmpegPids = new List<int>();
            foreach (var pid in pids)
            {
                try
                {
                    var commandLine = File.ReadAllBytes("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/cmdline");
                    if (IsFfmpegCommandLine(Encoding.UTF8.GetString(commandLine).Replace('\0', ' ')))
                    {
                        ffmpegPids.Add(pid);
                    }
                }
                catch (IOException)
                {
                    // 进程在读取之间退出，正常竞态。
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            lock (Sync)
            {
                foreach (var pid in ffmpegPids)
                {
                    if (SeenPids.Add(pid))
                    {
                        Started.Inc();
                    }
                }

                Active.Set(ffmpegPids.Count);
            }
        }

        public static bool IsFfmpegCommandLine(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return false;
            }

            var firstToken = commandLine.TrimStart();
            var space = firstToken.IndexOf(' ');
            if (space >= 0)
            {
                firstToken = firstToken.Substring(0, space);
            }

            var executable = firstToken.Replace('\\', '/');
            var slash = executable.LastIndexOf('/');
            if (slash >= 0)
            {
                executable = executable.Substring(slash + 1);
            }

            return executable.IndexOf("ffmpeg", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
