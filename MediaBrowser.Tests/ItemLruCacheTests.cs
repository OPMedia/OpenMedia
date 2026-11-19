using System;
using System.Threading;
using Emby.Server.Implementations.Library;
using MediaBrowser.Controller.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MediaBrowser.Tests
{
    [TestClass]
    public class ItemLruCacheTests
    {
        [TestMethod]
        public void SetThenTryGetReturnsStoredItem()
        {
            var cache = new ItemLruCache(4, TimeSpan.FromMinutes(5));
            var id = Guid.NewGuid();
            var item = NewItem(id);

            cache.Set(item);

            Assert.IsTrue(cache.TryGet(id, out var cached));
            Assert.AreSame(item, cached);
            Assert.AreEqual(1, cache.Count);
        }

        [TestMethod]
        public void CapacityOverflowEvictsLeastRecentlyUsedEntry()
        {
            var cache = new ItemLruCache(2, TimeSpan.FromMinutes(5));
            var a = NewItem(Guid.NewGuid());
            var b = NewItem(Guid.NewGuid());
            var c = NewItem(Guid.NewGuid());

            cache.Set(a);
            cache.Set(b);
            Assert.IsTrue(cache.TryGet(a.Id, out _)); // a 变为最近使用

            cache.Set(c);

            Assert.AreEqual(2, cache.Count);
            Assert.IsFalse(cache.TryGet(b.Id, out _));
            Assert.IsTrue(cache.TryGet(a.Id, out _));
            Assert.IsTrue(cache.TryGet(c.Id, out _));
        }

        [TestMethod]
        public void SetExistingIdReplacesItemAndRefreshesRecency()
        {
            var cache = new ItemLruCache(2, TimeSpan.FromMinutes(5));
            var id = Guid.NewGuid();
            var original = NewItem(id);
            var other = NewItem(Guid.NewGuid());
            var replacement = NewItem(id);

            cache.Set(original);
            cache.Set(other);
            cache.Set(replacement);
            cache.Set(NewItem(Guid.NewGuid()));

            Assert.IsTrue(cache.TryGet(id, out var cached));
            Assert.AreSame(replacement, cached);
            Assert.IsFalse(cache.TryGet(other.Id, out _));
        }

        [TestMethod]
        public void RemoveDropsItemAndReportsWhetherItExisted()
        {
            var cache = new ItemLruCache(4, TimeSpan.FromMinutes(5));
            var id = Guid.NewGuid();
            cache.Set(NewItem(id));

            Assert.IsTrue(cache.Remove(id));
            Assert.IsFalse(cache.Remove(id));
            Assert.IsFalse(cache.TryGet(id, out var item));
            Assert.IsNull(item);
        }

        [TestMethod]
        public void ExpiredEntryIsTreatedAsMiss()
        {
            var cache = new ItemLruCache(4, TimeSpan.FromMilliseconds(200));
            var id = Guid.NewGuid();
            cache.Set(NewItem(id));

            Assert.IsTrue(cache.TryGet(id, out _));
            Thread.Sleep(500);

            Assert.IsFalse(cache.TryGet(id, out var item));
            Assert.IsNull(item);
        }

        [TestMethod]
        public void SingleEntryCapacityKeepsMostRecentlySetItem()
        {
            var cache = new ItemLruCache(1, TimeSpan.FromMinutes(5));
            var oldItem = NewItem(Guid.NewGuid());
            var newItem = NewItem(Guid.NewGuid());

            cache.Set(oldItem);
            cache.Set(newItem);

            Assert.IsTrue(cache.TryGet(newItem.Id, out var cached));
            Assert.AreSame(newItem, cached);
            Assert.IsFalse(cache.TryGet(oldItem.Id, out _));
        }

        private static TestItem NewItem(Guid id)
        {
            return new TestItem { Id = id };
        }

        private sealed class TestItem : BaseItem
        {
        }
    }
}
