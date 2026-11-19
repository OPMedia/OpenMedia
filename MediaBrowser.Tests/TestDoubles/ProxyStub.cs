using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace MediaBrowser.Tests.TestDoubles;

/// <summary>
/// 用 DispatchProxy 生成轻量接口桩：只登记被测路径真正调用到的方法，
/// 其余方法返回默认值，避免为每个测试手写十几个空接口实现。
/// </summary>
public class ProxyStub<T> : DispatchProxy where T : class
{
    private readonly Dictionary<string, Func<object[], object>> _handlers = new Dictionary<string, Func<object[], object>>(StringComparer.Ordinal);

    public ProxyStub()
    {
    }

    public static T Create(params (string Name, Func<object[], object> Handler)[] handlers)
    {
        object proxy = DispatchProxy.Create<T, ProxyStub<T>>();
        var stub = (ProxyStub<T>)proxy;
        foreach (var (name, handler) in handlers)
        {
            stub._handlers[name] = handler;
        }

        return (T)proxy;
    }

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        if (_handlers.TryGetValue(targetMethod.Name, out var handler))
        {
            return handler(args ?? Array.Empty<object>());
        }

        Type returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return typeof(Task).GetMethod(nameof(Task.FromResult))
                .MakeGenericMethod(returnType.GetGenericArguments()[0])
                .Invoke(null, new object[] { null });
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }
}
