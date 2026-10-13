using System;
using System.Globalization;

namespace Emby.Common.Implementations.Serialization
{
    /// <summary>
    /// REST 查询串 → 请求 DTO 属性的解析器（替代 ServiceStack.Text.JsvReader.GetParseFn）。
    /// 请求 DTO 属性类型盘点：string/bool?/Guid/int?/long?/double?/DateTime/枚举/string[]，
    /// 无 List&lt;T&gt; 直接绑定（列表参数走 string + 服务内 split）。
    /// </summary>
    public static class QueryStringParser
    {
        public static Func<string, object> GetParseFn(Type propertyType)
        {
            var underlying = Nullable.GetUnderlyingType(propertyType);
            var targetType = underlying ?? propertyType;

            Func<string, object> core;
            if (targetType == typeof(string))
            {
                core = s => s;
            }
            else if (targetType == typeof(bool))
            {
                // "1"/"0" 容错（SST ParseUtils 同款语义）
                core = s => bool.TryParse(s, out var b) ? b : Convert.ToInt32(s, CultureInfo.InvariantCulture) != 0;
            }
            else if (targetType == typeof(Guid))
            {
                core = s => Guid.Parse(s);
            }
            else if (targetType == typeof(DateTime))
            {
                core = s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }
            else if (targetType.IsEnum)
            {
                core = s => Enum.Parse(targetType, s, ignoreCase: true);
            }
            else if (targetType == typeof(string[]))
            {
                core = s => s.Split(',');
            }
            else
            {
                // int/long/double/float/short/byte/decimal
                core = s => Convert.ChangeType(s, targetType, CultureInfo.InvariantCulture);
            }

            if (underlying != null)
            {
                return s => string.IsNullOrEmpty(s) ? null : core(s);
            }

            return core;
        }
    }
}
