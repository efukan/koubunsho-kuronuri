using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Kuronuri.Core.Rendering
{
    /// <summary>Identifies one rendered bitmap: page, full page pixel size, and the rendered region within it.</summary>
    public readonly record struct RenderKey(int Page, int FullW, int FullH, int X, int Y, int W, int H)
    {
        public static RenderKey Full(int page, int w, int h) => new RenderKey(page, w, h, 0, 0, w, h);
        public bool IsFull => X == 0 && Y == 0 && W == FullW && H == FullH;
        public long Bytes => (long)W * H * 4;
    }

    /// <summary>
    /// LRU cache of frozen page bitmaps with a byte budget and an optional entry limit (ARCHITECTURE §6).
    /// Entries of "pinned" pages (current page and its neighbours) are evicted last. Thread-safe.
    /// </summary>
    public sealed class PageBitmapCache
    {
        private readonly object _gate = new object();
        private readonly LinkedList<(RenderKey Key, BitmapSource Bmp)> _lru = new LinkedList<(RenderKey, BitmapSource)>();   // first = most recent
        private readonly Dictionary<RenderKey, LinkedListNode<(RenderKey Key, BitmapSource Bmp)>> _map = new Dictionary<RenderKey, LinkedListNode<(RenderKey, BitmapSource)>>();
        private HashSet<int> _pinned = new HashSet<int>();

        public PageBitmapCache(long budgetBytes, int maxEntries = int.MaxValue) { BudgetBytes = budgetBytes; MaxEntries = maxEntries; }

        public long BudgetBytes { get; }
        public int MaxEntries { get; }
        public long UsedBytes { get; private set; }
        private long _evicted;

        /// <summary>
        /// True (and resets the counter) when at least <paramref name="threshold"/> bytes were evicted since the last call.
        /// Evicted bitmaps are only released by the GC, so the caller then collects (ARCHITECTURE §6).
        /// </summary>
        public bool TakeEvicted(long threshold)
        {
            lock (_gate)
            {
                if (_evicted < threshold) return false;
                _evicted = 0;
                return true;
            }
        }
        public int Count { get { lock (_gate) return _map.Count; } }

        /// <summary>"25% of the free physical memory, at most 512MB, at least 96MB".</summary>
        public static long DefaultBudget()
        {
            long free = MemoryInfo.AvailablePhysicalBytes();
            long b = free / 4;
            const long Min = 96L << 20, Max = 512L << 20;
            return b < Min ? Min : b > Max ? Max : b;
        }

        public void SetPinnedPages(IEnumerable<int> pages)
        {
            lock (_gate) { _pinned = new HashSet<int>(pages); Trim(); }
        }

        public BitmapSource? Get(RenderKey key)
        {
            lock (_gate)
            {
                if (!_map.TryGetValue(key, out var node)) return null;
                _lru.Remove(node); _lru.AddFirst(node);
                return node.Value.Bmp;
            }
        }

        /// <summary>Returns any cached bitmap of the page (used as a blurry placeholder while the exact one renders).</summary>
        public BitmapSource? GetAnyFull(int page)
        {
            lock (_gate)
            {
                foreach (var e in _lru) if (e.Key.Page == page && e.Key.IsFull) return e.Bmp;
                return null;
            }
        }

        public void Put(RenderKey key, BitmapSource bmp)
        {
            if (!bmp.IsFrozen) throw new ArgumentException("bitmap must be frozen");
            lock (_gate)
            {
                if (_map.TryGetValue(key, out var old)) { _lru.Remove(old); _map.Remove(key); UsedBytes -= Cost(key); }
                var node = _lru.AddFirst((key, bmp));
                _map[key] = node;
                UsedBytes += Cost(key);
                Trim();
            }
        }

        public void Clear()
        {
            lock (_gate) { _lru.Clear(); _map.Clear(); UsedBytes = 0; }
        }

        private void Trim()
        {
            // 1) least recently used, not pinned
            var node = _lru.Last;
            while (Over() && node != null && node != _lru.First)
            {
                var prev = node.Previous;
                if (!_pinned.Contains(node.Value.Key.Page)) Remove(node);
                node = prev;
            }
            // 2) still over: pinned ones too, but always keep the most recent entry
            while (Over() && _lru.Count > 1) Remove(_lru.Last!);
        }

        private bool Over() => UsedBytes > BudgetBytes || _map.Count > MaxEntries;

        /// <summary>
        /// Memory charged for one entry: the WIC bitmap plus the copy WPF keeps for drawing it. Measured on the 300-page
        /// test PDF: the working set grows by about twice the pixel bytes that are cached.
        /// </summary>
        public static long Cost(RenderKey key) => key.Bytes * 2;

        private void Remove(LinkedListNode<(RenderKey Key, BitmapSource Bmp)> node)
        {
            _lru.Remove(node);
            _map.Remove(node.Value.Key);
            UsedBytes -= Cost(node.Value.Key);
            _evicted += Cost(node.Value.Key);
        }
    }

    public static class MemoryInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        public static long AvailablePhysicalBytes()
        {
            var m = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref m) ? (long)Math.Min(m.AvailPhys, long.MaxValue) : 1L << 30;
        }
    }
}
