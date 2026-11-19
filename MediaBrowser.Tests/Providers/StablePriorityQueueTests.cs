using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Providers.Manager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests.Providers
{
    /// <summary>
    /// 钉住替代 vendored BlueRaja 队列后的稳定序：BCL PriorityQueue 本身不稳定，
    /// 同优先级必须仍按入队先后出队。
    /// </summary>
    [TestClass]
    public class StablePriorityQueueTests
    {
        [TestMethod]
        public void SamePriorityEnqueuesDequeueInFirstInFirstOutOrder()
        {
            var queue = new StablePriorityQueue<string, int>();
            for (var i = 0; i < 64; i++)
            {
                queue.Enqueue("item-" + i, 7);
            }

            CollectionAssert.AreEqual(
                Enumerable.Range(0, 64).Select(i => "item-" + i).ToArray(),
                DequeueAll(queue));
        }

        [TestMethod]
        public void LowerPriorityAlwaysDequeuesFirst()
        {
            var queue = new StablePriorityQueue<string, int>();
            queue.Enqueue("normal-1", 9);
            queue.Enqueue("urgent-1", 1);
            queue.Enqueue("normal-2", 9);
            queue.Enqueue("urgent-2", 1);

            CollectionAssert.AreEqual(
                new[] { "urgent-1", "urgent-2", "normal-1", "normal-2" },
                DequeueAll(queue));
        }

        [TestMethod]
        public void InterleavedEnqueueDequeueKeepsFifoWithinEachPriority()
        {
            var queue = new StablePriorityQueue<string, int>();
            queue.Enqueue("a", 1);
            queue.Enqueue("b", 2);
            queue.Enqueue("c", 1);

            Assert.IsTrue(queue.TryDequeue(out var first));
            Assert.AreEqual("a", first);

            queue.Enqueue("d", 1);
            CollectionAssert.AreEqual(new[] { "c", "d", "b" }, DequeueAll(queue));
        }

        [TestMethod]
        public void EnumerationIsPriorityThenInsertionOrderSnapshot()
        {
            var queue = new StablePriorityQueue<string, int>();
            queue.Enqueue("last", -1);
            queue.Enqueue("first", 3);
            queue.Enqueue("second", 3);

            CollectionAssert.AreEqual(new[] { "last", "first", "second" }, queue.ToArray());
            Assert.AreEqual(3, queue.Count);
            CollectionAssert.AreEqual(new[] { "last", "first", "second" }, DequeueAll(queue));
        }

        [TestMethod]
        public void TryDequeueOnEmptyQueueReturnsFalse()
        {
            var queue = new StablePriorityQueue<string, int>();
            Assert.IsFalse(queue.TryDequeue(out var item));
            Assert.IsNull(item);
            Assert.AreEqual(0, queue.Count);
        }

        private static string[] DequeueAll(StablePriorityQueue<string, int> queue)
        {
            var items = new List<string>();
            while (queue.TryDequeue(out var item))
            {
                items.Add(item);
            }

            return items.ToArray();
        }
    }
}
