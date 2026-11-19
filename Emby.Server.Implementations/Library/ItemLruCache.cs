using System;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Emby.Server.Implementations.Library
{
    /// <summary>
    /// 条目缓存（S2/scale-design §2 #1）：容量上限 + 近似 LRU 逐出 + 绝对 TTL。
    /// 基于 Microsoft.Extensions.Caching.Memory 的 MemoryCache：SizeLimit 限制条目数，
    /// 每项 Size=1；写入时设置 AbsoluteExpirationRelativeToNow，使他实例的 DB 写
    /// 在 TTL 窗口后对本实例可见（T4.9 语义）。未命中回源由调用方负责。
    /// </summary>
    internal sealed class ItemLruCache
    {
        private readonly MemoryCache _cache;
        private readonly TimeSpan _ttl;
        private readonly int _capacity;
        private readonly double _compactionFraction;

        /// <param name="capacity">容量上限（默认 100k，可用 OPENMEDIA_ITEM_CACHE_SIZE 覆盖）</param>
        /// <param name="ttl">TTL——到期后按未命中回源（可用 OPENMEDIA_ITEM_CACHE_TTL_SECONDS 覆盖）</param>
        internal ItemLruCache(int capacity, TimeSpan ttl)
        {
            _ttl = ttl;
            _capacity = capacity;
            // MemoryCache 满容量时原生行为是拒绝新条目并在后台压实；这里显式压实，
            // 保证新条目必然入缓存。默认容量下一次压实腾出约 5% 的槽位，
            // 小容量时至少腾出一个槽位（贴近原 LRU 一次逐出一条的行为）。
            _compactionFraction = Math.Max(0.05, 1.0 / capacity);
            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = capacity,
                CompactionPercentage = _compactionFraction
            });
        }

        public ItemLruCache()
            : this(ReadCapacity(), ReadTtl())
        {
        }

        private static int ReadCapacity()
        {
            var envSize = Environment.GetEnvironmentVariable("OPENMEDIA_ITEM_CACHE_SIZE");
            if (!string.IsNullOrEmpty(envSize) && int.TryParse(envSize, out var parsed) && parsed > 0)
            {
                return parsed;
            }

            return 100000;
        }

        private static TimeSpan ReadTtl()
        {
            var envTtl = Environment.GetEnvironmentVariable("OPENMEDIA_ITEM_CACHE_TTL_SECONDS");
            if (!string.IsNullOrEmpty(envTtl) && int.TryParse(envTtl, out var parsed) && parsed > 0)
            {
                return TimeSpan.FromSeconds(parsed);
            }

            return TimeSpan.FromSeconds(1800);
        }

        public bool TryGet(Guid id, out BaseItem item)
        {
            return _cache.TryGetValue(id, out item);
        }

        public void Set(BaseItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (_cache.Count >= _capacity && !_cache.TryGetValue(item.Id, out _))
            {
                _cache.Compact(_compactionFraction);
            }

            using (var entry = _cache.CreateEntry(item.Id))
            {
                entry.Value = item;
                entry.Size = 1;
                entry.AbsoluteExpirationRelativeToNow = _ttl;
            }
        }

        public bool Remove(Guid id)
        {
            if (!_cache.TryGetValue(id, out _))
            {
                return false;
            }

            _cache.Remove(id);
            return true;
        }

        public int Count
        {
            get { return (int)_cache.Count; }
        }
    }
}
