using System;
using System.Collections.Generic;
using System.Linq;

namespace Kuronuri.Core.Model
{
    /// <summary>
    /// Page order, user rotation and exclusion (ARCHITECTURE §4.4). <see cref="Order"/> lists original page numbers in
    /// view order; <see cref="Rotations"/> (quarter turns clockwise, 0..3) and <see cref="Excluded"/> are indexed by the
    /// original page number. Redaction items keep the original page number and base coordinates.
    /// Immutable: every change returns a new layout. Records compare lists by reference; use <see cref="SameAs"/>.
    /// </summary>
    public sealed record PageLayout(IReadOnlyList<int> Order, IReadOnlyList<int> Rotations, IReadOnlyList<bool> Excluded)
    {
        public static PageLayout Identity(int pageCount) =>
            new PageLayout(Enumerable.Range(0, pageCount).ToArray(), new int[pageCount], new bool[pageCount]);

        public int Count => Order.Count;

        public bool IsIdentity => Order.Select((p, i) => p == i).All(x => x) && Rotations.All(r => r == 0) && Excluded.All(x => !x);

        /// <summary>Original page shown at view position <paramref name="view"/>.</summary>
        public int BaseOf(int view) => Order[view];

        /// <summary>View position of original page <paramref name="basePage"/>.</summary>
        public int ViewOf(int basePage)
        {
            for (int i = 0; i < Order.Count; i++) if (Order[i] == basePage) return i;
            throw new ArgumentOutOfRangeException(nameof(basePage));
        }

        public int RotationOf(int basePage) => Rotations[basePage] & 3;
        public bool IsExcluded(int basePage) => Excluded[basePage];
        public int IncludedCount => Excluded.Count(x => !x);

        public PageLayout Rotate(int basePage, int quarterTurns)
        {
            var r = Rotations.ToArray();
            r[basePage] = ((r[basePage] + quarterTurns) % 4 + 4) % 4;
            return this with { Rotations = r };
        }

        public PageLayout SetExcluded(int basePage, bool excluded)
        {
            var x = Excluded.ToArray();
            x[basePage] = excluded;
            return this with { Excluded = x };
        }

        /// <summary>Moves the page at view position <paramref name="from"/> to view position <paramref name="to"/>.</summary>
        public PageLayout Move(int from, int to)
        {
            var o = Order.ToList();
            int p = o[from];
            o.RemoveAt(from);
            o.Insert(Math.Max(0, Math.Min(o.Count, to)), p);
            return this with { Order = o.ToArray() };
        }

        public bool SameAs(PageLayout other) =>
            Order.SequenceEqual(other.Order) && Rotations.SequenceEqual(other.Rotations) && Excluded.SequenceEqual(other.Excluded);

        /// <summary>Order is a permutation of 0..n-1, rotations are 0..3, and all lists have n entries.</summary>
        public bool IsValidFor(int pageCount) =>
            Order.Count == pageCount && Rotations.Count == pageCount && Excluded.Count == pageCount &&
            Order.OrderBy(p => p).SequenceEqual(Enumerable.Range(0, pageCount)) && Rotations.All(r => r >= 0 && r <= 3);
    }
}
