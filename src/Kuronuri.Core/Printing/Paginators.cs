using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuronuri.Core.Export;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Hybrid;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;

namespace Kuronuri.Core.Printing
{
    /// <summary>Print job name. Never contains a file name (the spooler keeps a history of job names).</summary>
    public static class PrintJob
    {
        public const string Name = "Kuronuri";
    }

    /// <summary>Scaling strategy when placing a page on paper (ARCHITECTURE §10).</summary>
    public enum PrintFitMode
    {
        /// <summary>Actual size first; shrink only when the page does not fit; never enlarge (default).</summary>
        ActualOrShrink,
        /// <summary>Scale up or down to fit the paper while keeping the aspect ratio.</summary>
        FitToPaper,
    }

    /// <summary>Result of placing a page on paper (units are WPF DIPs = 1/96 inch).</summary>
    public sealed record PrintPlacement(Transform Transform, double PageWidthDip, double PageHeightDip, double Scale, bool Rotated);

    /// <summary>
    /// Placement on paper (ARCHITECTURE §10): actual size first (or fit to paper), centred; optionally rotate by 90°
    /// when the page and the paper differ in orientation. Units are WPF DIPs (1/96 inch).
    /// </summary>
    public static class PrintLayout
    {
        public static PrintPlacement Compute(double pageWd, double pageHd, Size paper, PrintFitMode fit = PrintFitMode.ActualOrShrink, bool autoRotate = true)
        {
            double pw = pageWd * 96 / 72, ph = pageHd * 96 / 72;
            bool rotate = autoRotate && (pw > ph) != (paper.Width > paper.Height) && Math.Abs(pw - ph) > 0.5;
            double cw = rotate ? ph : pw, ch = rotate ? pw : ph;
            double k = 1;
            if (cw > 0 && ch > 0 && paper.Width > 0 && paper.Height > 0)
            {
                double fitScale = Math.Min(paper.Width / cw, paper.Height / ch);
                k = fit == PrintFitMode.FitToPaper ? fitScale : Math.Min(1, fitScale);
            }
            var g = new TransformGroup();
            if (rotate) { g.Children.Add(new RotateTransform(90)); g.Children.Add(new TranslateTransform(ph, 0)); }
            g.Children.Add(new ScaleTransform(k, k));
            g.Children.Add(new TranslateTransform((paper.Width - cw * k) / 2, (paper.Height - ch * k) / 2));
            g.Freeze();
            return new PrintPlacement(g, pw, ph, k, rotate);
        }

        public static Transform Place(double pageWd, double pageHd, Size paper, out double pw, out double ph)
        {
            var p = Compute(pageWd, pageHd, paper);
            pw = p.PageWidthDip;
            ph = p.PageHeightDip;
            return p.Transform;
        }
    }

