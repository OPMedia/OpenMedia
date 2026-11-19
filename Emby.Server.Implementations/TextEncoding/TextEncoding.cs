using System;
using System.Text;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Text;
using UtfUnknown;

namespace Emby.Server.Implementations.TextEncoding
{
    public class TextEncoding : ITextEncoding
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;

        // json 参数保留：ApplicationHost 和测试都按这个签名构造实例。
        public TextEncoding(IFileSystem fileSystem, ILogger logger, IJsonSerializer json)
        {
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public Encoding GetASCIIEncoding()
        {
            return Encoding.ASCII;
        }

        private Encoding GetInitialEncoding(byte[] buffer, int count)
        {
            if (count >= 3)
            {
                if (buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf)
                    return Encoding.UTF8;
            }

            if (count >= 2)
            {
                // FE FF = UTF-16 big-endian BOM, FF FE = little-endian。
                // 原实现两者都返回 Encoding.Unicode（小端），BE 内容会被逐字节颠倒。
                if (buffer[0] == 0xfe && buffer[1] == 0xff)
                    return Encoding.BigEndianUnicode;

                if (buffer[0] == 0xff && buffer[1] == 0xfe)
                    return Encoding.Unicode;
            }

            if (count >= 4)
            {
                if (buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0xfe && buffer[3] == 0xff)
                    return Encoding.UTF32;
            }

            if (count >= 3)
            {
                if (buffer[0] == 0x2b && buffer[1] == 0x2f && buffer[2] == 0x76)
#pragma warning disable SYSLIB0001 // UTF-7 是旧字幕文件的既有编码，改 UTF-8 会错解码 +base64- 转义
                    return Encoding.UTF7;
#pragma warning restore SYSLIB0001
            }

            var result = new TextEncodingDetect().DetectEncoding(buffer, count);

            switch (result)
            {
                case TextEncodingDetect.CharacterEncoding.Ansi:
                    return Encoding.ASCII;
                case TextEncodingDetect.CharacterEncoding.Ascii:
                    return Encoding.ASCII;
                case TextEncodingDetect.CharacterEncoding.Utf16BeBom:
                case TextEncodingDetect.CharacterEncoding.Utf16BeNoBom:
                    return Encoding.BigEndianUnicode;
                case TextEncodingDetect.CharacterEncoding.Utf16LeBom:
                case TextEncodingDetect.CharacterEncoding.Utf16LeNoBom:
                    return Encoding.Unicode;
                case TextEncodingDetect.CharacterEncoding.Utf8Bom:
                    return Encoding.UTF8;
                case TextEncodingDetect.CharacterEncoding.Utf8Nobom:
                    return Encoding.UTF8;
                default:
                    return null;
            }
        }

        public string GetDetectedEncodingName(byte[] bytes, int count, string language, bool enableLanguageDetection)
        {
            // enableLanguageDetection 保留在签名里（ITextEncoding 与 SubtitleEncoder 都还在传），
            // 但不再做语种检测：跟随 Jellyfin 移除内置语种检测，检测字符集只用字节统计模型。
            // language 仍然有效：调用方从媒体元数据带来的已知语言会用于 charset 兜底。
            var index = 0;

            var encoding = GetInitialEncoding(bytes, count);

            // 带 BOM 或结构上可判定的编码必须在这里直接返回。
            // 原来只有 UTF-8 早返回，UTF-16/UTF-32 会落到下面的字符集检测 ——
            // 而字符集检测对 UTF-16 只会报 "ascii"，随后被当 ASCII/1252 解码，
            // 这就是 UTF-16 字幕读出来是乱码 + 满屏 NUL 的根因。
            if (encoding != null)
            {
                if (encoding.Equals(Encoding.UTF8))
                {
                    return "utf-8";
                }

                if (encoding.Equals(Encoding.Unicode))
                {
                    return "utf-16";
                }

                if (encoding.Equals(Encoding.BigEndianUnicode))
                {
                    return "utf-16BE";
                }

                if (encoding.Equals(Encoding.UTF32))
                {
                    return "utf-32";
                }

#pragma warning disable SYSLIB0001 // 旧字幕的既有编码，不是新数据
                if (encoding.Equals(Encoding.UTF7))
                {
                    return "utf-7";
                }
#pragma warning restore SYSLIB0001
            }

            var charset = DetectCharset(bytes, index, count, language);

            if (!string.IsNullOrWhiteSpace(charset))
            {
                if (string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase))
                {
                    return "utf-8";
                }

                if (!string.Equals(charset, "windows-1252", StringComparison.OrdinalIgnoreCase))
                {
                    return charset;
                }
            }

            if (!string.IsNullOrWhiteSpace(language))
            {
                return GetFileCharacterSetFromLanguage(language);
            }

            return null;
        }

