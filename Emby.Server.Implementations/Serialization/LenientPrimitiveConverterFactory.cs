using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emby.Common.Implementations.Serialization
{
    /// <summary>
    /// 宽容值类型转换器工厂：复刻旧 ServiceStack.Text ThrowOnDeserializationError=false
    /// 的吞错语义（null/"" → 默认值，"42" → 42，"true" → true）。
    /// Guid/DateTime/枚举的容错由各自转换器承担，不进本工厂。
    /// </summary>
    public sealed class LenientPrimitiveConverterFactory : JsonConverterFactory
    {
        private static readonly ConcurrentDictionary<Type, JsonConverter> Cache = new ConcurrentDictionary<Type, JsonConverter>();

        public override bool CanConvert(Type typeToConvert)
        {
            var underlying = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
            return underlying == typeof(bool)
                || underlying == typeof(int)
                || underlying == typeof(long)
                || underlying == typeof(short)
                || underlying == typeof(byte)
                || underlying == typeof(float)
                || underlying == typeof(double)
                || underlying == typeof(decimal);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return Cache.GetOrAdd(typeToConvert, t =>
            {
                var underlying = Nullable.GetUnderlyingType(t) ?? t;
                if (underlying != t)
                {
                    // Nullable<T>：包装内层转换器（STJ 要求转换器与声明类型精确匹配）
                    var nullableType = typeof(LenientNullableConverter<>).MakeGenericType(underlying);
                    return (JsonConverter)Activator.CreateInstance(nullableType);
                }
                var converterType = typeof(LenientConverter<>).MakeGenericType(underlying);
                return (JsonConverter)Activator.CreateInstance(converterType);
            });
        }
    }

    internal sealed class LenientNullableConverter<T> : JsonConverter<T?>
        where T : struct
    {
        private readonly LenientConverter<T> _inner = new LenientConverter<T>();

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
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

    internal sealed class LenientConverter<T> : JsonConverter<T>
        where T : struct
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
                    if (typeof(T) == typeof(bool))
                    {
                        if (bool.TryParse(s, out var b))
                        {
                            return (T)(object)b;
                        }
                        return (T)(object)(Convert.ToInt32(s, CultureInfo.InvariantCulture) != 0);
                    }
                    return (T)Convert.ChangeType(s, typeof(T), CultureInfo.InvariantCulture);

                case JsonTokenType.True:
                case JsonTokenType.False:
                    if (typeof(T) == typeof(bool))
                    {
                        return (T)(object)(reader.TokenType == JsonTokenType.True);
                    }
                    break;

                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out var l))
                    {
                        return (T)Convert.ChangeType(l, typeof(T), CultureInfo.InvariantCulture);
                    }
                    if (reader.TryGetDouble(out var d))
                    {
                        return (T)Convert.ChangeType(d, typeof(T), CultureInfo.InvariantCulture);
                    }
                    break;
            }

            throw new JsonException($"Unexpected token {reader.TokenType} for {typeof(T).Name}");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            // 写路径直接输出原始值（不得回调带本工厂的 options，否则无限递归）
            if (typeof(T) == typeof(bool))
            {
                writer.WriteBooleanValue((bool)(object)value);
            }
            else if (typeof(T) == typeof(int))
            {
                writer.WriteNumberValue((int)(object)value);
            }
            else if (typeof(T) == typeof(long))
            {
                writer.WriteNumberValue((long)(object)value);
            }
            else if (typeof(T) == typeof(short))
            {
                writer.WriteNumberValue((short)(object)value);
            }
            else if (typeof(T) == typeof(byte))
            {
                writer.WriteNumberValue((byte)(object)value);
            }
            else if (typeof(T) == typeof(float))
            {
                writer.WriteNumberValue((float)(object)value);
            }
            else if (typeof(T) == typeof(double))
            {
                writer.WriteNumberValue((double)(object)value);
            }
            else if (typeof(T) == typeof(decimal))
            {
                writer.WriteNumberValue((decimal)(object)value);
            }
            else
            {
                writer.WriteNumberValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            }
        }
    }
}
