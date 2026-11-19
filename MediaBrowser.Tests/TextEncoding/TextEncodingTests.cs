using System;
using System.Text;
using MediaBrowser.Tests.PgData;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.TextEncoding
{
    /// <summary>
    /// 字幕字符集链路的回归测试。
    ///
    /// 背景：.NET Core 起 BCL 只内置 UTF-8 / UTF-16 / UTF-32 / ASCII，传统代码页
    /// 必须显式注册 <see cref="CodePagesEncodingProvider"/>。生产入口在
    /// MediaBrowser.Server/Program.cs 的 Main 里注册；这里注册同一份，保证测试
    /// 走的是和线上一致的编码可用性。
    /// </summary>
    [TestClass]
    public class TextEncodingTests
    {
        private static readonly Emby.Server.Implementations.TextEncoding.TextEncoding Subject = CreateSubject();

        [ClassInitialize]
        public static void RegisterCodePages(TestContext context)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private static Emby.Server.Implementations.TextEncoding.TextEncoding CreateSubject()
        {
            return new Emby.Server.Implementations.TextEncoding.TextEncoding(
                fileSystem: null,
                logger: new FakeLogManager().GetLogger("TextEncodingTests"),
                json: null);
        }

        /// <summary>
        /// 中文 GBK / 日文 Shift-JIS / 俄文 cp1251 —— 这三种是实际媒体库里最常见的
        /// 非 UTF-8 字幕编码。在 provider 未注册时它们全部抛 ArgumentException。
        /// </summary>
        [TestMethod]
        [DataRow("gb18030", new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 }, "中文")]
        [DataRow("gbk", new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 }, "中文")]
        [DataRow("shift_jis", new byte[] { 0x93, 0xFA, 0x96, 0x7B }, "日本")]
        [DataRow("windows-1251", new byte[] { 0xD0, 0xF3, 0xF1, 0xF1, 0xEA, 0xE8, 0xE9 }, "Русский")]
        public void GetEncodingFromCharset_DecodesLegacyCodePages(string charset, byte[] bytes, string expected)
        {
            var encoding = Subject.GetEncodingFromCharset(charset);

            Assert.AreEqual(expected, encoding.GetString(bytes));
        }

        /// <summary>
        /// "cp949" 不是可解析的编码名（CodePages 里对应 ks_c_5601-1987 / code page 949），
        /// 语言兜底表之前返回的就是这个无效名。
        /// </summary>
        [TestMethod]
        public void GetEncodingFromCharset_KoreanCodePage949_Resolves()
        {
            var encoding = Subject.GetEncodingFromCharset("ks_c_5601-1987");

            Assert.AreEqual(949, encoding.CodePage);
        }

        /// <summary>
        /// 未知字符集不能抛异常 —— 一条字幕的字符集猜错不该让整个转换失败。
        /// </summary>
        [TestMethod]
        [DataRow("x-not-a-real-charset")]
        [DataRow("windows-9999")]
        [DataRow("utf-7")]
        public void GetEncodingFromCharset_UnknownCharset_FallsBackWithoutThrowing(string charset)
        {
            var encoding = Subject.GetEncodingFromCharset(charset);

            Assert.IsNotNull(encoding);
        }

        /// <summary>
        /// UTF-16 带 BOM 必须在初始编码判定阶段直接返回；
        /// 否则会落到字符集检测（对 UTF-16 只会报 ascii），被当 ASCII/1252 解码 → 乱码。
        /// </summary>
        [TestMethod]
        public void GetDetectedEncodingName_Utf16LittleEndianBom_DetectsUtf16()
        {
            // FF FE + "中文" (U+4E2D U+6587) little-endian
            var bytes = new byte[] { 0xFF, 0xFE, 0x2D, 0x4E, 0x87, 0x65 };

            var name = Subject.GetDetectedEncodingName(bytes, bytes.Length, string.Empty, enableLanguageDetection: false);

            Assert.AreEqual("utf-16", name);
            Assert.AreEqual("中文", Subject.GetEncodingFromCharset(name).GetString(bytes, 2, bytes.Length - 2));
        }

        [TestMethod]
        public void GetDetectedEncodingName_Utf16BigEndianBom_DetectsUtf16Be()
        {
            // FE FF + "中文" big-endian
            var bytes = new byte[] { 0xFE, 0xFF, 0x4E, 0x2D, 0x65, 0x87 };

            var name = Subject.GetDetectedEncodingName(bytes, bytes.Length, string.Empty, enableLanguageDetection: false);

            Assert.AreEqual("utf-16BE", name);
            Assert.AreEqual("中文", Subject.GetEncodingFromCharset(name).GetString(bytes, 2, bytes.Length - 2));
        }

        [TestMethod]
        public void GetDetectedEncodingName_Utf8Bom_DetectsUtf8()
        {
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 0xE4, 0xB8, 0xAD, 0xE6, 0x96, 0x87 };

            var name = Subject.GetDetectedEncodingName(bytes, bytes.Length, string.Empty, enableLanguageDetection: false);

            Assert.AreEqual("utf-8", name);
        }

        /// <summary>
        /// 没有 BOM 的 GBK 内容仍然要能被检测成 gb18030 并正确解码 —— 这条覆盖
        /// 「检测 → 查编码」的完整链路。
        /// </summary>
        [TestMethod]
        public void GetDetectedEncodingName_GbkWithoutBom_RoundTrips()
        {
            // 真实字幕是 KB 级；4 个字节不足以让字符集统计模型收敛，
            // 这里用一段有代表性的中文正文，长度贴近真实 .srt 的头部。
            const string Text = "这是一段中文字幕测试内容，用于验证字符集检测链路能否正确识别 GBK 编码。\r\n"
                + "第二行同样使用简体中文，包含标点符号、数字 12345 与英文 mixed content。\r\n";

            var bytes = Encoding.GetEncoding("gb18030").GetBytes(Text);

            var name = Subject.GetDetectedEncodingName(bytes, bytes.Length, "chi", enableLanguageDetection: false);

            Assert.IsNotNull(name);
            Assert.AreEqual(Text, Subject.GetEncodingFromCharset(name).GetString(bytes));
        }
    }
}
