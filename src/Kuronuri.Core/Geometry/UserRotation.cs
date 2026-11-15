using System;
using System.Collections.Generic;
using System.Linq;
using Kuronuri.Core.Model;

namespace Kuronuri.Core.Geometry
{
    /// <summary>
    /// User rotation (ARCHITECTURE §2.4): base display coordinates (page as the original shows it, size wd x hd) to view
    /// coordinates (after the user's extra quarter turns clockwise). Items always stay in base coordinates.
    /// </summary>
    public static partial class CoordMap
    {
        public static (double W, double H) ViewSize(int e, double wd, double hd) => (e & 1) == 0 ? (wd, hd) : (hd, wd);

        public static Point2 ToView(int e, double wd, double hd, Point2 p)
        {
            switch (e & 3)
            {
                case 0: return p;
                case 1: return new Point2(hd - p.Y, p.X);
                case 2: return new Point2(wd - p.X, hd - p.Y);
                default: return new Point2(p.Y, wd - p.X);
            }
        }

        public static Point2 FromView(int e, double wd, double hd, Point2 v)
        {
            switch (e & 3)
            {
                case 0: return v;
                case 1: return new Point2(v.Y, hd - v.X);
                case 2: return new Point2(wd - v.X, hd - v.Y);
                default: return new Point2(wd - v.Y, v.X);
            }
        }

        /// <summary>Matrix (a b c d e f) of <see cref="ToView"/>: xv = a·x + c·y + e, yv = b·x + d·y + f.</summary>
        public static double[] ToViewMatrix(int e, double wd, double hd)
        {
            switch (e & 3)
            {
                case 0: return new double[] { 1, 0, 0, 1, 0, 0 };
                case 1: return new double[] { 0, 1, -1, 0, hd, 0 };
                case 2: return new double[] { -1, 0, 0, -1, wd, hd };
                default: return new double[] { 0, -1, 1, 0, 0, wd };
            }
        }

        public static Shape ToView(int e, double wd, double hd, Shape s)
        {
            if ((e & 3) == 0) return s;
            if (s is RectShape r)
            {
                var a = ToView(e, wd, hd, new Point2(r.X, r.Y));
                var b = ToView(e, wd, hd, new Point2(r.X + r.W, r.Y + r.H));
                return new RectShape(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            }
            var st = (StrokeShape)s;
            return st with { Points = st.Points.Select(p => ToView(e, wd, hd, p)).ToList() };
        }

        public static RedactionItem ToView(int e, double wd, double hd, RedactionItem item) =>
            (e & 3) == 0 ? item : item with { Shapes = item.Shapes.Select(s => ToView(e, wd, hd, s)).ToList() };

        /// <summary>
        /// Display order with a page layout: view position of the page, then top, then left in view coordinates
        /// (ARCHITECTURE §4.4). With the identity layout this equals <see cref="ShapeMath.InDisplayOrder"/>.
        /// </summary>
        public static List<RedactionItem> InDisplayOrder(IEnumerable<RedactionItem> items, PageLayout layout, IReadOnlyList<PageInfo> basePages)
        {
            var view = new Dictionary<int, int>();
            for (int i = 0; i < layout.Order.Count; i++) view[layout.Order[i]] = i;
            return items
                .Select(i => (Item: i, Box: ShapeMath.Bounds(ToView(layout.RotationOf(i.PageIndex), basePages[i.PageIndex].Width, basePages[i.PageIndex].Height, i))))
                .OrderBy(x => view[x.Item.PageIndex]).ThenBy(x => x.Box.T).ThenBy(x => x.Box.L)
                .Select(x => x.Item).ToList();
        }
    }
}
