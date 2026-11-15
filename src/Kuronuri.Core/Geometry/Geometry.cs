using System;
using System.Collections.Generic;
using System.Linq;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;

namespace Kuronuri.Core.Geometry
{
    /// <summary>Effective page box in PDF user space, rotation (0..3), and displayed size in pt.</summary>
    public readonly struct PageGeometry
    {
        public readonly double X0, Y0, X1, Y1, Wd, Hd;
        public readonly int Rotation;

        public PageGeometry(double x0, double y0, double x1, double y1, int rotation, double wd, double hd)
        { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; Rotation = rotation; Wd = wd; Hd = hd; }

        public static PageGeometry FromPage(IntPtr page)
        {
            if (!PdfiumNative.FPDF_GetPageBoundingBox(page, out var r)) throw new InvalidOperationException("FPDF_GetPageBoundingBox failed");
            return new PageGeometry(r.Left, r.Bottom, r.Right, r.Top, PdfiumNative.FPDFPage_GetRotation(page),
                PdfiumNative.FPDF_GetPageWidthF(page), PdfiumNative.FPDF_GetPageHeightF(page));
        }
    }

    /// <summary>The only place that knows about /Rotate. Display page space: top-left origin, y down, pt.</summary>
    public static class CoordMap
    {
        /// <summary>Matrix (a b c d e f): X = a*xd + c*yd + e, Y = b*xd + d*yd + f.</summary>
        public static double[] DisplayToUserMatrix(PageGeometry g)
        {
            switch (g.Rotation & 3)
            {
                case 0: return new double[] { 1, 0, 0, -1, g.X0, g.Y1 };
                case 1: return new double[] { 0, 1, 1, 0, g.X0, g.Y0 };
                case 2: return new double[] { -1, 0, 0, 1, g.X1, g.Y0 };
                default: return new double[] { 0, -1, -1, 0, g.X1, g.Y1 };
            }
        }

        /// <summary>Matrix that applies <paramref name="first"/> and then <paramref name="then"/>.</summary>
        public static double[] Compose(double[] first, double[] then) => new[]
        {
            first[0] * then[0] + first[1] * then[2],
            first[0] * then[1] + first[1] * then[3],
            first[2] * then[0] + first[3] * then[2],
            first[2] * then[1] + first[3] * then[3],
            first[4] * then[0] + first[5] * then[2] + then[4],
            first[4] * then[1] + first[5] * then[3] + then[5],
        };

        public static void DisplayToUser(double[] m, double xd, double yd, out double x, out double y)
        {
            x = m[0] * xd + m[2] * yd + m[4];
            y = m[1] * xd + m[3] * yd + m[5];
        }

        public static void UserToDisplay(PageGeometry g, double x, double y, out double xd, out double yd)
        {
            switch (g.Rotation & 3)
            {
                case 0: xd = x - g.X0; yd = g.Y1 - y; break;
                case 1: xd = y - g.Y0; yd = x - g.X0; break;
                case 2: xd = g.X1 - x; yd = y - g.Y0; break;
                default: xd = g.Y1 - y; yd = g.X1 - x; break;
            }
        }
    }

    /// <summary>
    /// Burns redactions into a BGRx buffer (stride = width * 4). No anti-aliasing; every edge is rounded outward
    /// and grown by 1 px so the burned area is always a superset of what the screen shows.
    /// </summary>
    public static unsafe class Burner
    {
        public const uint Black = 0xFF000000, White = 0xFFFFFFFF;

        /// <returns>Number of pixels inside the true geometry that were NOT black after the black pass (must be 0).</returns>
        public static long Burn(IntPtr buffer, int w, int h, double sx, double sy, IReadOnlyList<Shape> shapes, RedactionStyle style)
        {
            uint* p = (uint*)buffer;
            foreach (var s in shapes) Fill(p, w, h, sx, sy, s, 1, Black, false);
            long unburned = 0;
            foreach (var s in shapes) unburned += Fill(p, w, h, sx, sy, s, 0, Black, true);
            if (style == RedactionStyle.WhiteWithBorder)
            {
                int border = Math.Max(2, (int)Math.Ceiling(0.75 * Math.Max(sx, sy)));
                foreach (var s in shapes) Fill(p, w, h, sx, sy, s, 1 - border, White, false);
            }
            return unburned;
        }

        // checkOnly = true: counts pixels that differ from color instead of writing.
        private static long Fill(uint* p, int w, int h, double sx, double sy, Shape s, int grow, uint color, bool checkOnly)
        {
            long bad = 0;
            if (s is RectShape r)
            {
                int l = Math.Max(0, (int)Math.Floor(r.X * sx) - grow), t = Math.Max(0, (int)Math.Floor(r.Y * sy) - grow);
                int rr = Math.Min(w, (int)Math.Ceiling((r.X + r.W) * sx) + grow), b = Math.Min(h, (int)Math.Ceiling((r.Y + r.H) * sy) + grow);
                for (int y = t; y < b; y++)
                {
                    uint* row = p + (long)y * w;
                    for (int x = l; x < rr; x++) { if (checkOnly) { if (row[x] != color) bad++; } else row[x] = color; }
                }
                return bad;
            }
            var st = (StrokeShape)s;
            double scale = Math.Max(sx, sy);
            double rad = checkOnly ? st.WidthPt / 2 * scale : Math.Ceiling(st.WidthPt / 2 * scale) + grow;
            if (rad <= 0) return 0;
            double r2 = rad * rad;
            int n = st.Points.Count;
            for (int i = 0; i < Math.Max(1, n - 1); i++)
            {
                var a = st.Points[i]; var c = st.Points[Math.Min(i + 1, n - 1)];
                double ax = a.X * sx, ay = a.Y * sy, bx = c.X * sx, by = c.Y * sy;
                int l = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - rad)), t = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - rad));
                int rr = Math.Min(w, (int)Math.Ceiling(Math.Max(ax, bx) + rad) + 1), b = Math.Min(h, (int)Math.Ceiling(Math.Max(ay, by) + rad) + 1);
                double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                for (int y = t; y < b; y++)
                {
                    uint* row = p + (long)y * w;
                    double py = y + 0.5;
                    for (int x = l; x < rr; x++)
                    {
                        double px = x + 0.5;
                        double u = len2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / len2 : 0;
                        if (u < 0) u = 0; else if (u > 1) u = 1;
                        double ex = ax + u * dx - px, ey = ay + u * dy - py;
                        if (ex * ex + ey * ey <= r2) { if (checkOnly) { if (row[x] != color) bad++; } else row[x] = color; }
                    }
                }
            }
            return bad;
        }
    }
}
