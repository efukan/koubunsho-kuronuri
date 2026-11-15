using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Kuronuri.Core.Export;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;
using N = Kuronuri.Core.Pdfium.PdfiumNative;

namespace Kuronuri.Core.Hybrid
{
    /// <summary>Turns text into outline paths so that no font is ever embedded or referenced in the PDF.</summary>
    public static class TextOutline
    {
        public static System.Windows.Media.Geometry Build(string text, double emSize, bool bold)
        {
            var tf = new Typeface(new FontFamily("Yu Gothic UI, Meiryo, MS Gothic"), FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText(text, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight, tf, emSize, Brushes.Black, 1.0);
            return ft.BuildGeometry(new Point(0, 0));
        }

        /// <param name="map">Maps a geometry point to PDF user space.</param>
        /// <returns>A PDFium path object (fill mode not yet set), or IntPtr.Zero when the geometry is empty.</returns>
        public static IntPtr ToPdfiumPath(System.Windows.Media.Geometry geo, Func<Point, Point> map)
        {
            PathGeometry pg = PathGeometry.CreateFromGeometry(geo);
            if (!Supported(pg)) pg = geo.GetFlattenedPathGeometry(0.02, ToleranceType.Absolute);
            Matrix t = pg.Transform == null ? Matrix.Identity : pg.Transform.Value;
            Func<Point, Point> m = p => map(t.Transform(p));
            IntPtr path = IntPtr.Zero;
            foreach (PathFigure fig in pg.Figures)
            {
                Point cur = fig.StartPoint, q = m(cur);
                if (path == IntPtr.Zero) path = N.FPDFPageObj_CreateNewPath((float)q.X, (float)q.Y);
                else N.FPDFPath_MoveTo(path, (float)q.X, (float)q.Y);
                foreach (PathSegment seg in fig.Segments)
                {
                    if (seg is LineSegment ls) { Line(path, m(ls.Point)); cur = ls.Point; }
                    else if (seg is PolyLineSegment pls) { foreach (Point p in pls.Points) { Line(path, m(p)); cur = p; } }
                    else if (seg is BezierSegment bs) { Cubic(path, m(bs.Point1), m(bs.Point2), m(bs.Point3)); cur = bs.Point3; }
                    else if (seg is PolyBezierSegment pbs)
                    {
                        for (int i = 0; i + 2 < pbs.Points.Count; i += 3) { Cubic(path, m(pbs.Points[i]), m(pbs.Points[i + 1]), m(pbs.Points[i + 2])); cur = pbs.Points[i + 2]; }
                    }
                    else if (seg is QuadraticBezierSegment qs) { Quad(path, m, cur, qs.Point1, qs.Point2); cur = qs.Point2; }
                    else if (seg is PolyQuadraticBezierSegment pqs)
                    {
                        for (int i = 0; i + 1 < pqs.Points.Count; i += 2) { Quad(path, m, cur, pqs.Points[i], pqs.Points[i + 1]); cur = pqs.Points[i + 1]; }
                    }
                }
                if (fig.IsClosed) N.FPDFPath_Close(path);
            }
            return path;
        }

        private static bool Supported(PathGeometry pg) => pg.Figures.All(f => f.Segments.All(s =>
            s is LineSegment || s is PolyLineSegment || s is BezierSegment || s is PolyBezierSegment ||
            s is QuadraticBezierSegment || s is PolyQuadraticBezierSegment));

        private static void Line(IntPtr path, Point p) => N.FPDFPath_LineTo(path, (float)p.X, (float)p.Y);
        private static void Cubic(IntPtr path, Point a, Point b, Point c) =>
            N.FPDFPath_BezierTo(path, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y, (float)c.X, (float)c.Y);
        private static void Quad(IntPtr path, Func<Point, Point> m, Point p0, Point ctrl, Point p2)   // quadratic -> cubic
        {
            var c1 = new Point(p0.X + 2.0 / 3 * (ctrl.X - p0.X), p0.Y + 2.0 / 3 * (ctrl.Y - p0.Y));
            var c2 = new Point(p2.X + 2.0 / 3 * (ctrl.X - p2.X), p2.Y + 2.0 / 3 * (ctrl.Y - p2.Y));
            Cubic(path, m(c1), m(c2), m(p2));
        }
    }

