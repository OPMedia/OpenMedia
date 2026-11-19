using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MediaBrowser.Providers.Manager
{
    /// <summary>
    /// 基于 BCL <see cref="PriorityQueue{TElement, TPriority}"/> 的稳定优先队列。
    /// 复合优先级中的自增序号复现了原 BlueRaja 队列 InsertionIndex 的同优先级 FIFO 语义；
    /// 队列内部加锁，保持原 SimplePriorityQueue 的线程安全行为。
    /// </summary>
    public sealed class StablePriorityQueue<TElement, TPriority> : IEnumerable<TElement>
        where TPriority : IComparable<TPriority>
    {
        private readonly object _sync = new object();
        private readonly PriorityQueue<TElement, (TPriority Priority, long Sequence)> _queue =
            new PriorityQueue<TElement, (TPriority Priority, long Sequence)>();

        private long _nextSequence;

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _queue.Count;
                }
            }
        }

        public void Enqueue(TElement element, TPriority priority)
        {
            lock (_sync)
            {
                _queue.Enqueue(element, (priority, _nextSequence++));
            }
        }

        public bool TryDequeue(out TElement element)
        {
            lock (_sync)
            {
                return _queue.TryDequeue(out element, out _);
            }
        }

        /// <summary>按出队顺序返回快照：优先级升序、同优先级按入队顺序。</summary>
        public IEnumerator<TElement> GetEnumerator()
        {
            lock (_sync)
            {
                return _queue.UnorderedItems
                    .OrderBy(entry => entry.Priority.Priority)
                    .ThenBy(entry => entry.Priority.Sequence)
                    .Select(entry => entry.Element)
                    .ToList()
                    .GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
