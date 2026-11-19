using System.Linq;
using Emby.Server.MediaEncoding.Encoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.MediaEncoding
{
    /// <summary>
    /// DVD <c>.vob</c> 选集规则的回归测试。
    ///
    /// 这些用例是「删掉 DvdLib 之后靠 ffprobe 推断 DVD 主播放列表」的正确性底线：
    /// 没有真实光盘样本时，至少选集这一步可以被完整断言。
    /// </summary>
    [TestClass]
    public class DvdVobSelectorTests
    {
        private const long Gib = 1024L * 1024 * 1024;

        private static DvdVobSelector.VobEntry Vob(string name, long length)
            => new DvdVobSelector.VobEntry(@"\\nas\Movies\DVDTEST\VIDEO_TS\" + name, length);

        private static string[] Names(System.Collections.Generic.IReadOnlyList<string> paths)
            => paths.Select(p => p.Substring(p.LastIndexOf('\\') + 1)).ToArray();

        [TestMethod]
        public void SingleMovieDisc_ReturnsMainTitleAndExcludesMenus()
        {
            var result = DvdVobSelector.Select(
                new[]
                {
                    Vob("VIDEO_TS.VOB", 80L * 1024 * 1024),   // 全局菜单
                    Vob("VTS_01_0.VOB", 200L * 1024 * 1024),  // 卷首菜单
                    Vob("VTS_01_1.VOB", Gib),
                    Vob("VTS_01_2.VOB", Gib),
                    Vob("VTS_01_3.VOB", Gib),
                },
                titleNumber: null);

            CollectionAssert.AreEqual(
                new[] { "VTS_01_1.VOB", "VTS_01_2.VOB", "VTS_01_3.VOB" },
                Names(result));
        }

        /// <summary>
        /// 所有 VOB 都小于 900 MiB 时（短片盘 / 高压缩），旧实现会因大小过滤落空而把
        /// 菜单也当成正片返回。现在按文件名排除，菜单不会进来。
        /// </summary>
        [TestMethod]
        public void AllVobsSmall_StillExcludesMenus()
        {
            var result = DvdVobSelector.Select(
                new[]
                {
                    Vob("VIDEO_TS.VOB", 10L * 1024 * 1024),
                    Vob("VTS_01_0.VOB", 10L * 1024 * 1024),
                    Vob("VTS_01_1.VOB", 100L * 1024 * 1024),
                    Vob("VTS_01_2.VOB", 100L * 1024 * 1024),
                },
                titleNumber: null);

            CollectionAssert.AreEqual(
                new[] { "VTS_01_1.VOB", "VTS_01_2.VOB" },
                Names(result));
        }

        /// <summary>
        /// 两个大 title（盘上两个正片）都应当被选中 —— 与 Jellyfin 的聚合行为一致。
        /// </summary>
        [TestMethod]
        public void TwoBigTitles_AreBothSelected()
        {
            var result = DvdVobSelector.Select(
                new[]
                {
                    Vob("VTS_01_0.VOB", 50L * 1024 * 1024),
                    Vob("VTS_01_1.VOB", Gib),
                    Vob("VTS_02_0.VOB", 50L * 1024 * 1024),
                    Vob("VTS_02_1.VOB", Gib),
                },
                titleNumber: null);

            CollectionAssert.AreEqual(
                new[] { "VTS_01_1.VOB", "VTS_02_1.VOB" },
                Names(result));
        }

        /// <summary>
        /// 这里有意偏离 Jellyfin：它用文件名的最后一个下划线段做 title 键，于是
        /// VTS_01_1 与 VTS_02_1 的键都是 "1"，小 title 会被错误地并进大 title 的结果里。
        /// 本实现用中间段的 VTS 号做键，只返回真正超阈值的那一卷。
        /// </summary>
        [TestMethod]
        public void SmallTitle_IsNotMergedIntoBigTitle_UnlikeJellyfinKeys()
        {
            var result = DvdVobSelector.Select(
                new[]
                {
                    Vob("VTS_01_0.VOB", 50L * 1024 * 1024),
                    Vob("VTS_01_1.VOB", Gib),
                    Vob("VTS_01_2.VOB", 500L * 1024 * 1024),
                    Vob("VTS_02_0.VOB", 50L * 1024 * 1024),
                    Vob("VTS_02_1.VOB", 500L * 1024 * 1024),
                },
                titleNumber: null);

            CollectionAssert.AreEqual(
                new[] { "VTS_01_1.VOB", "VTS_01_2.VOB" },
                Names(result));
        }

        [TestMethod]
        public void ExplicitTitleNumber_WinsOverSizeHeuristic()
        {
            var result = DvdVobSelector.Select(
                new[]
                {
                    Vob("VTS_01_0.VOB", 20L * 1024 * 1024),
                    Vob("VTS_01_1.VOB", Gib),
                    Vob("VTS_02_0.VOB", 20L * 1024 * 1024),
                    Vob("VTS_02_1.VOB", 50L * 1024 * 1024),
                },
                titleNumber: 2);

            CollectionAssert.AreEqual(
                new[] { "VTS_02_1.VOB" },
                Names(result));
        }

        [TestMethod]
        public void NoVobs_ReturnsEmpty()
        {
            var result = DvdVobSelector.Select(System.Array.Empty<DvdVobSelector.VobEntry>(), null);

            Assert.AreEqual(0, result.Count);
        }

        /// <summary>
        /// 文件名不符合 VTS_0X_C 结构时，菜单已经排除，剩下的全给 —— 不能返回空，
        /// 否则一张结构不规范的盘会完全没有可播放文件。
        /// </summary>
        [TestMethod]
        public void NonStandardNames_FallBackToAllPlayable()
        {
            var result = DvdVobSelector.Select(
                new[]
                {
                    Vob("VIDEO_TS.VOB", 10L * 1024 * 1024),
                    Vob("custom_a.VOB", 100L * 1024 * 1024),
                    Vob("custom_b.VOB", 100L * 1024 * 1024),
                },
                titleNumber: null);

            CollectionAssert.AreEqual(
                new[] { "custom_a.VOB", "custom_b.VOB" },
                Names(result));
        }
    }
}
