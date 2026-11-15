using System;
using System.Collections.Generic;
using System.Linq;
using Kuronuri.Core.Model;

namespace Kuronuri.Core.Editing
{
    /// <summary>
    /// Automatic grouping of freehand strokes (ARCHITECTURE §4.2). While the freehand tool is in use there is one
    /// "editing" item; a new stroke whose bounds (grown by 6pt) touch that item on the same page joins it, otherwise a
    /// new item is created and becomes the editing one. Tool change, selection change, page change, Enter/Esc call
    /// <see cref="Close"/>. No time-based rule.
    /// </summary>
    public sealed class FreehandGrouper
    {
        public const double JoinDistancePt = 6;

        public Guid? EditingId { get; private set; }

        public void Close() => EditingId = null;

        /// <summary>Builds the command for a finished stroke (does not execute it).</summary>
        public EditCommand AddStroke(ItemList list, int pageIndex, StrokeShape stroke, Reason? currentReason)
        {
            var editing = EditingId.HasValue ? list.Find(EditingId.Value) : null;
            if (editing != null && editing.PageIndex == pageIndex &&
                ShapeOps.Box(stroke).Inflate(JoinDistancePt).Intersects(ShapeOps.Box(editing)))
            {
                return new ReplaceItemCommand(editing, editing with { Shapes = editing.Shapes.Concat(new Shape[] { stroke }).ToList() });
            }
            var item = new RedactionItem(Guid.NewGuid(), pageIndex, new Shape[] { stroke }, currentReason);
            EditingId = item.Id;
            return new AddItemCommand(item);
        }
    }

    /// <summary>Builders for the commands used by the UI. They never execute anything themselves.</summary>
    public static class EditOps
    {
        public static AddItemCommand AddRect(int pageIndex, RectShape rect, Reason? reason) =>
            new AddItemCommand(new RedactionItem(Guid.NewGuid(), pageIndex, new Shape[] { rect }, reason));

        public static EditCommand? Move(RedactionItem item, double dx, double dy)
        {
            if (dx == 0 && dy == 0) return null;
            return new ReplaceItemCommand(item, ShapeOps.Translate(item, dx, dy));
        }

        public static EditCommand? MoveMany(IEnumerable<RedactionItem> items, double dx, double dy)
        {
            var parts = items.Select(i => Move(i, dx, dy)).Where(c => c != null).Select(c => c!).ToList();
            return parts.Count == 0 ? null : parts.Count == 1 ? parts[0] : new CompositeCommand(parts);
        }

        /// <summary>Resizing is only offered for an item that consists of one rectangle.</summary>
        public static EditCommand? ResizeRect(RedactionItem item, RectShape newRect)
        {
            if (item.Shapes.Count != 1 || !(item.Shapes[0] is RectShape old)) throw new InvalidOperationException("not a single-rectangle item");
            if (old == newRect) return null;
            return new ReplaceItemCommand(item, item with { Shapes = new Shape[] { newRect } });
        }

        public static EditCommand? SetReason(IEnumerable<RedactionItem> items, Reason? reason)
        {
            var parts = items.Where(i => i.Reason != reason).Select(i => (EditCommand)new ReplaceItemCommand(i, i with { Reason = reason })).ToList();
            return parts.Count == 0 ? null : parts.Count == 1 ? parts[0] : new CompositeCommand(parts);
        }

        public static EditCommand? Delete(IEnumerable<RedactionItem> items)
        {
            var parts = items.Select(i => (EditCommand)new RemoveItemCommand(i)).ToList();
            return parts.Count == 0 ? null : parts.Count == 1 ? parts[0] : new CompositeCommand(parts);
        }

        /// <summary>Eraser: removes one shape; an item left without shapes is removed.</summary>
        public static EditCommand EraseShape(RedactionItem item, int shapeIndex)
        {
            if (shapeIndex < 0 || shapeIndex >= item.Shapes.Count) throw new ArgumentOutOfRangeException(nameof(shapeIndex));
            if (item.Shapes.Count == 1) return new RemoveItemCommand(item);
            var shapes = item.Shapes.Where((_, i) => i != shapeIndex).ToList();
            return new ReplaceItemCommand(item, item with { Shapes = shapes });
        }

        public static bool CanMerge(IReadOnlyCollection<RedactionItem> items) =>
            items.Count >= 2 && items.Select(i => i.PageIndex).Distinct().Count() == 1;

        /// <summary>
        /// Several items on one page become one. The display-order first item keeps its id; shapes are concatenated in
        /// display order. The reason is taken from the first item (display order) that has one.
        /// </summary>
        public static EditCommand Merge(IReadOnlyCollection<RedactionItem> items)
        {
            if (!CanMerge(items)) throw new InvalidOperationException("merge needs two or more items on the same page");
            var ordered = ShapeMath.InDisplayOrder(items);
            var first = ordered[0];
            var merged = first with
            {
                Shapes = ordered.SelectMany(i => i.Shapes).ToList(),
                Reason = ordered.Select(i => i.Reason).FirstOrDefault(r => r != null),
            };
            var parts = new List<EditCommand>();
            foreach (var other in ordered.Skip(1)) parts.Add(new RemoveItemCommand(other));
            parts.Add(new ReplaceItemCommand(first, merged));
            return new CompositeCommand(parts);
        }

        public static bool CanSplit(RedactionItem item) => item.Shapes.Count >= 2;

        /// <summary>One item becomes one item per shape. The first shape keeps the id; all parts keep the reason.</summary>
        public static EditCommand Split(ItemList list, RedactionItem item)
        {
            if (!CanSplit(item)) throw new InvalidOperationException("split needs two or more shapes");
            int index = list.IndexOf(item.Id);
            var parts = new List<EditCommand> { new ReplaceItemCommand(item, item with { Shapes = new[] { item.Shapes[0] } }) };
            for (int k = 1; k < item.Shapes.Count; k++)
                parts.Add(new AddItemCommand(new RedactionItem(Guid.NewGuid(), item.PageIndex, new[] { item.Shapes[k] }, item.Reason),
                    index < 0 ? -1 : index + k));
            return new CompositeCommand(parts);
        }
    }
}
