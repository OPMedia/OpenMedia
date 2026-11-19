using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaBrowser.Api.System;

namespace Emby.Server.Implementations.Backup
{
    public sealed class BackupManifest
    {
        public int FormatVersion { get; set; }

        public string Id { get; set; }

        public string Kind { get; set; }

        public DateTime CreatedUtc { get; set; }

        public string Mode { get; set; }

        public BackupManifestSchema Schema { get; set; }

        public List<BackupManifestComponent> Components { get; set; }

        public long TotalBytes { get; set; }

        /// <summary>对其余字段（Integrity 置空）的规范化 JSON 做的 SHA-256。</summary>
        public string Integrity { get; set; }

        public BackupManifest WithoutIntegrity()
        {
            return new BackupManifest
            {
                FormatVersion = FormatVersion,
                Id = Id,
                Kind = Kind,
                CreatedUtc = CreatedUtc,
                Mode = Mode,
                Schema = Schema,
                Components = Components,
                TotalBytes = TotalBytes,
                Integrity = null
            };
        }
    }

    public sealed class BackupManifestSchema
    {
        public string ApplicationVersion { get; set; }

        public int SchemaVersion { get; set; }

        public string DatabaseName { get; set; }

        public int DatabaseMajorVersion { get; set; }
    }

    public sealed class BackupManifestComponent
    {
        public string Name { get; set; }

        public string Path { get; set; }

        public long Bytes { get; set; }

        public int FileCount { get; set; }

        public string Sha256 { get; set; }
    }

    public static class BackupManifestCodec
    {
        public const int FormatVersion = 1;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static string Serialize(BackupManifest manifest)
        {
            return JsonSerializer.Serialize(manifest, Options);
        }

        public static BackupManifest Deserialize(string json)
        {
            return JsonSerializer.Deserialize<BackupManifest>(json, Options);
        }

        public static string ComputeIntegrity(BackupManifest manifest)
        {
            var canonical = JsonSerializer.SerializeToUtf8Bytes(manifest.WithoutIntegrity(), Options);
            return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
        }

        /// <summary>
        /// 目录校验和按「相对路径 + 长度 + 内容」有序流入，避免文件系统枚举顺序造成假差异。
        /// </summary>
        public static string ComputeDirectoryHash(string directoryPath, out long totalBytes, out int fileCount)
        {
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                totalBytes = 0;
                fileCount = 0;

                var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.Ordinal);
                var root = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar);

                foreach (var file in files)
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new BackupValidationException("备份目录包含符号链接，拒绝校验: " + file);
                    }

                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    hash.AppendData(Encoding.UTF8.GetBytes(relative));
                    var length = new FileInfo(file).Length;
                    hash.AppendData(BitConverter.GetBytes(length));

                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true))
                    {
                        var buffer = new byte[64 * 1024];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            hash.AppendData(buffer, 0, read);
                        }
                    }

                    totalBytes += length;
                    fileCount++;
                }

                return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
        }

        public static string ComputeFileHash(string filePath, out long totalBytes)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true))
            {
                totalBytes = stream.Length;
                return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }
        }
    }
}