    public sealed class HybridOptions
    {
        public bool DrawText = true;
        public string Watermark = "【庁内確認用・外部交付厳禁】";
        public bool SharedWatermark = true;   // one Form XObject referenced by every page instead of per-page paths
    }

    /// <summary>
    /// Internal-review PDF: the original pages are imported as vectors, overlays are added as page content,
    /// and the untouched original bytes plus the project JSON are stored as attachments.
    /// </summary>
    public static class HybridWriter
    {
        public const string OriginalName = "original.pdf";
        public const string ProjectName = "kuronuri-project.json";

        /// <summary>
        /// Label drawn at the top-left of an item: display number and reason code, e.g. "No.3（1号）" ("No.3" without a
        /// reason). The prefix and brackets keep "1 3号" from being read as "13号" (ARCHITECTURE §9.1).
        /// </summary>
        public static string LabelText(int number, RedactionItem item) =>
            NumberLabel(number) + (item.Reason == null || item.Reason.Code.Length == 0 ? "" : "（" + item.Reason.Code + "）");

        /// <summary>The display number as shown on labels and the screen badge ("No.3").</summary>
        public static string NumberLabel(int number) => "No." + number.ToString(CultureInfo.InvariantCulture);

        /// <summary>Top of the label in display space: above the item, or just inside it when there is no room.</summary>
        public static double LabelTop(double itemTop) => itemTop >= 12 ? itemTop - 11 : itemTop + 1;

        public const string ExcludedNote = "このページは開示用PDFに含めません";