    /// <summary>
    /// Parses a 1-based page range string such as "1-3, 5" (also accepts full-width digits and Japanese punctuation).
    /// Results are sorted ascending and deduplicated as 0-based indices.
    /// </summary>
    public static class PrintPageRange
    {
        public static bool TryParse(string? spec, int totalPages, out IReadOnlyList<int> zeroBasedPages, out string? error)
        {
            zeroBasedPages = Array.Empty<int>();
            if (totalPages <= 0)
            {
                error = "印刷できるページがありません。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(spec))
            {
                error = "ページ番号を入力してください（例: 1-3, 5）。";
                return false;
            }

            string normalized = Normalize(spec!);
            var parts = normalized.Split(',');
            var set = new SortedSet<int>();
            foreach (string raw in parts)
            {
                string token = raw.Trim();
                if (token.Length == 0)
                {
                    error = "ページ指定の書式が正しくありません（例: 1-3, 5）。";
                    return false;
                }
                int dash = token.IndexOf('-');
                if (dash >= 0)
                {
                    if (token.IndexOf('-', dash + 1) >= 0)
                    {
                        error = "ページ指定の書式が正しくありません（例: 1-3, 5）。";
                        return false;
                    }
                    string left = token.Substring(0, dash).Trim();
                    string right = token.Substring(dash + 1).Trim();
                    if (!int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out int a) ||
                        !int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out int b))
                    {
                        error = "ページ指定の書式が正しくありません（例: 1-3, 5）。";
                        return false;
                    }
                    if (a < 1 || b < 1 || a > totalPages || b > totalPages)
                    {
                        error = "ページ番号は 1〜" + totalPages + " の範囲で指定してください。";
                        return false;
                    }
                    if (a > b)
                    {
                        error = "ページ範囲の開始が終了より大きくなっています（" + a + "-" + b + "）。";
                        return false;
                    }
                    for (int p = a; p <= b; p++) set.Add(p - 1);
                }
                else
                {
                    if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int p))
                    {
                        error = "ページ指定の書式が正しくありません（例: 1-3, 5）。";
                        return false;
                    }
                    if (p < 1 || p > totalPages)
                    {
                        error = "ページ番号は 1〜" + totalPages + " の範囲で指定してください。";
                        return false;
                    }
                    set.Add(p - 1);
                }
            }

            zeroBasedPages = set.ToList();
            error = null;
            return true;
        }

        private static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c >= '\uFF10' && c <= '\uFF19') sb.Append((char)('0' + (c - '\uFF10')));
                else if (c == '、' || c == '，' || c == ';' || c == '；') sb.Append(',');
                else if (c == '－' || c == 'ー' || c == '―' || c == '‐' || c == '–' || c == '—' || c == '〜' || c == '~' || c == '～') sb.Append('-');
                else if (c == '\u3000') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>Base: renders one page bitmap on the PDFium worker (optionally burned) and lays it out on paper.</summary>
    public abstract class PagePaginator : DocumentPaginator
    {
        private readonly PdfiumWorker _worker;
        private readonly PdfDoc _doc;
        private readonly IReadOnlyList<int>? _pageIndices;
        private readonly int _pageCount;
        private readonly PrintFitMode _fit;
        private readonly bool _autoRotate;
        private readonly NativeBuffer _buf = new NativeBuffer();   // worker thread only
        private Size _paper;
        protected readonly double Dpi;
        /// <summary>Pages in print order with their user rotation; plan items are in view coordinates (ARCHITECTURE §4.4).</summary>
        protected readonly OutputPlan Plan;
        public int Calls;

        protected PagePaginator(
            PdfiumWorker worker,
            PdfDoc doc,
            OutputPlan plan,
            double dpi,
            Size paper,
            IReadOnlyList<int>? pageIndices = null,
            PrintFitMode fit = PrintFitMode.ActualOrShrink,
            bool autoRotate = true)
        {
            _worker = worker; _doc = doc; Dpi = dpi; _paper = paper; Plan = plan;
            _fit = fit; _autoRotate = autoRotate;
            if (pageIndices != null)
            {
                var copy = new List<int>(pageIndices.Count);
                foreach (int idx in pageIndices)
                {
                    if (idx < 0 || idx >= plan.Pages.Count) throw new ArgumentOutOfRangeException(nameof(pageIndices));
                    copy.Add(idx);
                }
                _pageIndices = copy;
                _pageCount = copy.Count;
            }
            else
            {
                _pageCount = plan.Pages.Count;
            }
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
            int planIndex = _pageIndices != null ? _pageIndices[pageNumber] : pageNumber;
            var (bmp, wd, hd) = _worker.Invoke(() =>
            {
                var op = Plan.Pages[planIndex];
                using (var page = _doc.LoadPage(op.BasePage))
                {
                    var (w, h) = CoordMap.ViewSize(op.Rotation, page.Width, page.Height);
                    PageRenderer.PixelSize(w, h, Dpi, 60_000_000, out int wpx, out int hpx, out _, PageRenderer.MaxEncodableSide);
                    _buf.Ensure((long)wpx * hpx * 4);
                    PageRenderer.RenderFull(page.Handle, _buf.Ptr, wpx, hpx, op.Rotation);
                    if (!AfterRender(planIndex, _buf.Ptr, wpx, hpx, w, h)) throw new InvalidOperationException("burn verification failed");
                    var b = BitmapSource.Create(wpx, hpx, 96, 96, PixelFormats.Bgr32, null, _buf.Ptr, checked(wpx * hpx * 4), wpx * 4);   // copies
                    b.Freeze();
                    if ((pageNumber + 1) % 8 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                    return (b, w, h);
                }
            });

            var place = PrintLayout.Compute(wd, hd, _paper, _fit, _autoRotate);
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.PushTransform(place.Transform);
                dc.DrawImage(bmp, new Rect(0, 0, place.PageWidthDip, place.PageHeightDip));
                dc.PushTransform(new ScaleTransform(96.0 / 72, 96.0 / 72));
                Decorate(dc, planIndex, wd, hd);
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
        /// <param name="pageIndices">Optional subset of 0-based indices in <paramref name="plan"/> to print.</param>
        public RedactedPaginator(
            PdfiumWorker worker,
            PdfDoc doc,
            OutputPlan plan,
            RedactionStyle style,
            double dpi,
            Size paper,
            IReadOnlyList<int>? pageIndices = null,
            PrintFitMode fit = PrintFitMode.ActualOrShrink,
            bool autoRotate = true)
            : base(worker, doc, plan, dpi, paper, pageIndices, fit, autoRotate)
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
        private readonly string _watermark;
        private static readonly Brush Fill = Frozen(new SolidColorBrush(Color.FromArgb(64, 255, 0, 0)));
        private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(200, 0, 0)));
        private static readonly Brush WatermarkBrush = Frozen(new SolidColorBrush(Color.FromArgb(48, 255, 0, 0)));
        private static readonly Pen Border = Frozen(new Pen(Brushes.Red, 1));

        public ReviewPaginator(PdfiumWorker worker, PdfDoc doc, IReadOnlyList<RedactionItem> items, double dpi, Size paper, string watermark)
            : this(worker, doc, IdentityPlan(worker, doc, items), dpi, paper, watermark) { }

        /// <param name="plan">Built with excluded pages (they are printed with a note).</param>
        /// <param name="pageIndices">Optional subset of 0-based indices in <paramref name="plan"/> to print. Labels keep full-document numbering.</param>
        public ReviewPaginator(
            PdfiumWorker worker,
            PdfDoc doc,
            OutputPlan plan,
            double dpi,
            Size paper,
            string watermark,
            IReadOnlyList<int>? pageIndices = null,
            PrintFitMode fit = PrintFitMode.ActualOrShrink,
            bool autoRotate = true)
            : base(worker, doc, plan, dpi, paper, pageIndices, fit, autoRotate)
        {
            _watermark = watermark;
        }

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        protected override void Decorate(DrawingContext dc, int pageIndex, double wd, double hd) =>
            DrawOverlay(dc, Plan, pageIndex, wd, hd, _watermark);

        /// <summary>Draws the review overlays (translucent red shapes, full-document item labels, excluded banner, watermark) in display page coordinates (pt).</summary>
        public static void DrawOverlay(DrawingContext dc, OutputPlan plan, int pageIndex, double wd, double hd, string watermark)
        {
            var ordered = plan.Items;
            for (int k = 0; k < ordered.Count; k++)
            {
                var item = ordered[k];
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
            if (plan.Pages[pageIndex].Excluded)
            {
                var note = TextOutline.Build(HybridWriter.ExcludedNote, 14, true);
                dc.PushTransform(new TranslateTransform(Math.Max(4, (wd - note.Bounds.Width) / 2), 6));
                dc.DrawGeometry(LabelBrush, null, note);
                dc.Pop();
            }
            if (watermark.Length > 0)
            {
                var wm = TextOutline.Build(watermark, 100, true);
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

    /// <summary>
    /// On-demand, in-memory page renderer for print preview (ARCHITECTURE §10). Uses the same PDFium rendering and
    /// burn-in path as actual printing, without creating any temporary files. Reuses one zero-cleared NativeBuffer.
    /// </summary>
    public sealed class PrintPreviewRenderer : IDisposable
    {
        private readonly PdfiumWorker _worker;
        private readonly PdfDoc _doc;
        private readonly OutputPlan? _defaultPlan;
        private readonly bool _defaultReview;
        private readonly RedactionStyle _defaultStyle;
        private readonly NativeBuffer _buf = new NativeBuffer();   // worker thread only
        private bool _disposed;

        public PrintPreviewRenderer(PdfiumWorker worker, PdfDoc doc)
        {
            _worker = worker; _doc = doc;
        }

        public PrintPreviewRenderer(PdfiumWorker worker, PdfDoc doc, OutputPlan plan, bool review, RedactionStyle style)
            : this(worker, doc)
        {
            _defaultPlan = plan; _defaultReview = review; _defaultStyle = style;
        }

        public Task<(BitmapSource Bitmap, double WidthPt, double HeightPt)> RenderPageAsync(int planPageIndex, double dpi, CancellationToken ct)
        {
            if (_defaultPlan == null) throw new InvalidOperationException("No default OutputPlan was supplied.");
            return RenderPageAsync(_defaultPlan, planPageIndex, _defaultReview, _defaultStyle, dpi, ct);
        }

        public Task<(BitmapSource Bitmap, double WidthPt, double HeightPt)> RenderPageAsync(
            OutputPlan plan,
            int planPageIndex,
            bool review,
            RedactionStyle style,
            double dpi,
            CancellationToken ct)
        {
            if (planPageIndex < 0 || planPageIndex >= plan.Pages.Count)
                throw new ArgumentOutOfRangeException(nameof(planPageIndex));

            IReadOnlyList<Shape>? shapes = review
                ? null
                : plan.Items.Where(i => i.PageIndex == planPageIndex).SelectMany(i => i.Shapes).ToList();

            return _worker.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (_disposed) throw new ObjectDisposedException(nameof(PrintPreviewRenderer));
                var op = plan.Pages[planPageIndex];
                using (var page = _doc.LoadPage(op.BasePage))
                {
                    var (w, h) = CoordMap.ViewSize(op.Rotation, page.Width, page.Height);
                    PageRenderer.PixelSize(w, h, dpi, 20_000_000, out int wpx, out int hpx, out _, PageRenderer.MaxEncodableSide);
                    _buf.Ensure((long)wpx * hpx * 4);
                    PageRenderer.RenderFull(page.Handle, _buf.Ptr, wpx, hpx, op.Rotation);
                    if (shapes != null && shapes.Count > 0)
                    {
                        if (Burner.Burn(_buf.Ptr, wpx, hpx, wpx / w, hpx / h, shapes, style) != 0)
                            throw new InvalidOperationException("burn verification failed");
                    }
                    var b = BitmapSource.Create(wpx, hpx, 96, 96, PixelFormats.Bgr32, null, _buf.Ptr, checked(wpx * hpx * 4), wpx * 4);
                    b.Freeze();
                    return (b, w, h);
                }
            }, ct);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _worker.Invoke(() => _buf.Dispose());
        }
    }
}
