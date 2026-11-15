using System;
using System.Collections.Generic;
using Kuronuri.Core.Model;

namespace Kuronuri.Core.Editing
{
    /// <summary>
    /// The current set of redaction items (mutable container of immutable records). Identity is <see cref="RedactionItem.Id"/>.
    /// Only commands change it; the UI observes <see cref="Changed"/>.
    /// </summary>
    public sealed class ItemList
    {
        private readonly List<RedactionItem> _items = new List<RedactionItem>();

        public event EventHandler? Changed;

        public IReadOnlyList<RedactionItem> Items => _items;
        public int Count => _items.Count;

        public void Reset(IEnumerable<RedactionItem> items)
        {
            _items.Clear();
            _items.AddRange(items);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public int IndexOf(Guid id)
        {
            for (int i = 0; i < _items.Count; i++) if (_items[i].Id == id) return i;
            return -1;
        }

        public RedactionItem? Find(Guid id) { int i = IndexOf(id); return i < 0 ? null : _items[i]; }

        internal void Insert(int index, RedactionItem item)
        {
            if (IndexOf(item.Id) >= 0) throw new InvalidOperationException("duplicate item id");
            _items.Insert(Math.Min(Math.Max(0, index), _items.Count), item);
        }

        internal int Remove(Guid id)
        {
            int i = IndexOf(id);
            if (i < 0) throw new InvalidOperationException("item not found");
            _items.RemoveAt(i);
            return i;
        }

        internal void Replace(RedactionItem after)
        {
            int i = IndexOf(after.Id);
            if (i < 0) throw new InvalidOperationException("item not found");
            _items[i] = after;
        }

        internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
