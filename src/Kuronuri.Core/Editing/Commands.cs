using System;
using System.Collections.Generic;
using System.Linq;
using Kuronuri.Core.Model;

namespace Kuronuri.Core.Editing
{
    /// <summary>One undoable change. There are only three primitive kinds plus <see cref="CompositeCommand"/> (ARCHITECTURE §5).</summary>
    public abstract class EditCommand
    {
        internal abstract void Apply(ItemList list);
        internal abstract void Revert(ItemList list);

        /// <summary>Items that exist after Apply (selected after Redo).</summary>
        public abstract IEnumerable<RedactionItem> ItemsAfter { get; }

        /// <summary>Items that exist after Revert (selected after Undo).</summary>
        public abstract IEnumerable<RedactionItem> ItemsBefore { get; }
    }

    public sealed class AddItemCommand : EditCommand
    {
        public RedactionItem Item { get; }
        private readonly int _index;

        /// <param name="index">Insert position; -1 appends.</param>
        public AddItemCommand(RedactionItem item, int index = -1) { Item = item; _index = index; }

        internal override void Apply(ItemList list) => list.Insert(_index < 0 ? list.Count : _index, Item);
        internal override void Revert(ItemList list) => list.Remove(Item.Id);
        public override IEnumerable<RedactionItem> ItemsAfter => new[] { Item };
        public override IEnumerable<RedactionItem> ItemsBefore => Enumerable.Empty<RedactionItem>();
    }

    public sealed class RemoveItemCommand : EditCommand
    {
        public RedactionItem Item { get; }
        private int _index = -1;

        public RemoveItemCommand(RedactionItem item) { Item = item; }

        internal override void Apply(ItemList list) => _index = list.Remove(Item.Id);
        internal override void Revert(ItemList list) => list.Insert(_index, Item);
        public override IEnumerable<RedactionItem> ItemsAfter => Enumerable.Empty<RedactionItem>();
        public override IEnumerable<RedactionItem> ItemsBefore => new[] { Item };
    }

    public sealed class ReplaceItemCommand : EditCommand
    {
        public RedactionItem Before { get; }
        public RedactionItem After { get; }

        public ReplaceItemCommand(RedactionItem before, RedactionItem after)
        {
            if (before.Id != after.Id) throw new ArgumentException("replace must keep the item id");
            Before = before; After = after;
        }

        internal override void Apply(ItemList list) => list.Replace(After);
        internal override void Revert(ItemList list) => list.Replace(Before);
        public override IEnumerable<RedactionItem> ItemsAfter => new[] { After };
        public override IEnumerable<RedactionItem> ItemsBefore => new[] { Before };
    }

    public sealed class CompositeCommand : EditCommand
    {
        public IReadOnlyList<EditCommand> Parts { get; }

        public CompositeCommand(IEnumerable<EditCommand> parts) { Parts = parts.ToList(); }

        internal override void Apply(ItemList list)
        {
            int done = 0;
            try { foreach (var p in Parts) { p.Apply(list); done++; } }
            catch
            {
                for (int i = done - 1; i >= 0; i--) Parts[i].Revert(list);
                throw;
            }
        }

        internal override void Revert(ItemList list)
        {
            for (int i = Parts.Count - 1; i >= 0; i--) Parts[i].Revert(list);
        }

        public override IEnumerable<RedactionItem> ItemsAfter => Final(true);
        public override IEnumerable<RedactionItem> ItemsBefore => Final(false);

        // Latest version of each id that survives the composite in the given direction.
        private IEnumerable<RedactionItem> Final(bool forward)
        {
            var map = new Dictionary<Guid, RedactionItem?>();
            var seq = forward ? Parts : Parts.Reverse();
            foreach (var p in seq)
            {
                foreach (var v in forward ? p.ItemsBefore : p.ItemsAfter) map[v.Id] = null;
                foreach (var a in forward ? p.ItemsAfter : p.ItemsBefore) map[a.Id] = a;
            }
            return map.Values.Where(v => v != null).Select(v => v!);
        }
    }
}
