using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emby.Common.Implementations.Serialization
{
    /// <summary>
    /// DateTime 转换器：复刻旧 ServiceStack.Text 的 ISO8601/"O" 格式与
    /// AlwaysUseUtc/AssumeUtc 语义（Unspecified 视为 UTC，Local 归一化为 UTC）。
    /// 读侧容错 null/空串（旧吞错语义）。
    /// </summary>
    public sealed class EmbyDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return default;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Unexpected token {reader.TokenType} for DateTime");
            }

            var s = reader.GetString();
            if (string.IsNullOrEmpty(s))
            {
                return default;
            }

            var dt = reader.GetDateTime();
            return Normalize(dt);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            var dt = Normalize(value);
            writer.WriteStringValue(dt.ToString("O", CultureInfo.InvariantCulture));
        }

        private static DateTime Normalize(DateTime dt)
        {
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            };
        }
    }
}
