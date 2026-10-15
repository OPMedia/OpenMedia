using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Logging;

namespace Emby.Server.Implementations.Library
{
    /// <summary>
    /// 条目 LRU 缓存（S2/scale-design §2 #1）：容量上限 + LRU 逐出 + 惰性 TTL。
    /// 替换 LibraryManager 的无界 ConcurrentDictionary——1M 条目无上限外推 1-2GB+ 常驻。
    /// 未命中回源由调用方（GetItemById → RetrieveItem）负责，本类只管缓存语义。
    /// 单锁 O(1) 读写——临界区为字典+链表指针操作（~百 ns），10k 用户 RPS 量级无瓶颈
    /// （S7 压测复核；如需再升级分片）。
    /// </summary>
    internal sealed class ItemLruCache
    {
        private sealed class CacheEntry
        {
            public BaseItem Item;
            public DateTime UtcStored;
        }

        private readonly object _sync = new object();
        private readonly Dictionary<Guid, LinkedListNode<CacheEntry>> _map;
        private readonly LinkedList<CacheEntry> _lru = new LinkedList<CacheEntry>(); // First = 最近使用
        private readonly int _capacity;
        private readonly TimeSpan _ttl;
        private readonly ILogger _logger;

        // 命中率报告（S2 验收）：postscan 时点由 LibraryManager 取走打日志
        private long _hits;
        private long _misses;
        private long _capacityEvictions;
        private long _ttlExpiries;

        internal sealed class CacheStats
        {
            public int Count;
            public long Hits;
            public long Misses;
            public long CapacityEvictions;
            public long TtlExpiries;
            public double HitRatePercent;
        }

        /// <param name="capacity">容量上限（默认 100k，可用 OPENMEDIA_ITEM_CACHE_SIZE 覆盖）</param>
        /// <param name="ttlSeconds">惰性 TTL 秒数——命中时检查年龄，过期按未命中回源（跨实例写可见性，T4.9）</param>
        public ItemLruCache(ILogger logger)
        {
            _logger = logger;
            var capacity = 100000;
            var envSize = Environment.GetEnvironmentVariable("OPENMEDIA_ITEM_CACHE_SIZE");
            if (!string.IsNullOrEmpty(envSize))
            {
                int parsed;
                if (int.TryParse(envSize, out parsed) && parsed > 0)
                {
                    capacity = parsed;
                }
            }
            var ttlSeconds = 1800;
            var envTtl = Environment.GetEnvironmentVariable("OPENMEDIA_ITEM_CACHE_TTL_SECONDS");
            if (!string.IsNullOrEmpty(envTtl))
            {
                int parsedTtl;
                if (int.TryParse(envTtl, out parsedTtl) && parsedTtl > 0)
                {
                    ttlSeconds = parsedTtl;
                }
            }
            _capacity = capacity;
            _ttl = TimeSpan.FromSeconds(ttlSeconds);
            _map = new Dictionary<Guid, LinkedListNode<CacheEntry>>(capacity / 4);
        }

        public bool TryGet(Guid id, out BaseItem item)
        {
            lock (_sync)
            {
                LinkedListNode<CacheEntry> node;
                if (_map.TryGetValue(id, out node))
                {
                    // 惰性 TTL：过期条目按未命中处理（回源后 Set 覆盖），
                    // 让他实例的 DB 写在 TTL 窗口后对本实例可见（T4.9 语义）
                    if (DateTime.UtcNow - node.Value.UtcStored > _ttl)
                    {
                        _lru.Remove(node);
                        _map.Remove(id);
                        _ttlExpiries++;
                        _misses++;
                        item = null;
                        return false;
                    }

                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    _hits++;
                    item = node.Value.Item;
                    return true;
                }

                _misses++;
                item = null;
                return false;
            }
        }

        public void Set(BaseItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            lock (_sync)
            {
                LinkedListNode<CacheEntry> node;
                if (_map.TryGetValue(item.Id, out node))
                {
                    node.Value.Item = item;
                    node.Value.UtcStored = DateTime.UtcNow;
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    return;
                }

                node = new LinkedListNode<CacheEntry>(new CacheEntry
                {
                    Item = item,
                    UtcStored = DateTime.UtcNow
                });
                _map[item.Id] = node;
                _lru.AddFirst(node);

                while (_map.Count > _capacity)
                {
                    var last = _lru.Last;
                    _lru.RemoveLast();
                    _map.Remove(last.Value.Item.Id);
                    _capacityEvictions++;
                }
            }
        }

        public bool Remove(Guid id)
        {
            lock (_sync)
            {
                LinkedListNode<CacheEntry> node;
                if (!_map.TryGetValue(id, out node))
                {
                    return false;
                }
                _lru.Remove(node);
                _map.Remove(id);
                return true;
            }
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _map.Count;
                }
            }
        }

        public CacheStats GetStats()
        {
            lock (_sync)
            {
                var total = _hits + _misses;
                return new CacheStats
                {
                    Count = _map.Count,
                    Hits = _hits,
                    Misses = _misses,
                    CapacityEvictions = _capacityEvictions,
                    TtlExpiries = _ttlExpiries,
                    HitRatePercent = total == 0 ? 0 : 100.0 * _hits / total
                };
            }
        }
    }
}
