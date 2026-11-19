using System;
using System.IO;
using System.Text;
using BDInfo;
using Emby.Server.Implementations.EnvironmentInfo;
using Emby.Server.Implementations.IO;
using MediaBrowser.MediaEncoding.BdInfo;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.MediaEncoding;

[TestClass]
public class BdInfoAdapterTests
{
	[TestMethod]
	public void BdRomFindsPlaylistAndAdapterReadsAsciiMpls()
	{
		var root = CreateTempDiscRoot(out var playlistPath);
		try
		{
			const string mplsText = "MPLS0100ASCII-PLAYLIST";
			File.WriteAllText(playlistPath, mplsText, new UTF8Encoding(false));
			var fileSystem = CreateFileSystem();

			var rom = new BDROM(new BdInfoDirectoryInfo(fileSystem, root));

			Assert.AreEqual("BDMV", rom.DirectoryBDMV.Name);
			Assert.AreEqual(root, rom.DirectoryRoot.FullName);
			Assert.IsTrue(rom.PlaylistFiles.ContainsKey("00000.MPLS"));

			var playlistPathFromRom = rom.PlaylistFiles["00000.MPLS"].GetFilePath();
			var fileInfo = new BdInfoFileInfo(fileSystem, fileSystem.GetFileInfo(playlistPathFromRom));
			using var reader = fileInfo.OpenText();
			Assert.AreEqual(mplsText, reader.ReadToEnd());
			Assert.AreEqual(Encoding.ASCII.GetString(File.ReadAllBytes(playlistPath)), mplsText);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[TestMethod]
	public void AdapterMapsDirectoryListingPatternsAndUtf8Text()
	{
		var root = CreateTempDiscRoot(out var playlistPath);
		try
		{
			File.WriteAllText(playlistPath, "MPLS0100", Encoding.ASCII);
			var metaDir = Path.Combine(root, "BDMV", "META", "DL");
			Directory.CreateDirectory(metaDir);
			var metaPath = Path.Combine(metaDir, "bdmt_eng.xml");
			const string title = "Café — 中文标题";
			File.WriteAllText(metaPath, title, new UTF8Encoding(false));

			var fileSystem = CreateFileSystem();
			var bdmv = new BdInfoDirectoryInfo(fileSystem, Path.Combine(root, "BDMV"));

			CollectionAssert.Contains(Array.ConvertAll(bdmv.GetDirectories(), d => d.Name), "PLAYLIST");
			Assert.AreEqual(root, bdmv.Parent.FullName);
			var playlist = new BdInfoDirectoryInfo(fileSystem, Path.Combine(root, "BDMV", "PLAYLIST"));
			Assert.AreEqual(1, playlist.GetFiles(".mpls").Length);
			Assert.AreEqual(1, playlist.GetFiles("*.mpls").Length);
			Assert.AreEqual(1, bdmv.GetFiles("*.xml", SearchOption.AllDirectories).Length);
			CollectionAssert.Contains(Array.ConvertAll(bdmv.GetFiles("bdmt_eng.xml", SearchOption.AllDirectories), f => f.Name), "bdmt_eng.xml");

			using var reader = new BdInfoFileInfo(fileSystem, fileSystem.GetFileInfo(metaPath)).OpenText();
			Assert.AreEqual(title, reader.ReadToEnd());
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	private static string CreateTempDiscRoot(out string playlistPath)
	{
		var root = Path.Combine(Path.GetTempPath(), "om-bdinfo-" + Guid.NewGuid().ToString("N"));
		playlistPath = Path.Combine(root, "BDMV", "PLAYLIST", "00000.mpls");
		Directory.CreateDirectory(Path.GetDirectoryName(playlistPath));
		Directory.CreateDirectory(Path.Combine(root, "BDMV", "CLIPINF"));
		return root;
	}

	private static IFileSystem CreateFileSystem()
	{
		return new ManagedFileSystem(new NullLogger(), new EnvironmentInfo(), ".", Path.GetTempPath(), false);
	}
}
