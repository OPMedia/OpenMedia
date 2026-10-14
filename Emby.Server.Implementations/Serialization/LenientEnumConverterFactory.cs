using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emby.Common.Implementations.Serialization
{
    /// <summary>
    /// 宽容枚举转换器工厂：复刻旧 ServiceStack.Text ThrowOnDeserializationError=false
    /// 的吞错语义——外部 JSON（包仓库/TMDb 等）含未知枚举值时回退默认值而非抛异常。
    /// 写路径：枚举名字符串（前端契约）。
    /// </summary>
    public sealed class LenientEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            var underlying = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
            return underlying.IsEnum;
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var underlying = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
            if (underlying != typeToConvert)
            {
                // Nullable<T>：STJ 要求转换器与声明类型精确匹配
                var nullableType = typeof(LenientNullableEnumConverter<>).MakeGenericType(underlying);
                return (JsonConverter)Activator.CreateInstance(nullableType);
            }
            var converterType = typeof(LenientEnumConverter<>).MakeGenericType(underlying);
            return (JsonConverter)Activator.CreateInstance(converterType);
        }
    }

    internal sealed class LenientNullableEnumConverter<T> : JsonConverter<T?>
        where T : struct, Enum
    {
        private readonly LenientEnumConverter<T> _inner = new LenientEnumConverter<T>();

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }
            return _inner.Read(ref reader, typeof(T), options);
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                _inner.Write(writer, value.Value, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    internal sealed class LenientEnumConverter<T> : JsonConverter<T>
        where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return default;

                case JsonTokenType.String:
                    var s = reader.GetString();
                    if (string.IsNullOrEmpty(s))
                    {
                        return default;
                    }
                    if (Enum.TryParse<T>(s, ignoreCase: true, out var parsed))
                    {
                        return parsed;
                    }
                    if (long.TryParse(s, out var numeric))
                    {
                        return (T)Enum.ToObject(typeof(T), numeric);
                    }
                    return default; // 未知枚举值 → 默认（SST 吞错语义）

                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out var number))
                    {
                        return (T)Enum.ToObject(typeof(T), number);
                    }
                    return default;

                default:
                    return default;
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }

        // 字典键支持（如 ItemImageInfo 的 Dictionary<ImageType, string>）
        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return Read(ref reader, typeToConvert, options);
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WritePropertyName(value.ToString());
        }
    }
}