        /// <param name="layout">Page order / rotation / exclusion (ARCHITECTURE §4.4). Pages are imported in that order, the user
        /// rotation is added to /Rotate, and overlays are drawn in view coordinates. Excluded pages stay, with a note.</param>
        public static byte[] Build(byte[] original, byte[] projectJson, IReadOnlyList<RedactionItem> items, HybridOptions opt, PageLayout? layout = null)
        {
            using (var src = PdfDoc.FromBytes(original))
            using (var dst = PdfDoc.CreateNew())
            {
                var basePages = Export.FinalExporter.ReadPageInfos(src);
                var plan = Export.OutputPlan.Build(basePages, items, layout, includeExcluded: true);
                string range = string.Join(",", plan.Pages.Select(p => (p.BasePage + 1).ToString(CultureInfo.InvariantCulture)));
                if (!N.FPDF_ImportPages(dst.Handle, src.Handle, range, 0)) throw new InvalidOperationException("FPDF_ImportPages failed");
                int n = dst.PageCount;
                if (n != src.PageCount) throw new InvalidOperationException("imported page count mismatch");
                var ordered = plan.Items.ToList();   // view coordinates, output page index, display order
                var wm = opt.DrawText ? TextOutline.Build(opt.Watermark, 100, true) : null;
                IntPtr wmX = IntPtr.Zero; double wmW = 0, wmH = 0;
                if (wm != null && opt.SharedWatermark) wmX = CreateWatermarkXObject(dst, wm, out wmW, out wmH);

                try
                {
                for (int i = 0; i < n; i++)
                {
                    IntPtr page = N.FPDF_LoadPage(dst.Handle, i);
                    if (page == IntPtr.Zero) throw new InvalidOperationException("page load failed");
                    try
                    {
                        var op = plan.Pages[i];
                        var g = PageGeometry.FromPage(page);
                        if (op.Rotation != 0)
                        {
                            // The user rotation is added to /Rotate; the geometry then describes the page as it will be displayed.
                            int r = (g.Rotation + op.Rotation) & 3;
                            N.FPDFPage_SetRotation(page, r);
                            g = new PageGeometry(g.X0, g.Y0, g.X1, g.Y1, r, op.Width, op.Height);
                        }
                        double[] m = CoordMap.DisplayToUserMatrix(g);
                        if (op.Excluded && opt.DrawText)
                        {
                            var note = TextOutline.Build(ExcludedNote, 14, true);
                            double nx = Math.Max(4, (op.Width - note.Bounds.Width) / 2), ny = 6;
                            IntPtr np = TextOutline.ToPdfiumPath(note, p => ToUser(m, nx + p.X, ny + p.Y));
                            if (np != IntPtr.Zero)
                            {
                                N.FPDFPath_SetDrawMode(np, N.FPDF_FILLMODE_WINDING, false);
                                N.FPDFPageObj_SetFillColor(np, 200, 0, 0, 255);
                                N.FPDFPage_InsertObject(page, np);
                            }
                        }
                        for (int k = 0; k < ordered.Count; k++)
                        {
                            var item = ordered[k];
                            if (item.PageIndex != i) continue;
                            foreach (var s in item.Shapes) N.FPDFPage_InsertObject(page, ShapePath(m, s));
                            if (opt.DrawText)
                            {
                                var b = ShapeMath.Bounds(item);
                                string label = LabelText(k + 1, item);
                                double top = LabelTop(b.T);
                                IntPtr lp = TextOutline.ToPdfiumPath(TextOutline.Build(label, 8, true), p => ToUser(m, b.L + p.X, top + p.Y));
                                if (lp != IntPtr.Zero)
                                {
                                    N.FPDFPath_SetDrawMode(lp, N.FPDF_FILLMODE_WINDING, false);
                                    N.FPDFPageObj_SetFillColor(lp, 200, 0, 0, 255);
                                    N.FPDFPage_InsertObject(page, lp);
                                }
                            }
                        }
                        if (wm != null && wmX != IntPtr.Zero)
                        {
                            // Shared form: XObject space (y up) -> centred display space -> rotate -30deg -> page user space.
                            double s = 0.85 * Math.Min(g.Wd, g.Hd) / wm.Bounds.Width;
                            double cos = Math.Cos(-Math.PI / 6), sin = Math.Sin(-Math.PI / 6);
                            double[] a = { s, 0, 0, -s, -s * wmW / 2, s * wmH / 2 };
                            double[] b = { cos, sin, -sin, cos, g.Wd / 2, g.Hd / 2 };
                            double[] t = CoordMap.Compose(CoordMap.Compose(a, b), m);
                            IntPtr form = N.FPDF_NewFormObjectFromXObject(wmX);
                            if (form == IntPtr.Zero) throw new InvalidOperationException("FPDF_NewFormObjectFromXObject failed");
                            N.FPDFPageObj_Transform(form, t[0], t[1], t[2], t[3], t[4], t[5]);
                            N.FPDFPage_InsertObject(page, form);
                        }
                        else if (wm != null)
                        {
                            Rect wb = wm.Bounds;
                            double scale = 0.85 * Math.Min(g.Wd, g.Hd) / wb.Width;
                            double cos = Math.Cos(-Math.PI / 6), sin = Math.Sin(-Math.PI / 6);
                            double cx = wb.X + wb.Width / 2, cy = wb.Y + wb.Height / 2;
                            IntPtr wp = TextOutline.ToPdfiumPath(wm, p =>
                            {
                                double qx = (p.X - cx) * scale, qy = (p.Y - cy) * scale;
                                return ToUser(m, g.Wd / 2 + qx * cos - qy * sin, g.Hd / 2 + qx * sin + qy * cos);
                            });
                            if (wp != IntPtr.Zero)
                            {
                                N.FPDFPath_SetDrawMode(wp, N.FPDF_FILLMODE_WINDING, false);
                                N.FPDFPageObj_SetFillColor(wp, 255, 0, 0, 48);
                                N.FPDFPage_InsertObject(page, wp);
                            }
                        }
                        if (!N.FPDFPage_GenerateContent(page)) throw new InvalidOperationException("FPDFPage_GenerateContent failed");
                    }
                    finally { N.FPDF_ClosePage(page); }
                }
                }
                finally { if (wmX != IntPtr.Zero) N.FPDF_CloseXObject(wmX); }

                AddAttachment(dst.Handle, OriginalName, original);
                AddAttachment(dst.Handle, ProjectName, projectJson);
                using (var ms = new MemoryStream()) { dst.SaveTo(ms, N.FPDF_NO_INCREMENTAL); return ms.ToArray(); }
            }
        }

