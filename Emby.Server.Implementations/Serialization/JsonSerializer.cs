using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.Common.Implementations.Serialization
{
    /// <summary>
    /// System.Text.Json 实现的 IJsonSerializer（替代 ServiceStack.Text 5.1.0）。
    /// 序列化契约与旧实现逐项对齐（见 docs/json-migration-map.md §3）：
    /// PascalCase 原名、null 省略、Guid "N" 格式、枚举字符串、ISO8601/"O" 日期、
    /// 宽容反序列化（吞错语义）。
    /// </summary>
    public class JsonSerializer : IJsonSerializer
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;

        /// <summary>
        /// 全局共享的序列化配置（与旧 ServiceStack JsConfig 语义对齐）。
        /// </summary>
        public static readonly JsonSerializerOptions Options = CreateOptions();

        public JsonSerializer(IFileSystem fileSystem, ILogger logger)
        {
            _fileSystem = fileSystem;
            _logger = logger;
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                // 命名：属性原名（PascalCase，前端契约）
                PropertyNamingPolicy = null,
                DictionaryKeyPolicy = null,

                // null 处理：null 属性省略（旧 IncludeNullValues=false）
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

                // 大小写匹配（读）：放宽为超集
                PropertyNameCaseInsensitive = true,

                // 数字容错（读）："42" → 42
                NumberHandling = JsonNumberHandling.AllowReadingFromString,

                // 转义：原样输出非 ASCII
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

                WriteIndented = false,
                MaxDepth = 128
            };

            options.Converters.Add(new EmbyGuidConverter());
            options.Converters.Add(new EmbyDateTimeConverter());
            options.Converters.Add(new JsonStringEnumConverter());
            options.Converters.Add(new LenientPrimitiveConverterFactory());

            return options;
        }

        /// <summary>
        /// Serializes to stream.
        /// </summary>
        public void SerializeToStream(object obj, Stream stream)
        {
            if (obj == null)
            {
                throw new ArgumentNullException("obj");
            }

            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            System.Text.Json.JsonSerializer.Serialize(stream, obj, obj.GetType(), Options);
        }

        /// <summary>
        /// Serializes to file.
        /// </summary>
        public void SerializeToFile(object obj, string file)
        {
            if (obj == null)
            {
                throw new ArgumentNullException("obj");
            }

            if (string.IsNullOrEmpty(file))
            {
                throw new ArgumentNullException("file");
            }

            using (Stream stream = _fileSystem.GetFileStream(file, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.Read))
            {
                SerializeToStream(obj, stream);
            }
        }

        private Stream OpenFile(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072);
        }

        /// <summary>
        /// Deserializes from file.
        /// </summary>
        public object DeserializeFromFile(Type type, string file)
        {
            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            if (string.IsNullOrEmpty(file))
            {
                throw new ArgumentNullException("file");
            }

            using (Stream stream = OpenFile(file))
            {
                return DeserializeFromStream(stream, type);
            }
        }

        /// <summary>
        /// Deserializes from file.
        /// </summary>
        public T DeserializeFromFile<T>(string file)
            where T : class
        {
            if (string.IsNullOrEmpty(file))
            {
                throw new ArgumentNullException("file");
            }

            using (Stream stream = OpenFile(file))
            {
                return DeserializeFromStream<T>(stream);
            }
        }

        /// <summary>
        /// Deserializes from stream.
        /// </summary>
        public T DeserializeFromStream<T>(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            return System.Text.Json.JsonSerializer.Deserialize<T>(stream, Options);
        }

        public async Task<T> DeserializeFromStreamAsync<T>(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(stream, Options).ConfigureAwait(false);
        }

        /// <summary>
        /// Deserializes from string.
        /// </summary>
        public T DeserializeFromString<T>(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentNullException("text");
            }

            return System.Text.Json.JsonSerializer.Deserialize<T>(text, Options);
        }

        /// <summary>
        /// Deserializes from stream.
        /// </summary>
        public object DeserializeFromStream(Stream stream, Type type)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            return System.Text.Json.JsonSerializer.Deserialize(stream, type, Options);
        }

        public async Task<object> DeserializeFromStreamAsync(Stream stream, Type type)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            return await System.Text.Json.JsonSerializer.DeserializeAsync(stream, type, Options).ConfigureAwait(false);
        }

        /// <summary>
        /// Deserializes from string.
        /// </summary>
        public object DeserializeFromString(string json, Type type)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new ArgumentNullException("json");
            }

            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            return System.Text.Json.JsonSerializer.Deserialize(json, type, Options);
        }

        /// <summary>
        /// Serializes to string.
        /// </summary>
        public string SerializeToString(object obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException("obj");
            }

            return System.Text.Json.JsonSerializer.Serialize(obj, obj.GetType(), Options);
        }
    }
}
