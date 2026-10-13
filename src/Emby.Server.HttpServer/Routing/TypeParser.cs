using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Emby.Server.HttpServer.Routing
{
    /// <summary>
    /// 字符串 → 基元/枚举/Guid/可空/集合 的解析工厂（替代旧 ServiceStack JsvReader.GetParseFn）。
    /// 用于 DTO 绑定：query/path/form 字符串 → 强类型属性。
    /// </summary>
    public static class TypeParser
    {
        private static readonly ConcurrentDictionary<Type, Func<string, object>> ParseFnCache =
            new ConcurrentDictionary<Type, Func<string, object>>();

        public static Func<string, object> GetParseFn(Type propertyType)
        {
            return ParseFnCache.GetOrAdd(propertyType, t => BuildParseFn(t));
        }

        private static Func<string, object> BuildParseFn(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                var innerFn = BuildParseFn(underlyingType);
                return s => string.IsNullOrEmpty(s) ? null : innerFn(s);
            }

            if (type == typeof(string))
            {
                return s => s;
            }

            if (type == typeof(bool))
            {
                return s =>
                {
                    if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s, "1", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s, "on", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s, "0", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s, "off", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s, "no", StringComparison.OrdinalIgnoreCase))
                        return false;
                    return bool.Parse(s);
                };
            }

            if (type == typeof(byte)) return s => byte.Parse(s, CultureInfo.InvariantCulture);
            if (type == typeof(sbyte)) return s => sbyte.Parse(s, CultureInfo.InvariantCulture);
            if (type == typeof(short)) return s => short.Parse(s, CultureInfo.InvariantCulture);
            if (type == typeof(ushort)) return s => ushort.Parse(s, CultureInfo.InvariantCulture);
            if (type == typeof(int)) return s => int.Parse(s, CultureInfo.InvariantCulture);
            if (type == typeof(uint)) return s => uint.Parse(s, CultureInfo.InvariantCulture);
            if (type == typeof(long)) return s => long.Parse(s, CultureInfo.InvariantCulture);
            if (type == typeof(ulong)) return s => ulong.Parse(s, CultureInfo.InvariantCulture);
            if (type == typeof(float)) return s => float.Parse(s, CultureInfo.InvariantCulture);
            if (type == typeof(double)) return s => double.Parse(s, CultureInfo.InvariantCulture);
            if (type == typeof(decimal)) return s => decimal.Parse(s, CultureInfo.InvariantCulture);

            if (type == typeof(Guid))
            {
                return s => Guid.Parse(s);
            }

            if (type == typeof(DateTime))
            {
                return s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }

            if (type == typeof(DateTimeOffset))
            {
                return s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }

            if (type == typeof(TimeSpan))
            {
                return s => TimeSpan.Parse(s, CultureInfo.InvariantCulture);
            }

            if (type.IsEnum)
            {
                return s => Enum.Parse(type, s, true);
            }

            // 集合：逗号分隔（Jsv 语义），如 "a,b,c" → List<string>、List<Guid>、string[]、int[]
            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var elementFn = BuildParseFn(elementType);
                return s => ParseArray(s, elementType, elementFn);
            }

            if (IsGenericList(type, out var listElementType))
            {
                var elementFn = BuildParseFn(listElementType);
                return s => ParseList(s, type, listElementType, elementFn);
            }

            if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(IEnumerable))
            {
                // 其他 IEnumerable 实现走 TypeConverter 兜底
            }

            // 兜底：TypeConverter / Convert.ChangeType
            var converter = TypeDescriptor.GetConverter(type);
            if (converter != null && converter.CanConvertFrom(typeof(string)))
            {
                return s => converter.ConvertFromInvariantString(s);
            }

            return s => Convert.ChangeType(s, type, CultureInfo.InvariantCulture);
        }

        private static bool IsGenericList(Type type, out Type elementType)
        {
            elementType = null;
            if (!type.IsConstructedGenericType)
            {
                return false;
            }

            var definition = type.GetGenericTypeDefinition();

            if (definition == typeof(List<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }

            if (definition == typeof(IList<>) || definition == typeof(IEnumerable<>) || definition == typeof(ICollection<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }

            // List<T> 派生类型
            if (typeof(IList).IsAssignableFrom(type))
            {
                var listInterface = type.GetInterfaces()
                    .FirstOrDefault(i => i.IsConstructedGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
                if (listInterface != null)
                {
                    elementType = listInterface.GetGenericArguments()[0];
                    return true;
                }
            }

            return false;
        }

        private static object ParseArray(string s, Type elementType, Func<string, object> elementFn)
        {
            var values = SplitValues(s);
            var result = Array.CreateInstance(elementType, values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                var value = elementFn(values[i]);
                if (value != null)
                {
                    result.SetValue(value, i);
                }
            }
            return result;
        }

        private static object ParseList(string s, Type listType, Type elementType, Func<string, object> elementFn)
        {
            var values = SplitValues(s);

            var concreteType = listType;
            if (!concreteType.IsClass || concreteType.IsAbstract)
            {
                concreteType = typeof(List<>).MakeGenericType(elementType);
            }

            var list = (IList)Activator.CreateInstance(concreteType);
            foreach (var v in values)
            {
                var value = elementFn(v);
                if (value != null)
                {
                    list.Add(value);
                }
            }
            return list;
        }

        private static string[] SplitValues(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return Array.Empty<string>();
            }

            return s.Split(',');
        }
    }
}