        /// <summary>Draws the watermark once on a temporary one-page document and imports that page into dst as a Form XObject.</summary>
        private static IntPtr CreateWatermarkXObject(PdfDoc dst, System.Windows.Media.Geometry wm, out double w, out double h)
        {
            Rect b = wm.Bounds;
            const double pad = 2;
            double pw = b.Width + 2 * pad, ph = b.Height + 2 * pad;
            w = pw; h = ph;
            using (var tmp = PdfDoc.CreateNew())
            {
                IntPtr page = N.FPDFPage_New(tmp.Handle, 0, pw, ph);
                if (page == IntPtr.Zero) throw new InvalidOperationException("FPDFPage_New failed");
                try
                {
                    IntPtr path = TextOutline.ToPdfiumPath(wm, p => new Point(p.X - b.X + pad, ph - (p.Y - b.Y + pad)));
                    if (path == IntPtr.Zero) return IntPtr.Zero;
                    N.FPDFPath_SetDrawMode(path, N.FPDF_FILLMODE_WINDING, false);
                    N.FPDFPageObj_SetFillColor(path, 255, 0, 0, 48);
                    N.FPDFPage_InsertObject(page, path);
                    if (!N.FPDFPage_GenerateContent(page)) throw new InvalidOperationException("FPDFPage_GenerateContent failed");
                }
                finally { N.FPDF_ClosePage(page); }
                IntPtr x = N.FPDF_NewXObjectFromPage(dst.Handle, tmp.Handle, 0);
                if (x == IntPtr.Zero) throw new InvalidOperationException("FPDF_NewXObjectFromPage failed");
                return x;
            }
        }

        private static Point ToUser(double[] m, double xd, double yd)
        {
            CoordMap.DisplayToUser(m, xd, yd, out double x, out double y);
            return new Point(x, y);
        }

        /// <summary>Translucent red overlay for one shape, in the user space of the imported page.</summary>
        public static IntPtr ShapePath(double[] m, Shape s)
        {
            if (s is RectShape r)
            {
                Point a = ToUser(m, r.X, r.Y), b = ToUser(m, r.X + r.W, r.Y), c = ToUser(m, r.X + r.W, r.Y + r.H), d = ToUser(m, r.X, r.Y + r.H);
                IntPtr path = N.FPDFPageObj_CreateNewPath((float)a.X, (float)a.Y);
                N.FPDFPath_LineTo(path, (float)b.X, (float)b.Y);
                N.FPDFPath_LineTo(path, (float)c.X, (float)c.Y);
                N.FPDFPath_LineTo(path, (float)d.X, (float)d.Y);
                N.FPDFPath_Close(path);
                N.FPDFPath_SetDrawMode(path, N.FPDF_FILLMODE_ALTERNATE, true);
                N.FPDFPageObj_SetFillColor(path, 255, 0, 0, 64);
                N.FPDFPageObj_SetStrokeColor(path, 255, 0, 0, 255);
                N.FPDFPageObj_SetStrokeWidth(path, 1f);
                return path;
            }
            else
            {
                var st = (StrokeShape)s;
                Point p0 = ToUser(m, st.Points[0].X, st.Points[0].Y);
                IntPtr path = N.FPDFPageObj_CreateNewPath((float)p0.X, (float)p0.Y);
                if (st.Points.Count == 1) N.FPDFPath_LineTo(path, (float)p0.X, (float)p0.Y);
                for (int i = 1; i < st.Points.Count; i++) { Point p = ToUser(m, st.Points[i].X, st.Points[i].Y); N.FPDFPath_LineTo(path, (float)p.X, (float)p.Y); }
                N.FPDFPath_SetDrawMode(path, N.FPDF_FILLMODE_NONE, true);
                N.FPDFPageObj_SetStrokeColor(path, 255, 0, 0, 96);
                N.FPDFPageObj_SetStrokeWidth(path, (float)st.WidthPt);
                N.FPDFPageObj_SetLineCap(path, N.FPDF_LINECAP_ROUND);
                N.FPDFPageObj_SetLineJoin(path, N.FPDF_LINEJOIN_ROUND);
                return path;
            }
        }

        private static void AddAttachment(IntPtr doc, string name, byte[] data)
        {
            IntPtr att = N.FPDFDoc_AddAttachment(doc, name);
            if (att == IntPtr.Zero) throw new InvalidOperationException("FPDFDoc_AddAttachment failed");
            var h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { if (!N.FPDFAttachment_SetFile(att, doc, h.AddrOfPinnedObject(), checked((uint)data.Length))) throw new InvalidOperationException("FPDFAttachment_SetFile failed"); }
            finally { h.Free(); }
        }
    }
}
