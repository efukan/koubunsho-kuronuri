using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Hybrid;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;
using Kuronuri.Core.Export;

namespace Kuronuri.Core.Printing
{
    /// <summary>Print job name. Never contains a file name (the spooler keeps a history of job names).</summary>
    public static class PrintJob
    {
        public const string Name = "Kuronuri";
    }

    /// <summary>
    /// Placement on paper (ARCHITECTURE §10): actual size first, centred; shrink only when the page does not fit; never
    /// enlarge; rotate by 90° when the page and the paper differ in orientation. Units are WPF DIPs (1/96 inch).
    /// </summary>
    public static class PrintLayout
    {
        public static Transform Place(double pageWd, double pageHd, Size paper, out double pw, out double ph)
        {
            pw = pageWd * 96 / 72; ph = pageHd * 96 / 72;
            bool rotate = (pw > ph) != (paper.Width > paper.Height) && Math.Abs(pw - ph) > 0.5;
            double cw = rotate ? ph : pw, ch = rotate ? pw : ph;
            double k = Math.Min(1, Math.Min(paper.Width / cw, paper.Height / ch));
            var g = new TransformGroup();
            if (rotate) { g.Children.Add(new RotateTransform(90)); g.Children.Add(new TranslateTransform(ph, 0)); }
            g.Children.Add(new ScaleTransform(k, k));
            g.Children.Add(new TranslateTransform((paper.Width - cw * k) / 2, (paper.Height - ch * k) / 2));
            g.Freeze();
            return g;
        }
    }

    /// <summary>Base: renders one page bitmap on the PDFium worker (optionally burned) and lays it out on paper.</summary>
    public abstract class PagePaginator : DocumentPaginator
    {
        private readonly PdfiumWorker _worker;
        private readonly PdfDoc _doc;
        private readonly int _pageCount;
        private readonly NativeBuffer _buf = new NativeBuffer();   // worker thread only
        private Size _paper;
        protected readonly double Dpi;
        /// <summary>Pages in print order with their user rotation; plan items are in view coordinates (ARCHITECTURE §4.4).</summary>
        protected readonly OutputPlan Plan;
        public int Calls;

        protected PagePaginator(PdfiumWorker worker, PdfDoc doc, OutputPlan plan, double dpi, Size paper)
        {
            _worker = worker; _doc = doc; Dpi = dpi; _paper = paper; Plan = plan;
            _pageCount = plan.Pages.Count;
        }

        /// <summary>All pages in their original order and orientation.</summary>
        protected static OutputPlan IdentityPlan(PdfiumWorker worker, PdfDoc doc, IReadOnlyList<RedactionItem> items) =>
            OutputPlan.Identity(worker.Invoke(() => FinalExporter.ReadPageInfos(doc)), items);

        public override bool IsPageCountValid => true;
        public override int PageCount => _pageCount;
        public override Size PageSize { get => _paper; set => _paper = value; }
        public override IDocumentPaginatorSource? Source => null;

        /// <summary>Worker thread: return false when burning failed.</summary>
        protected virtual bool AfterRender(int pageIndex, IntPtr buffer, int wpx, int hpx, double wd, double hd) => true;

        /// <summary>UI thread: draw on top of the page bitmap, in display page coordinates (pt).</summary>
        protected virtual void Decorate(DrawingContext dc, int pageIndex, double wd, double hd) { }

        public override DocumentPage GetPage(int pageNumber)
        {
            Calls++;
            var (bmp, wd, hd) = _worker.Invoke(() =>
            {
                var op = Plan.Pages[pageNumber];
                using (var page = _doc.LoadPage(op.BasePage))
                {
                    var (w, h) = CoordMap.ViewSize(op.Rotation, page.Width, page.Height);
                    PageRenderer.PixelSize(w, h, Dpi, 60_000_000, out int wpx, out int hpx, out _, PageRenderer.MaxEncodableSide);
                    _buf.Ensure((long)wpx * hpx * 4);
                    PageRenderer.RenderFull(page.Handle, _buf.Ptr, wpx, hpx, op.Rotation);
                    if (!AfterRender(pageNumber, _buf.Ptr, wpx, hpx, w, h)) throw new InvalidOperationException("burn verification failed");
                    var b = BitmapSource.Create(wpx, hpx, 96, 96, PixelFormats.Bgr32, null, _buf.Ptr, checked(wpx * hpx * 4), wpx * 4);   // copies
                    b.Freeze();
                    if ((pageNumber + 1) % 8 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                    return (b, w, h);
                }
            });

            var place = PrintLayout.Place(wd, hd, _paper, out double pw, out double ph);
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.PushTransform(place);
                dc.DrawImage(bmp, new Rect(0, 0, pw, ph));
                dc.PushTransform(new ScaleTransform(96.0 / 72, 96.0 / 72));
                Decorate(dc, pageNumber, wd, hd);
                dc.Pop();
                dc.Pop();
            }
            return new DocumentPage(dv, _paper, new Rect(_paper), new Rect(_paper));
        }

        /// <summary>Releases (zero-clears) the pixel buffer.</summary>
        public void Release() => _worker.Invoke(() => _buf.Dispose());
    }

