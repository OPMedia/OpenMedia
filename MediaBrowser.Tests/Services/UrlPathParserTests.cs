using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Emby.Dlna.Api;
using MediaBrowser.Api;
using MediaBrowser.Common.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Services
{
    /// <summary>
    /// BaseApiService 与 DlnaServerService 的 GetPathValue 都依赖同一套 PathInfo 拆解语义，
    /// 包括 Skip(1) 吃掉首段的历史行为。
    /// </summary>
    [TestClass]
    public class UrlPathParserTests
    {
        private static List<string> ParseVia(Type serviceType, string pathUri)
        {
            var method = serviceType.GetMethod("Parse", BindingFlags.Instance | BindingFlags.NonPublic);
            var service = RuntimeHelpers.GetUninitializedObject(serviceType);
            return (List<string>)method.Invoke(service, new object[] { pathUri });
        }

        [DataTestMethod]
        [DataRow("/emby/Items/123?x=1", "emby|Items|123")]
        [DataRow("http://host/emby/Items/123?x=1", "emby|Items|123")]
        [DataRow("emby/Items/123", "Items|123")]
        [DataRow("/Dlna/abc/contentdirectory/control", "Dlna|abc|contentdirectory|control")]
        [DataRow("/a//b/?z=1?y=2", "a||b|?z=1")]
        [DataRow("/a/b#frag", "a|b#frag")]
        public void Parse_KeepsLegacySegmentBehaviour(string pathUri, string expected)
        {
            var expectedSegments = expected.Split('|');
            CollectionAssert.AreEqual(expectedSegments, UrlPathParser.ParsePathInfo(pathUri));
            CollectionAssert.AreEqual(expectedSegments, ParseVia(typeof(BaseApiService), pathUri));
            CollectionAssert.AreEqual(expectedSegments, ParseVia(typeof(DlnaServerService), pathUri));
        }
    }
}
