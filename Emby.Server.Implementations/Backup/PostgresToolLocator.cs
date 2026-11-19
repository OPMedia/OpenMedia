using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Api.System;

namespace Emby.Server.Implementations.Backup
{
    /// <summary>
    /// 探测 pg_dump / pg_restore。本机 PostgreSQL 常常不在 PATH 中（容器、homebrew、发行版多版本并存），
    /// 因此按「配置目录 → OPENMEDIA_PG_BIN → PATH → 常见安装位置」顺序查找，并在失败时列出所有尝试过的路径。
    /// </summary>
    public static class PostgresToolLocator
    {
        public const string BinDirectoryEnvironmentVariable = "OPENMEDIA_PG_BIN";

        /// <summary>
        /// 定位 pg_dump / pg_restore。
        /// </summary>
        /// <param name="toolName">工具名（不含扩展名）。</param>
        /// <param name="configuredDirectory">配置里指定的 bin 目录。</param>
        /// <param name="includeSystemFallbacks">
        /// 是否继续探测环境变量、PATH 与已知安装目录。
        /// 关掉它可以构造「一个都找不到」的确定性场景 —— 否则这个分支的测试结果会随
        /// 运行机器上有没有装 postgresql-client 而变（CI 上就因为 /usr/lib/postgresql
        /// 存在而永远找不到失败路径，测试反而红了）。
        /// </param>
        public static string Locate(string toolName, string configuredDirectory, bool includeSystemFallbacks = true)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new ArgumentNullException(nameof(toolName));
            }

            var fileName = OperatingSystem.IsWindows() ? toolName + ".exe" : toolName;
            var searched = new List<string>();

            AddIfExists(searched, configuredDirectory, fileName, out var found);
            if (found != null)
            {
                return found;
            }

            AddIfExists(searched, Environment.GetEnvironmentVariable(BinDirectoryEnvironmentVariable), fileName, out found);
            if (found != null)
            {
                return found;
            }

            if (!includeSystemFallbacks)
            {
                throw BuildNotFound(toolName, fileName, searched);
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                AddIfExists(searched, directory, fileName, out found);
                if (found != null)
                {
                    return found;
                }
            }

            foreach (var directory in GetKnownDirectories())
            {
                AddIfExists(searched, directory, fileName, out found);
                if (found != null)
                {
                    return found;
                }
            }

            throw BuildNotFound(toolName, fileName, searched);
        }

        private static BackupValidationException BuildNotFound(string toolName, string fileName, List<string> searched)
        {
            return new BackupValidationException(
                "找不到 " + fileName + "。已探测：" + string.Join(", ", searched)
                + "。请安装与服务器同主版本的 postgresql-client，或设置 Backup.PostgresBinDirectory / 环境变量 "
                + BinDirectoryEnvironmentVariable + " 指向其 bin 目录。");
        }

        private static IEnumerable<string> GetKnownDirectories()
        {
            var candidates = new List<string>
            {
                "/usr/lib/postgresql",
                "/usr/pgsql",
                "/usr/local/pgsql/bin",
                "/opt/local/lib/postgresql/bin",
                "/opt/homebrew/opt/libpq/bin",
                "/usr/local/opt/libpq/bin"
            };

            if (OperatingSystem.IsWindows())
            {
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL"));
            }

            foreach (var candidate in candidates)
            {
                if (!Directory.Exists(candidate))
                {
                    continue;
                }

                // /usr/lib/postgresql/16/bin 与 C:\Program Files\PostgreSQL\16\bin
                foreach (var versionDirectory in SafeGetDirectories(candidate))
                {
                    var bin = Path.Combine(versionDirectory, "bin");
                    if (Directory.Exists(bin))
                    {
                        yield return bin;
                    }
                }

                if (candidate.EndsWith("bin", StringComparison.Ordinal))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<string> SafeGetDirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static void AddIfExists(List<string> searched, string directory, string fileName, out string found)
        {
            found = null;
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(directory, fileName));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                searched.Add(directory + " (无效路径)");
                return;
            }

            searched.Add(candidate);
            if (File.Exists(candidate))
            {
                found = candidate;
            }
        }
    }
}
