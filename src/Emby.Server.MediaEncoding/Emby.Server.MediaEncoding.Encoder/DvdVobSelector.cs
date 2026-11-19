using System;
using System.Collections.Generic;
using System.Linq;

namespace Emby.Server.MediaEncoding.Encoder
{
    /// <summary>
    /// DVD <c>.vob</c> 选集逻辑。
    ///
    /// 对齐 Jellyfin 的算法（<c>MediaBrowser.MediaEncoding/Encoder/MediaEncoder.cs</c>
    /// 的 <c>GetPrimaryPlaylistVobFiles</c>）：
    /// <list type="number">
    /// <item>按文件名排除菜单：<c>VIDEO_TS.VOB</c> 与所有 <c>*_0.VOB</c>。菜单不是正片，
    /// 而按大小过滤在小体积盘（短片、高压缩）上会把菜单漏进来。</item>
    /// <item>给了 titleNumber 就按 <c>VTS_0X_</c> 前缀取整卷。</item>
    /// <item>否则用 900 MiB 阈值（DVD 单 VOB 上限 1 GiB，正片 VOB 必然接近满）找出所有「大 title」，
    /// 聚合它们的全部 VOB。</item>
    /// <item>没有大 title 时退回第一个可播放 VOB 所属的 title。</item>
    /// </list>
    ///
    /// 与 Jellyfin 的一处有意偏离：Jellyfin 用文件名最后一个下划线段做 title 键，
    /// 对 <c>VTS_01_1</c> 和 <c>VTS_02_1</c> 都会得到 "1"，两个不同 VTS 会被错误聚合。
    /// 这里用中间段的 VTS 号（<c>VTS_01_C.VOB</c> 里的 <c>01</c>），这才是它的本意。
    ///
    /// 抽成纯函数是为了能脱离 ffprobe 与真实光盘做断言。
    /// </summary>
    public static class DvdVobSelector
    {
        /// <summary>DVD 单 VOB 上限 1 GiB；正片 VOB 接近满，取 900 MiB 作为「大 title」门槛。</summary>
        internal const long BigTitleThresholdBytes = 900L * 1024 * 1024;

        /// <summary>
        /// 从 VOB 列表中选出主播放列表。
        /// </summary>
        /// <param name="vobs">候选文件（文件名 + 体积），顺序无关。</param>
        /// <param name="titleNumber">已知的 title set 号；null 表示靠体积推断。</param>
        public static IReadOnlyList<string> Select(IEnumerable<VobEntry> vobs, uint? titleNumber)
        {
            var playable = vobs
                .Where(v => !IsMenu(v.Name))
                .OrderBy(v => v.FullName, StringComparer.Ordinal)
                .ToList();

            if (playable.Count == 0)
            {
                return Array.Empty<string>();
            }

            if (titleNumber.HasValue)
            {
                var prefix = "VTS_" + titleNumber.Value.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "_";
                var byTitle = playable
                    .Where(v => v.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (byTitle.Count > 0)
                {
                    return byTitle.Select(v => v.FullName).ToList();
                }
            }

            var bigTitles = playable
                .Where(v => v.Length >= BigTitleThresholdBytes)
                .Select(v => GetTitleSetKey(v.Name))
                .Where(key => key != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (bigTitles.Count == 0)
            {
                var firstKey = GetTitleSetKey(playable[0].Name);
                if (firstKey != null)
                {
                    bigTitles.Add(firstKey);
                }
            }

            if (bigTitles.Count == 0)
            {
                // 文件名不符合 VTS_0X_C 结构 —— 已经排除了菜单，全给。
                return playable.Select(v => v.FullName).ToList();
            }

            return playable
                .Where(v => bigTitles.Contains(GetTitleSetKey(v.Name) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                .Select(v => v.FullName)
                .ToList();
        }

        /// <summary>菜单文件：<c>VIDEO_TS.VOB</c> 与所有 <c>*_0.VOB</c>（每个 VTS 的第 0 个是菜单）。</summary>
        private static bool IsMenu(string name)
        {
            return string.Equals(name, "VIDEO_TS.VOB", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_0.VOB", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 从 <c>VTS_01_2.VOB</c> 取 title set 号 <c>01</c>；结构不符返回 null。
        /// </summary>
        private static string? GetTitleSetKey(string name)
        {
            var stem = name;
            var dot = stem.LastIndexOf('.');
            if (dot >= 0)
            {
                stem = stem.Substring(0, dot);
            }

            var parts = stem.Split('_');
            if (parts.Length != 3 || !parts[0].Equals("VTS", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return parts[1];
        }

        /// <summary>一个候选 VOB 的最小信息集。</summary>
        public sealed class VobEntry
        {
            public VobEntry(string fullName, long length)
            {
                FullName = fullName;
                Length = length;
                var slash = fullName.LastIndexOfAny(new[] { '/', '\\' });
                Name = slash >= 0 ? fullName.Substring(slash + 1) : fullName;
            }

            public string FullName { get; }

            public string Name { get; }

            public long Length { get; }
        }
    }
}