    /// <summary>
    /// "Print redacted": each page is rendered and burned on demand, so the spooler only ever receives already-redacted
    /// pixels. Usage: new PrintDialog().PrintDocument(paginator, PrintJob.Name).
    /// </summary>
    public sealed class RedactedPaginator : PagePaginator
    {
        private readonly Dictionary<int, IReadOnlyList<Shape>> _byPage;
        private readonly RedactionStyle _style;

        public RedactedPaginator(PdfiumWorker worker, PdfDoc doc, IReadOnlyList<RedactionItem> items, RedactionStyle style, double dpi, Size paper)
            : this(worker, doc, IdentityPlan(worker, doc, items), style, dpi, paper) { }

        /// <param name="plan">Built without excluded pages (they are not printed in this mode).</param>
        public RedactedPaginator(PdfiumWorker worker, PdfDoc doc, OutputPlan plan, RedactionStyle style, double dpi, Size paper)
            : base(worker, doc, plan, dpi, paper)
        {
            _style = style;
            _byPage = plan.Items.GroupBy(i => i.PageIndex).ToDictionary(g => g.Key, g => (IReadOnlyList<Shape>)g.SelectMany(i => i.Shapes).ToList());
        }

        protected override bool AfterRender(int pageIndex, IntPtr buffer, int wpx, int hpx, double wd, double hd) =>
            !_byPage.TryGetValue(pageIndex, out var shapes) || Burner.Burn(buffer, wpx, hpx, wpx / wd, hpx / hd, shapes, _style) == 0;
    }

    /// <summary>
    /// "Print for review": the original bitmap with the translucent red overlays, labels and watermark drawn by WPF.
    /// The spool contains the original content, so the caller must warn before printing.
    /// </summary>
    public sealed class ReviewPaginator : PagePaginator
    {
        private readonly List<RedactionItem> _ordered;
        private readonly string _watermark;
        private static readonly Brush Fill = Frozen(new SolidColorBrush(Color.FromArgb(64, 255, 0, 0)));
        private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(200, 0, 0)));
        private static readonly Brush WatermarkBrush = Frozen(new SolidColorBrush(Color.FromArgb(48, 255, 0, 0)));
        private static readonly Pen Border = Frozen(new Pen(Brushes.Red, 1));

        public ReviewPaginator(PdfiumWorker worker, PdfDoc doc, IReadOnlyList<RedactionItem> items, double dpi, Size paper, string watermark)
            : this(worker, doc, IdentityPlan(worker, doc, items), dpi, paper, watermark) { }

        /// <param name="plan">Built with excluded pages (they are printed with a note).</param>
        public ReviewPaginator(PdfiumWorker worker, PdfDoc doc, OutputPlan plan, double dpi, Size paper, string watermark)
            : base(worker, doc, plan, dpi, paper)
        {
            _ordered = plan.Items.ToList();   // already in display order
            _watermark = watermark;
        }

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        protected override void Decorate(DrawingContext dc, int pageIndex, double wd, double hd)
        {
            for (int k = 0; k < _ordered.Count; k++)
            {
                var item = _ordered[k];
                if (item.PageIndex != pageIndex) continue;
                foreach (var s in item.Shapes)
                {
                    if (s is RectShape r) dc.DrawRectangle(Fill, Border, new Rect(r.X, r.Y, r.W, r.H));
                    else
                    {
                        var st = (StrokeShape)s;
                        var pen = new Pen(new SolidColorBrush(Color.FromArgb(96, 255, 0, 0)), st.WidthPt) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                        pen.Freeze();
                        dc.DrawGeometry(null, pen, StrokeGeometry(st));
                    }
                }
                var b = ShapeMath.Bounds(item);
                var label = TextOutline.Build(HybridWriter.LabelText(k + 1, item), 8, true);
                dc.PushTransform(new TranslateTransform(b.L, HybridWriter.LabelTop(b.T)));
                dc.DrawGeometry(LabelBrush, null, label);
                dc.Pop();
            }
            if (Plan.Pages[pageIndex].Excluded)
            {
                var note = TextOutline.Build(HybridWriter.ExcludedNote, 14, true);
                dc.PushTransform(new TranslateTransform(Math.Max(4, (wd - note.Bounds.Width) / 2), 6));
                dc.DrawGeometry(LabelBrush, null, note);
                dc.Pop();
            }
            if (_watermark.Length > 0)
            {
                var wm = TextOutline.Build(_watermark, 100, true);
                Rect wb = wm.Bounds;
                double scale = 0.85 * Math.Min(wd, hd) / wb.Width;
                var t = new TransformGroup();
                t.Children.Add(new TranslateTransform(-(wb.X + wb.Width / 2), -(wb.Y + wb.Height / 2)));
                t.Children.Add(new ScaleTransform(scale, scale));
                t.Children.Add(new RotateTransform(-30));
                t.Children.Add(new TranslateTransform(wd / 2, hd / 2));
                dc.PushTransform(t);
                dc.DrawGeometry(WatermarkBrush, null, wm);
                dc.Pop();
            }
        }

        public static StreamGeometry StrokeGeometry(StrokeShape st)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(st.Points[0].X, st.Points[0].Y), false, false);
                if (st.Points.Count == 1) c.LineTo(new Point(st.Points[0].X, st.Points[0].Y), true, true);
                for (int i = 1; i < st.Points.Count; i++) c.LineTo(new Point(st.Points[i].X, st.Points[i].Y), true, true);
            }
            g.Freeze();
            return g;
        }
    }
}
