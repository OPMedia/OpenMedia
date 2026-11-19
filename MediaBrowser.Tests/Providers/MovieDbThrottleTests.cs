using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MediaBrowser.Providers.Movies;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Providers
{
    /// <summary>
    /// MovieDb 节流：并发调用必须顺序预留 300ms 的槽位，而不是一起读到同一个 _lastRequestTicks。
    /// </summary>
    [TestClass]
    public class MovieDbThrottleTests
    {
        private const long TicksPerMs = TimeSpan.TicksPerMillisecond;

        private static readonly FieldInfo LastRequestField = typeof(MovieDbProvider).GetField(
            "_lastRequestTicks", BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly MethodInfo ReserveMethod = typeof(MovieDbProvider).GetMethod(
            "ReserveThrottleDelayTicks", BindingFlags.Static | BindingFlags.NonPublic);

        private static long Reserve(long nowTicks)
        {
            return (long)ReserveMethod.Invoke(null, new object[] { nowTicks });
        }

        [TestMethod]
        public void Reserve_RepeatedCallsQueueAtInterval()
        {
            var now = 638000000000000000L;
            LastRequestField.SetValue(null, 0L);

            Assert.AreEqual(0L, Reserve(now));
            Assert.AreEqual(300 * TicksPerMs, Reserve(now));
            Assert.AreEqual(600 * TicksPerMs, Reserve(now));
            Assert.AreEqual(900 * TicksPerMs, Reserve(now));
        }

        [TestMethod]
        public void Reserve_ConcurrentCallsReserveDistinctSlots()
        {
            const int count = 12;
            var now = 638000000000000000L;
            LastRequestField.SetValue(null, 0L);

            var delays = new ConcurrentBag<long>();
            Parallel.For(0, count, _ => delays.Add(Reserve(now)));

            var expected = Enumerable.Range(0, count).Select(i => (long)i * 300 * TicksPerMs).OrderBy(v => v).ToArray();
            CollectionAssert.AreEqual(expected, delays.OrderBy(v => v).ToArray());
            Assert.AreEqual(now + (count - 1) * 300 * TicksPerMs, (long)LastRequestField.GetValue(null));
        }
    }
}
