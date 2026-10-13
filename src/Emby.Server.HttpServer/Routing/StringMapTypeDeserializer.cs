using System;
using System.Collections.Generic;

namespace Emby.Server.HttpServer.Routing
{
    /// <summary>
    /// 序列化缓存委托：从字符串 map 创建/填充类型实例（等价旧 StringMapTypeDeserializer）。
    /// </summary>
    public class StringMapTypeDeserializer
    {
        internal class PropertySerializerEntry
        {
            public PropertySerializerEntry(Action<object, object> propertySetFn, Func<string, object> propertyParseStringFn)
            {
                PropertySetFn = propertySetFn;
                PropertyParseStringFn = propertyParseStringFn;
            }

            public Action<object, object> PropertySetFn;
            public Func<string, object> PropertyParseStringFn;
            public Type PropertyType;
        }

        private readonly Type type;
        private readonly Dictionary<string, PropertySerializerEntry> propertySetterMap
            = new Dictionary<string, PropertySerializerEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly Func<Type, object> _CreateInstanceFn;
        private readonly Func<Type, Func<string, object>> _GetParseFn;

        public StringMapTypeDeserializer(Func<Type, object> createInstanceFn, Func<Type, Func<string, object>> getParseFn, Type type)
        {
            _CreateInstanceFn = createInstanceFn;
            _GetParseFn = getParseFn;
            this.type = type;

            foreach (var propertyInfo in RestPath.GetSerializableProperties(type))
            {
                var propertySetFn = TypeAccessor.GetSetPropertyMethod(type, propertyInfo);
                var propertyType = propertyInfo.PropertyType;
                var propertyParseStringFn = GetParseFn(propertyType);
                var propertySerializer = new PropertySerializerEntry(propertySetFn, propertyParseStringFn) { PropertyType = propertyType };

                propertySetterMap[propertyInfo.Name] = propertySerializer;
            }
        }

        public Func<string, object> GetParseFn(Type propertyType)
        {
            if (propertyType == typeof(string))
                return s => s;

            return _GetParseFn(propertyType);
        }

        public object PopulateFromMap(object instance, IDictionary<string, string> keyValuePairs)
        {
            string propertyName = null;
            string propertyTextValue = null;
            PropertySerializerEntry propertySerializerEntry = null;

            if (instance == null)
                instance = _CreateInstanceFn(type);

            foreach (var pair in keyValuePairs)
            {
                propertyName = pair.Key;
                propertyTextValue = pair.Value;

                if (string.IsNullOrEmpty(propertyTextValue))
                {
                    continue;
                }

                if (!propertySetterMap.TryGetValue(propertyName, out propertySerializerEntry))
                {
                    if (propertyName == "v")
                    {
                        continue;
                    }

                    continue;
                }

                if (propertySerializerEntry.PropertySetFn == null)
                {
                    continue;
                }

                if (propertySerializerEntry.PropertyType == typeof(bool))
                {
                    //InputExtensions.cs#530 MVC Checkbox helper emits extra hidden input field, generating 2 values, first is the real value
                    propertyTextValue = LeftPart(propertyTextValue, ',');
                }

                var value = propertySerializerEntry.PropertyParseStringFn(propertyTextValue);
                if (value == null)
                {
                    continue;
                }
                propertySerializerEntry.PropertySetFn(instance, value);
            }

            return instance;
        }

        public static string LeftPart(string strVal, char needle)
        {
            if (strVal == null) return null;
            var pos = strVal.IndexOf(needle);
            return pos == -1
                ? strVal
                : strVal.Substring(0, pos);
        }
    }

    internal class TypeAccessor
    {
        public static Action<object, object> GetSetPropertyMethod(Type type, System.Reflection.PropertyInfo propertyInfo)
        {
            if (!propertyInfo.CanWrite || propertyInfo.GetIndexParameters().Length > 0) return null;

            var setMethodInfo = propertyInfo.SetMethod;
            return (instance, value) => setMethodInfo.Invoke(instance, new[] { value });
        }
    }
}
