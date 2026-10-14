using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emby.Common.Implementations.Serialization
{
    /// <summary>
    /// 宽容字符串转换器：复刻旧 ServiceStack.Text ThrowOnDeserializationError=false
    /// 的数字/布尔 → 字符串静默强转语义。
    ///
    /// 触发场景（实测）：ffprobe 9 的 JSON 输出中 start_pts 等字段为数字
    /// （"start_pts": 0），而 Emby.Server.MediaEncoding.Probing.MediaStreamInfo
    /// 将其声明为 string（旧 SST 会强转，STJ 默认抛
    /// "Cannot get the value of a token type 'Number' as a string"），
    /// 导致全部媒体探测失败 → 转码/播放链路整体不可用。
    ///
    /// 读：String 原样；Number → 不变文化文本；True/False → "true"/"false"；
    ///     Null → null；其余 token（对象/数组）跳过并返回 null（吞错语义）。
    /// 写：与默认行为一致（字符串原样、null 写 null），不改变序列化契约。
    /// </summary>
    public sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();

                case JsonTokenType.Null:
                    return null;

                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out var l))
                    {
                        return l.ToString(CultureInfo.InvariantCulture);
                    }
                    if (reader.TryGetDouble(out var d))
                    {
                        return d.ToString("R", CultureInfo.InvariantCulture);
                    }
                    return null;

                case JsonTokenType.True:
                    return "true";

                case JsonTokenType.False:
                    return "false";

                default:
                    // 旧 SST 吞错语义：无法解析的 token 跳过并置 null，不抛异常
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value);
            }
        }
    }
}
