using System;
using System.Collections.Generic;
using System.Linq;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;

namespace Kuronuri.Core.Export
{
    /// <summary>One page of an output: which original page, rotated how, at what (view) size.</summary>
    public sealed record OutputPage(int BasePage, int Rotation, double Width, double Height, bool Excluded);

    /// <summary>
    /// The pages of an output in order, with the items mapped to them (ARCHITECTURE §4.4, §7). Items in the plan have
    /// <see cref="RedactionItem.PageIndex"/> = output page index and shapes in that page's view coordinates, so the
    /// burner, the verifier and the overlay writers work exactly as for an unrotated document.
    /// </summary>
    public sealed class OutputPlan
    {
        private OutputPlan(List<OutputPage> pages, List<RedactionItem> items) { Pages = pages; Items = items; }

        public IReadOnlyList<OutputPage> Pages { get; }
        public IReadOnlyList<RedactionItem> Items { get; }

        /// <summary>Output page sizes (view size, rotation 0) for the verifier.</summary>
        public IReadOnlyList<PageInfo> PageInfos => Pages.Select(p => new PageInfo(p.Width, p.Height, 0)).ToList();

        /// <param name="basePages">Display sizes of the original pages (base coordinates).</param>
        /// <param name="includeExcluded">true for the review PDF and review printing; false for the disclosure PDF.</param>
        public static OutputPlan Build(IReadOnlyList<PageInfo> basePages, IReadOnlyList<RedactionItem> items, PageLayout? layout, bool includeExcluded)
        {
            layout ??= PageLayout.Identity(basePages.Count);
            if (!layout.IsValidFor(basePages.Count)) throw new ArgumentException("layout does not match the document");
            var pages = new List<OutputPage>();
            var outIndex = new Dictionary<int, int>();
            foreach (int b in layout.Order)
            {
                if (layout.IsExcluded(b) && !includeExcluded) continue;
                int e = layout.RotationOf(b);
                var (w, h) = CoordMap.ViewSize(e, basePages[b].Width, basePages[b].Height);
                outIndex[b] = pages.Count;
                pages.Add(new OutputPage(b, e, w, h, layout.IsExcluded(b)));
            }
            var mapped = new List<RedactionItem>();
            foreach (var item in CoordMap.InDisplayOrder(items, layout, basePages))
            {
                if (!outIndex.TryGetValue(item.PageIndex, out int o)) continue;
                var p = basePages[item.PageIndex];
                mapped.Add(CoordMap.ToView(layout.RotationOf(item.PageIndex), p.Width, p.Height, item) with { PageIndex = o });
            }
            return new OutputPlan(pages, mapped);
        }

        /// <summary>All pages in their original order and orientation.</summary>
        public static OutputPlan Identity(IReadOnlyList<PageInfo> basePages, IReadOnlyList<RedactionItem> items) =>
            Build(basePages, items, null, includeExcluded: true);
    }
}
