using System;
using System.Reflection;
using Emby.Server.Implementations.Playlists;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Playlists
{
    /// <summary>
    /// NormalizeItemPath 把结果直接写进 m3u/pls 的 Path：本地文件要相对化，
    /// 远程 URL 与 UNC 路径必须保持可解析。
    /// </summary>
    [TestClass]
    public class PlaylistRelativePathTests
    {
        private static string Invoke(string folderPath, string fileAbsolutePath)
        {
            var method = typeof(PlaylistManager).GetMethod("MakeRelativePath", BindingFlags.Static | BindingFlags.NonPublic);
            return (string)method.Invoke(null, new object[] { folderPath, fileAbsolutePath });
        }

        [DataTestMethod]
        [DataRow("/media/music", "/media/music/song.mp3", "song.mp3")]
        [DataRow("/media/music/", "/media/music/sub/song with space.mp3", "sub/song with space.mp3")]
        [DataRow("/media/music", "/other/song.mp3", "../../other/song.mp3")]
        [DataRow("/media/music", "/media/music/100%25.mp3", "100%25.mp3")]
        public void MakeRelativePath_LocalFilesAreRelativeToPlaylistFolder(string folder, string file, string expected)
        {
            Assert.AreEqual(expected, Invoke(folder, file));
        }

        [TestMethod]
        public void MakeRelativePath_CrossSchemeKeepsAbsoluteUrl()
        {
            Assert.AreEqual("http://host/song.mp3", Invoke("/media/music", "http://host/song.mp3"));
        }

        [TestMethod]
        public void MakeRelativePath_FileUriIsTreatedAsUri()
        {
            Assert.AreEqual("song.mp3", Invoke("/media/music", "file:///media/music/song.mp3"));
        }

        [TestMethod]
        public void MakeRelativePath_TwoUrlsStayUrlRelative()
        {
            Assert.AreEqual("../song.mp3", Invoke("http://host/list.m3u", "http://host/song.mp3"));
        }

        [TestMethod]
        public void MakeRelativePath_UncPathsKeepUriSemantics()
        {
            Assert.AreEqual("song.mp3", Invoke(@"\\server\share", @"\\server\share\song.mp3"));
            Assert.AreEqual("file:///media/music/song.mp3", Invoke(@"\\server\share", "/media/music/song.mp3"));
        }

        [TestMethod]
        public void MakeRelativePath_SamePathUsesBclDot()
        {
            Assert.AreEqual(".", Invoke("/media/music", "/media/music"));
        }

        [TestMethod]
        public void MakeRelativePath_RejectsEmptyArguments()
        {
            Assert.ThrowsException<TargetInvocationException>(() => Invoke(null, "/a"));
            Assert.ThrowsException<TargetInvocationException>(() => Invoke("/a", ""));
        }
    }
}