        public Encoding GetEncodingFromCharset(string charset)
        {
            if (string.IsNullOrWhiteSpace(charset))
            {
                throw new ArgumentNullException("charset");
            }

            _logger.Debug("Getting encoding object for character set: {0}", charset);

            // .NET 5+ 默认禁用了 UTF-7 的名字解析（GetEncoding("utf-7") 抛
            // NotSupportedException，除非打开 AppContext switch），但 Encoding.UTF7
            // 属性本身仍可用。旧字幕里确实存在 UTF-7，走属性保住这条路径。
            if (string.Equals(charset, "utf-7", StringComparison.OrdinalIgnoreCase))
            {
#pragma warning disable SYSLIB0001 // 旧字幕的既有编码，不是新数据
                return Encoding.UTF7;
#pragma warning restore SYSLIB0001
            }

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                // 第二道：去掉连字符再试（"windows-1252" -> "windows1252"）。
                // 这个 fallback 本身也可能失败，原实现没兜住，异常会一路冒到
                // SubtitleEncoder，导致一条字幕的字符集猜错就整个转换失败。
                var stripped = charset.Replace("-", string.Empty);

                try
                {
                    return Encoding.GetEncoding(stripped);
                }
                catch (Exception inner) when (inner is ArgumentException or NotSupportedException)
                {
                    _logger.Warn("Unknown character set '{0}', falling back to UTF-8", charset);
                    return Encoding.UTF8;
                }
            }
        }

        public Encoding GetDetectedEncoding(byte[] bytes, int size, string language, bool enableLanguageDetection)
        {
            var charset = GetDetectedEncodingName(bytes, size, language, enableLanguageDetection);

            return GetEncodingFromCharset(charset);
        }

        private string GetFileCharacterSetFromLanguage(string language)
        {
            // https://developer.xamarin.com/api/type/System.Text.Encoding/

            switch (language.ToLower())
            {
                case "tha":
                    return "windows-874";
                case "hun":
                    return "windows-1252";
                case "pol":
                case "cze":
                case "ces":
                case "slo":
                case "srp":
                case "hrv":
                case "rum":
                case "ron":
                case "rom":
                case "rup":
                    return "windows-1250";
                // albanian
                case "alb":
                case "sqi":
                    return "windows-1250";
                // slovak
                case "slk":
                case "slv":
                    return "windows-1250";
                case "ara":
                    return "windows-1256";
                case "heb":
                    return "windows-1255";
                case "grc":
                    return "windows-1253";
                // greek
                case "gre":
                case "ell":
                    return "windows-1253";
                case "crh":
                case "ota":
                case "tur":
                    return "windows-1254";
                // bulgarian
                case "bul":
                case "bgr":
                    return "windows-1251";
                case "rus":
                    return "windows-1251";
                case "vie":
                    return "windows-1258";
                case "kor":
                    // "cp949" 不是可解析的编码名，CodePages 里对应的是 ks_c_5601-1987（code page 949）。
                    // 用 euc-kr 会落到 code page 51949，扩展区韩文会错码。
                    return "ks_c_5601-1987";
                default:
                    return "windows-1252";
            }
        }

        private string DetectCharset(byte[] bytes, int index, int count, string language)
        {
            var charset = CharsetDetector.DetectFromBytes(bytes, index, count).Detected?.EncodingName;

            // This is often incorrectly indetected. If this happens, try to use other techniques instead
            if (string.Equals("x-mac-cyrillic", charset, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(language))
                {
                    return null;
                }
            }

            return charset;
        }
    }
}
