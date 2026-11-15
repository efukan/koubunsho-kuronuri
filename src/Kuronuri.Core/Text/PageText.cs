using System;
using System.Collections.Generic;
using System.Text;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using N = Kuronuri.Core.Pdfium.PdfiumNative;

namespace Kuronuri.Core.Text
{
    /// <summary>
    /// The characters of one page with their loose boxes in display page coordinates (ARCHITECTURE §11.5).
    /// Kept in memory only; never logged or written anywhere.
    /// </summary>
    public sealed class PageText
    {
        private PageText(int pageIndex, double width, double height, string[] chars, BoxD[] boxes)
        {
            PageIndex = pageIndex; Width = width; Height = height; Chars = chars; Boxes = boxes;
        }

        public int PageIndex { get; }
        public double Width { get; }
        public double Height { get; }

        /// <summary>Text of each character (one or two UTF-16 units).</summary>
        public IReadOnlyList<string> Chars { get; }

        /// <summary>Loose box of each character; an empty box (W or H &lt;= 0) for generated spaces / line breaks.</summary>
        public IReadOnlyList<BoxD> Boxes { get; }

        public int Count => Chars.Count;

        public static bool HasBox(BoxD b) => b.W > 0 && b.H > 0;

        /// <summary>Worker thread only.</summary>
        public static PageText Read(PdfDoc doc, int pageIndex)
        {
            using (var page = doc.LoadPage(pageIndex))
            {
                var g = PageGeometry.FromPage(page.Handle);
                IntPtr tp = N.FPDFText_LoadPage(page.Handle);
                if (tp == IntPtr.Zero) return new PageText(pageIndex, g.Wd, g.Hd, new string[0], new BoxD[0]);
                try
                {
                    int n = Math.Max(0, N.FPDFText_CountChars(tp));
                    var chars = new string[n];
                    var boxes = new BoxD[n];
                    for (int i = 0; i < n; i++)
                    {
                        uint code = N.FPDFText_GetUnicode(tp, i);
                        chars[i] = code == 0 ? "" : code <= 0xFFFF || code > 0x10FFFF ? ((char)(code & 0xFFFF)).ToString() : char.ConvertFromUtf32((int)code);
                        boxes[i] = default;
                        if (N.FPDFText_IsGenerated(tp, i) == 1) continue;
                        if (!N.FPDFText_GetLooseCharBox(tp, i, out var r)) continue;
                        // User space -> display space through CoordMap (the only place that knows /Rotate).
                        CoordMap.UserToDisplay(g, r.Left, r.Top, out double ax, out double ay);
                        CoordMap.UserToDisplay(g, r.Right, r.Bottom, out double bx, out double by);
                        var b = new BoxD(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by));
                        if (HasBox(b)) boxes[i] = b;
                    }
                    return new PageText(pageIndex, g.Wd, g.Hd, chars, boxes);
                }
                finally { N.FPDFText_ClosePage(tp); }
            }
        }

        /// <summary>For tests: a page text built from explicit characters and boxes.</summary>
        public static PageText Create(int pageIndex, double width, double height, string[] chars, BoxD[] boxes) =>
            new PageText(pageIndex, width, height, chars, boxes);

        /// <summary>Index of the character under p (box grown by tolerance); the nearest centre wins. -1 when none.</summary>
        public int HitChar(Point2 p, double tolerance)
        {
            int best = -1; double bestD = double.MaxValue;
            for (int i = 0; i < Boxes.Count; i++)
            {
                var b = Boxes[i];
                if (!HasBox(b) || !b.Inflate(tolerance).Contains(p)) continue;
                double cx = (b.L + b.R) / 2 - p.X, cy = (b.T + b.B) / 2 - p.Y, d = cx * cx + cy * cy;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Text of [first, last] with line breaks flattened (for search result previews).</summary>
        public string Slice(int first, int last)
        {
            var sb = new StringBuilder();
            for (int i = Math.Max(0, first); i <= Math.Min(Count - 1, last); i++)
            {
                string c = Chars[i];
                sb.Append(c == "\r" || c == "\n" ? " " : c);
            }
            return sb.ToString();
        }
    }

    /// <summary>Rectangles that cover a run of characters, one per line (or column for vertical writing).</summary>
    public static class TextRects
    {
        public const double PaddingPt = 1;

        public static List<RectShape> ForRange(PageText t, int first, int last, double padding = PaddingPt)
        {
            var groups = new List<BoxD>();
            BoxD? cur = null;
            BoxD prev = default;
            for (int i = Math.Max(0, first); i <= Math.Min(t.Count - 1, last); i++)
            {
                var b = t.Boxes[i];
                if (!PageText.HasBox(b)) continue;
                if (cur != null && SameLine(prev, b))
                    cur = new BoxD(Math.Min(cur.Value.L, b.L), Math.Min(cur.Value.T, b.T), Math.Max(cur.Value.R, b.R), Math.Max(cur.Value.B, b.B));
                else
                {
                    if (cur != null) groups.Add(cur.Value);
                    cur = b;
                }
                prev = b;
            }
            if (cur != null) groups.Add(cur.Value);
            var rects = new List<RectShape>();
            foreach (var g in groups)
            {
                var p = g.Inflate(padding);
                double l = Math.Max(0, p.L), tt = Math.Max(0, p.T), r = Math.Min(t.Width, p.R), b = Math.Min(t.Height, p.B);
                if (r > l && b > tt) rects.Add(new RectShape(l, tt, r - l, b - tt));
            }
            return rects;
        }

        /// <summary>Neighbouring characters on the same horizontal line, or in the same vertical column.</summary>
        public static bool SameLine(BoxD a, BoxD b)
        {
            double vOverlap = Math.Min(a.B, b.B) - Math.Max(a.T, b.T);
            double hOverlap = Math.Min(a.R, b.R) - Math.Max(a.L, b.L);
            double minH = Math.Min(a.H, b.H), minW = Math.Min(a.W, b.W);
            double maxH = Math.Max(a.H, b.H), maxW = Math.Max(a.W, b.W);
            double hGap = Math.Max(0, Math.Max(a.L, b.L) - Math.Min(a.R, b.R));
            double vGap = Math.Max(0, Math.Max(a.T, b.T) - Math.Min(a.B, b.B));
            bool horizontal = vOverlap >= 0.5 * minH && hGap < 2 * maxH;
            bool vertical = hOverlap >= 0.5 * minW && vGap < 2 * maxW;
            return horizontal || vertical;
        }
    }
}
