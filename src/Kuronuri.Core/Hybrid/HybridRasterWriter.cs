using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>Converts a WPF geometry (text outline) into PDF path operators (m / l / c / h).</summary>
    public static class PdfPathOps
    {
        public static void Append(StringBuilder sb, System.Windows.Media.Geometry geo, Func<Point, Point> map)
        {
            PathGeometry pg = geo.GetFlattenedPathGeometry(0.02, ToleranceType.Absolute);
            Matrix t = pg.Transform == null ? Matrix.Identity : pg.Transform.Value;
            foreach (PathFigure fig in pg.Figures)
            {
                Point cur = fig.StartPoint;
                Op(sb, map(t.Transform(cur)), "m");
                foreach (PathSegment seg in fig.Segments)
                {
                    if (seg is LineSegment ls) { Op(sb, map(t.Transform(ls.Point)), "l"); cur = ls.Point; }
                    else if (seg is PolyLineSegment pls) foreach (Point p in pls.Points) { Op(sb, map(t.Transform(p)), "l"); cur = p; }
                    else if (seg is BezierSegment bs)
                    {
                        Point a = map(t.Transform(bs.Point1)), b = map(t.Transform(bs.Point2)), c = map(t.Transform(bs.Point3));
                        sb.Append(F(a.X)).Append(' ').Append(F(a.Y)).Append(' ').Append(F(b.X)).Append(' ').Append(F(b.Y)).Append(' ')
                          .Append(F(c.X)).Append(' ').Append(F(c.Y)).Append(" c\n");
                        cur = bs.Point3;
                    }
                    else if (seg is PolyBezierSegment pbs)
                    {
                        for (int i = 0; i + 2 < pbs.Points.Count; i += 3)
                        {
                            Point a = map(t.Transform(pbs.Points[i])), b = map(t.Transform(pbs.Points[i + 1])), c = map(t.Transform(pbs.Points[i + 2]));
                            sb.Append(F(a.X)).Append(' ').Append(F(a.Y)).Append(' ').Append(F(b.X)).Append(' ').Append(F(b.Y)).Append(' ')
                              .Append(F(c.X)).Append(' ').Append(F(c.Y)).Append(" c\n");
                            cur = pbs.Points[i + 2];
                        }
                    }
                }
                if (fig.IsClosed) sb.Append("h\n");
            }
        }

        private static string F(double v) => MiniPdfWriter.F(v);
        private static void Op(StringBuilder sb, Point p, string op) => sb.Append(F(p.X)).Append(' ').Append(F(p.Y)).Append(' ').Append(op).Append('\n');
    }

    /// <summary>
    /// Fallback review PDF written with MiniPdfWriter (ARCHITECTURE §9.4): pages are images (display orientation),
    /// overlays / labels / watermark are vector operators, and the original + project JSON are embedded files stored
    /// without compression. The watermark is one shared Form XObject.
    /// </summary>
    public static class HybridRasterWriter
    {
        public const double DefaultDpi = 150;
        public const int DefaultQuality = 75;

        public static void Build(PdfDoc src, byte[] original, byte[] projectJson, IReadOnlyList<RedactionItem> items, Stream output,
            double dpi = DefaultDpi, int quality = DefaultQuality, HybridOptions? opt = null, PageLayout? layout = null)
        {
            opt ??= new HybridOptions();
            string F(double v) => MiniPdfWriter.F(v);
            var plan = OutputPlan.Build(FinalExporter.ReadPageInfos(src), items, layout, includeExcluded: true);
            int n = plan.Pages.Count;
            var w = new MiniPdfWriter(output);
            int catalog = w.Reserve(), pages = w.Reserve();
            var kids = new StringBuilder();
            var ordered = plan.Items.ToList();   // view coordinates, output page index, display order

            // Shared watermark form: drawn once in its own box (y up), placed on every page with a cm matrix.
            int wmForm = 0; double wmW = 0, wmH = 0, wmGeoW = 0;
            if (opt.DrawText)
            {
                var wm = TextOutline.Build(opt.Watermark, 100, true);
                Rect b = wm.Bounds;
                const double pad = 2;
                wmW = b.Width + 2 * pad; wmH = b.Height + 2 * pad; wmGeoW = b.Width;
                var ops = new StringBuilder("/GSW gs 1 0 0 rg\n");
                double ph = wmH;
                PdfPathOps.Append(ops, wm, p => new Point(p.X - b.X + pad, ph - (p.Y - b.Y + pad)));
                ops.Append("f\n");
                wmForm = w.Reserve();
                w.WriteStream(wmForm, "/Type /XObject /Subtype /Form /BBox [0 0 " + F(wmW) + " " + F(wmH) + "] /Resources << /ExtGState << /GSW << /ca 0.1882 /CA 0.1882 >> >> >>",
                    MiniPdfWriter.Latin1.GetBytes(ops.ToString()));
            }

            using (var buf = new NativeBuffer())
            using (var gray = new NativeBuffer())
            using (var jpeg = new MemoryStream())
            {
                for (int i = 0; i < n; i++)
                {
                    var op = plan.Pages[i];
                    IntPtr page = N.FPDF_LoadPage(src.Handle, op.BasePage);
                    if (page == IntPtr.Zero) throw new InvalidOperationException("page load failed: " + (i + 1));
                    try
                    {
                        double wd = op.Width, hd = op.Height;   // view size (user rotation applied)
                        PageRenderer.PixelSize(wd, hd, dpi, 60_000_000, out int wpx, out int hpx, out _, PageRenderer.MaxEncodableSide);
                        buf.Ensure((long)wpx * hpx * 4);
                        PageRenderer.RenderFull(page, buf.Ptr, wpx, hpx, op.Rotation);
                        bool color = JpegCodec.DetectColor(buf.Ptr, (long)wpx * hpx);
                        JpegCodec.Encode(buf.Ptr, gray, wpx, hpx, color, quality, jpeg);
                        int len = (int)jpeg.Length, comps = JpegCodec.Components(jpeg.GetBuffer(), len);

                        var ops = new StringBuilder();
                        ops.Append("q ").Append(F(wd)).Append(" 0 0 ").Append(F(hd)).Append(" 0 0 cm /Im0 Do Q\n");
                        for (int k = 0; k < ordered.Count; k++)
                        {
                            var item = ordered[k];
                            if (item.PageIndex != i) continue;
                            foreach (var s in item.Shapes)
                            {
                                if (s is RectShape r)
                                    ops.Append("/GS1 gs 1 0 0 rg 1 0 0 RG 1 w ").Append(F(r.X)).Append(' ').Append(F(hd - r.Y - r.H)).Append(' ')
                                       .Append(F(r.W)).Append(' ').Append(F(r.H)).Append(" re B\n");
                                else
                                {
                                    var st = (StrokeShape)s;
                                    ops.Append("/GS2 gs 1 0 0 RG 1 J 1 j ").Append(F(st.WidthPt)).Append(" w ");
                                    for (int q = 0; q < st.Points.Count; q++)
                                        ops.Append(F(st.Points[q].X)).Append(' ').Append(F(hd - st.Points[q].Y)).Append(q == 0 ? " m " : " l ");
                                    if (st.Points.Count == 1) ops.Append(F(st.Points[0].X)).Append(' ').Append(F(hd - st.Points[0].Y)).Append(" l ");
                                    ops.Append("S\n");
                                }
                            }
                            if (opt.DrawText)
                            {
                                var bb = ShapeMath.Bounds(item);
                                double top = HybridWriter.LabelTop(bb.T);
                                var label = TextOutline.Build(HybridWriter.LabelText(k + 1, item), 8, true);
                                ops.Append("/GS0 gs 0.7843 0 0 rg\n");
                                PdfPathOps.Append(ops, label, p => new Point(bb.L + p.X, hd - (top + p.Y)));
                                ops.Append("f\n");
                            }
                        }
                        if (op.Excluded && opt.DrawText)
                        {
                            var note = TextOutline.Build(HybridWriter.ExcludedNote, 14, true);
                            double nx = Math.Max(4, (wd - note.Bounds.Width) / 2);
                            ops.Append("/GS0 gs 0.7843 0 0 rg\n");
                            PdfPathOps.Append(ops, note, p => new Point(nx + p.X, hd - (6 + p.Y)));
                            ops.Append("f\n");
                        }
                        string xobjects = "/Im0 " + "{IMG} 0 R";
                        if (wmForm != 0)
                        {
                            // XObject space (y up) -> centred display space -> rotate -30deg -> page user space (y flip).
                            double s = 0.85 * Math.Min(wd, hd) / wmGeoW;
                            double cos = Math.Cos(-Math.PI / 6), sin = Math.Sin(-Math.PI / 6);
                            double[] a = { s, 0, 0, -s, -s * wmW / 2, s * wmH / 2 };
                            double[] b = { cos, sin, -sin, cos, wd / 2, hd / 2 };
                            double[] m = { 1, 0, 0, -1, 0, hd };
                            double[] t = CoordMap.Compose(CoordMap.Compose(a, b), m);
                            ops.Append("q ").Append(string.Join(" ", t.Select(F))).Append(" cm /Wm Do Q\n");
                            xobjects += " /Wm " + wmForm + " 0 R";
                        }

                        int img = w.Reserve(), cont = w.Reserve(), pg = w.Reserve();
                        w.WriteStream(img, "/Type /XObject /Subtype /Image /Width " + wpx + " /Height " + hpx + " /ColorSpace " +
                            (comps == 1 ? "/DeviceGray" : "/DeviceRGB") + " /BitsPerComponent 8 /Filter /DCTDecode", jpeg.GetBuffer(), 0, len);
                        w.WriteStream(cont, "", MiniPdfWriter.Latin1.GetBytes(ops.ToString()));
                        w.WriteObject(pg, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + F(wd) + " " + F(hd) + "] /Resources << /XObject << " +
                            xobjects.Replace("{IMG}", img.ToString(System.Globalization.CultureInfo.InvariantCulture)) +
                            " >> /ExtGState << /GS0 << /ca 1 /CA 1 >> /GS1 << /ca 0.25 /CA 1 >> /GS2 << /CA 0.38 >> >> >> /Contents " + cont + " 0 R >>");
                        kids.Append(kids.Length == 0 ? "" : " ").Append(pg).Append(" 0 R");
                    }
                    finally { N.FPDF_ClosePage(page); }
                    if ((i + 1) % 8 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                }
            }
            // Embedded files. Name-tree keys must be sorted: "kuronuri-project.json" < "original.pdf".
            int efJson = w.Reserve(), fsJson = w.Reserve(), efOrig = w.Reserve(), fsOrig = w.Reserve();
            w.WriteStream(efJson, "/Type /EmbeddedFile /Params << /Size " + projectJson.Length + " >>", projectJson);
            w.WriteObject(fsJson, "<< /Type /Filespec /F (" + HybridWriter.ProjectName + ") /UF (" + HybridWriter.ProjectName + ") /EF << /F " + efJson + " 0 R >> >>");
            w.WriteStream(efOrig, "/Type /EmbeddedFile /Params << /Size " + original.Length + " >>", original);
            w.WriteObject(fsOrig, "<< /Type /Filespec /F (" + HybridWriter.OriginalName + ") /UF (" + HybridWriter.OriginalName + ") /EF << /F " + efOrig + " 0 R >> >>");
            w.WriteObject(pages, "<< /Type /Pages /Count " + n + " /Kids [" + kids + "] >>");
            w.WriteObject(catalog, "<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [(" + HybridWriter.ProjectName + ") " + fsJson +
                " 0 R (" + HybridWriter.OriginalName + ") " + fsOrig + " 0 R] >> >> >>");
            w.Finish(catalog);
        }
    }
}
