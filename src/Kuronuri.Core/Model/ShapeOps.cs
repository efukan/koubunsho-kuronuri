using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Kuronuri.Core.Model
{
    /// <summary>Axis-aligned rectangle in display page space (pt).</summary>
    public readonly record struct BoxD(double L, double T, double R, double B)
    {
        public double W => R - L;
        public double H => B - T;
        public BoxD Inflate(double d) => new BoxD(L - d, T - d, R + d, B + d);
        public bool Intersects(BoxD o) => L <= o.R && o.L <= R && T <= o.B && o.T <= B;
        public bool Contains(Point2 p) => p.X >= L && p.X <= R && p.Y >= T && p.Y <= B;
        public static BoxD Of((double L, double T, double R, double B) b) => new BoxD(b.L, b.T, b.R, b.B);
    }

    /// <summary>Pure geometric operations on shapes. All coordinates are display page coordinates (pt).</summary>
    public static class ShapeOps
    {
        public const double PtPerMm = 72.0 / 25.4;
        public const double MinBrushMm = 0.5;
        public const double MaxBrushMm = 50.0;

        public static double MmToPt(double mm) => mm * PtPerMm;
        public static double PtToMm(double pt) => pt / PtPerMm;

        /// <summary>Clamps a freehand width to 0.5..50 mm and rounds to 0.1 mm.</summary>
        public static double ClampBrushMm(double mm) => Math.Round(Clamp(mm, MinBrushMm, MaxBrushMm) * 10.0) / 10.0;

        /// <summary>
        /// Steps the freehand width up or down on a symmetric grid:
        /// 0.5 mm in [0.5, 5], 1 mm in [5, 10], 2 mm in [10, 30], 5 mm in [30, 50].
        /// </summary>
        public static double StepBrushMm(double currentMm, int dir)
        {
            double cur = ClampBrushMm(currentMm);
            if (dir == 0) return cur;
            const double eps = 1e-6;
            double step;
            if (dir > 0)
            {
                step = cur < 5.0 - eps ? 0.5 : cur < 10.0 - eps ? 1.0 : cur < 30.0 - eps ? 2.0 : 5.0;
                double next = Math.Floor(cur / step + eps) * step + step;
                return ClampBrushMm(next);
            }
            else
            {
                step = cur <= 5.0 + eps ? 0.5 : cur <= 10.0 + eps ? 1.0 : cur <= 30.0 + eps ? 2.0 : 5.0;
                double next = Math.Ceiling(cur / step - eps) * step - step;
                return ClampBrushMm(next);
            }
        }

        /// <summary>Parses a user-typed freehand width in mm (accepts optional "mm" suffix and full-width digits).</summary>
        public static bool TryParseBrushMm(string? text, out double mm)
        {
            mm = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var sb = new StringBuilder(text!.Length);
            foreach (char c in text)
            {
                if (c >= '\uFF10' && c <= '\uFF19') sb.Append((char)('0' + (c - '\uFF10')));
                else if (c == '．' || c == '。') sb.Append('.');
                else if (c == '\u3000') sb.Append(' ');
                else if (c == 'ｍ' || c == 'Ｍ') sb.Append('m');
                else sb.Append(c);
            }
            string s = sb.ToString().Trim();
            if (s.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 2).Trim();
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || double.IsNaN(v) || double.IsInfinity(v))
                return false;
            if (v < MinBrushMm || v > MaxBrushMm) return false;
            mm = ClampBrushMm(v);
            return true;
        }

        public static BoxD Box(Shape s) => BoxD.Of(ShapeMath.Bounds(s));
        public static BoxD Box(RedactionItem item) => BoxD.Of(ShapeMath.Bounds(item));

        public static Shape Translate(Shape s, double dx, double dy)
        {
            if (s is RectShape r) return r with { X = r.X + dx, Y = r.Y + dy };
            var st = (StrokeShape)s;
            return st with { Points = st.Points.Select(p => new Point2(p.X + dx, p.Y + dy)).ToList() };
        }

        public static RedactionItem Translate(RedactionItem item, double dx, double dy) =>
            item with { Shapes = item.Shapes.Select(s => Translate(s, dx, dy)).ToList() };

        /// <summary>Keeps every shape inside the page by limiting (dx, dy). Returns the clamped delta.</summary>
        public static (double Dx, double Dy) ClampDelta(RedactionItem item, double dx, double dy, double pageW, double pageH)
        {
            var b = Box(item);
            if (b.L + dx < 0) dx = -b.L;
            if (b.R + dx > pageW) dx = pageW - b.R;
            if (b.T + dy < 0) dy = -b.T;
            if (b.B + dy > pageH) dy = pageH - b.B;
            return (dx, dy);
        }

        /// <summary>Rectangle from two corner points, clipped to the page.</summary>
        public static RectShape RectFromCorners(Point2 a, Point2 b, double pageW, double pageH)
        {
            double l = Clamp(Math.Min(a.X, b.X), 0, pageW), r = Clamp(Math.Max(a.X, b.X), 0, pageW);
            double t = Clamp(Math.Min(a.Y, b.Y), 0, pageH), bt = Clamp(Math.Max(a.Y, b.Y), 0, pageH);
            return new RectShape(l, t, r - l, bt - t);
        }

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>True when p lies on the shape (within tolerance pt).</summary>
        public static bool Hit(Shape s, Point2 p, double tolerance)
        {
            if (s is RectShape r)
                return p.X >= r.X - tolerance && p.X <= r.X + r.W + tolerance && p.Y >= r.Y - tolerance && p.Y <= r.Y + r.H + tolerance;
            var st = (StrokeShape)s;
            double rad = st.WidthPt / 2 + tolerance, r2 = rad * rad;
            int n = st.Points.Count;
            for (int i = 0; i < Math.Max(1, n - 1); i++)
                if (DistSqToSegment(p, st.Points[i], st.Points[Math.Min(i + 1, n - 1)]) <= r2) return true;
            return false;
        }

        /// <summary>Index of the top-most (last) shape of the item under p, or -1.</summary>
        public static int HitShape(RedactionItem item, Point2 p, double tolerance)
        {
            for (int i = item.Shapes.Count - 1; i >= 0; i--) if (Hit(item.Shapes[i], p, tolerance)) return i;
            return -1;
        }

        public static double DistSqToSegment(Point2 p, Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            double u = len2 > 0 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2 : 0;
            if (u < 0) u = 0; else if (u > 1) u = 1;
            double ex = a.X + u * dx - p.X, ey = a.Y + u * dy - p.Y;
            return ex * ex + ey * ey;
        }

        /// <summary>
        /// Ramer-Douglas-Peucker simplification. The first and last points are always kept.
        /// Callers widen the stroke by <see cref="StrokeWidthPad"/> so that the simplified path never covers less.
        /// </summary>
        public static List<Point2> Simplify(IReadOnlyList<Point2> pts, double tolerance)
        {
            var dedup = new List<Point2>(pts.Count);
            foreach (var p in pts) if (dedup.Count == 0 || dedup[dedup.Count - 1] != p) dedup.Add(p);
            if (dedup.Count <= 2) return dedup;
            var keep = new bool[dedup.Count];
            keep[0] = keep[dedup.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, dedup.Count - 1));
            double tol2 = tolerance * tolerance;
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                double worst = -1; int idx = -1;
                for (int i = a + 1; i < b; i++)
                {
                    double d = DistSqToSegment(dedup[i], dedup[a], dedup[b]);
                    if (d > worst) { worst = d; idx = i; }
                }
                if (idx >= 0 && worst > tol2)
                {
                    keep[idx] = true;
                    stack.Push((a, idx));
                    stack.Push((idx, b));
                }
            }
            var res = new List<Point2>();
            for (int i = 0; i < dedup.Count; i++) if (keep[i]) res.Add(dedup[i]);
            return res;
        }

        public const double SimplifyTolerancePt = 0.25;
        public const double StrokeWidthPad = 0.5;

        /// <summary>Builds the stored stroke from raw mouse points: RDP (0.25pt) and +0.5pt width so coverage never shrinks.</summary>
        public static StrokeShape FinishStroke(IReadOnlyList<Point2> raw, double widthPt)
        {
            if (raw.Count == 0) throw new ArgumentException("empty stroke");
            return new StrokeShape(Simplify(raw, SimplifyTolerancePt), widthPt + StrokeWidthPad);
        }
    }
}
