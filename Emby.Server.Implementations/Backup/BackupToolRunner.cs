using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Server.Implementations.Backup
{
    public sealed class BackupToolResult
    {
        public int ExitCode { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }
    }

    /// <summary>进程执行抽象，便于用假 runner 做并发/失败路径测试。</summary>
    public interface IBackupToolRunner
    {
        Task<BackupToolResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            IDictionary<string, string> environment,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// 用 ProcessStartInfo.ArgumentList 逐项传参：pg_dump/pg_restore 的路径、文件名、连接参数
    /// 永远不进入 shell 字符串，也不参与任何字符串拼接后的解析。
    /// </summary>
    public sealed class ProcessBackupToolRunner : IBackupToolRunner
    {
        public async Task<BackupToolResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            IDictionary<string, string> environment,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();

                // 两个流必须并发排空，否则大 dump 的 stderr 可能填满管道导致死锁。
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                return new BackupToolResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = await stdoutTask.ConfigureAwait(false),
                    StandardError = await stderrTask.ConfigureAwait(false)
                };
            }
        }
    }
}
