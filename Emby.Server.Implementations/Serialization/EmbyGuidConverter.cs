using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emby.Common.Implementations.Serialization
{
    /// <summary>
    /// Guid 转换器：写 "N" 格式（32 位无连字符，与旧 ServiceStack.Text 一致），
    /// Guid.Empty 写 null（复刻旧 SerializeFn 语义，前端 falsy 判断不受影响）；
    /// 读侧接受 N/D/B/P 全格式（修复 STJ 只认 D 格式的破坏性差异）。
    /// </summary>
    public sealed class EmbyGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return Guid.Empty;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (string.IsNullOrEmpty(s))
                {
                    return Guid.Empty;
                }

                return Guid.Parse(s);
            }

            throw new JsonException($"Unexpected token {reader.TokenType} for Guid");
        }

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
        {
            if (value == Guid.Empty)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value.ToString("N"));
            }
        }
    }
}
