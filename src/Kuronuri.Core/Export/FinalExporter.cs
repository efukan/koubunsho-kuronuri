using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;

namespace Kuronuri.Core.Export
{
    public sealed class FinalExportOptions
    {
        public double Dpi = 200;
        public int JpegQuality = 85;
        public long MaxPixelsPerPage = 60_000_000;
        public bool TryLossless = true;   // keep the smaller of JPEG and lossless per page
        public int GcEveryPages = 8;      // WIC bitmaps are only released by the GC; without this the working set balloons
    }

    public sealed class FinalPageReport
    {
        public int WidthPx, HeightPx, ImageBytes;
        public double EffectiveDpi;
        public bool Color, Lossless;
    }

    /// <summary>
    /// Final (public) output: every page is rendered, burned, JPEG-encoded and written as an image-only page.
    /// Object layout is fixed: 1 = Catalog, 2 = Pages, then (image, contents, page) per page. Nothing else is written.
    /// Runs on the PDFium worker thread.
    /// </summary>
    public static class FinalExporter
    {
        /// <summary>All pages in their original order and orientation (no page layout).</summary>
        public static List<FinalPageReport> Export(PdfDoc src, IReadOnlyList<RedactionItem> items, RedactionStyle style,
            Stream output, FinalExportOptions opt, Action<int, int>? progress = null, CancellationToken token = default) =>
            Export(src, OutputPlan.Identity(ReadPageInfos(src), items), style, output, opt, progress, token);

        /// <summary>Display sizes of all pages of a document (base coordinates).</summary>
        public static List<PageInfo> ReadPageInfos(PdfDoc doc)
        {
            var list = new List<PageInfo>();
            for (int i = 0; i < doc.PageCount; i++)
                using (var p = doc.LoadPage(i)) list.Add(new PageInfo(p.Width, p.Height, 0));
            return list;
        }

        /// <summary>The pages of <paramref name="plan"/> in its order and rotation; plan items are in view coordinates.</summary>
        public static List<FinalPageReport> Export(PdfDoc src, OutputPlan plan, RedactionStyle style,
            Stream output, FinalExportOptions opt, Action<int, int>? progress = null, CancellationToken token = default)
        {
            int n = plan.Pages.Count;
            if (n == 0) throw new InvalidOperationException("no pages to output");
            var w = new MiniPdfWriter(output);
            int catalog = w.Reserve(), pages = w.Reserve();
            var kids = new StringBuilder();
            var reports = new List<FinalPageReport>();
            var byPage = plan.Items.GroupBy(i => i.PageIndex)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Shape>)g.SelectMany(i => i.Shapes).ToList());
            var lossless = new LosslessCodec();

            using (var buf = new NativeBuffer())
            using (var gray = new NativeBuffer())
            using (var jpeg = new MemoryStream())
            {
                for (int i = 0; i < n; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var op = plan.Pages[i];
                    IntPtr page = PdfiumNative.FPDF_LoadPage(src.Handle, op.BasePage);
                    if (page == IntPtr.Zero) throw new InvalidOperationException("page load failed: " + (op.BasePage + 1));
                    try
                    {
                        double wd = PdfiumNative.FPDF_GetPageWidthF(page), hd = PdfiumNative.FPDF_GetPageHeightF(page);
                        (wd, hd) = CoordMap.ViewSize(op.Rotation, wd, hd);
                        PageRenderer.PixelSize(wd, hd, opt.Dpi, opt.MaxPixelsPerPage, out int wpx, out int hpx, out double eff, PageRenderer.MaxEncodableSide);
                        buf.Ensure((long)wpx * hpx * 4);
                        PageRenderer.RenderFull(page, buf.Ptr, wpx, hpx, op.Rotation);

                        if (byPage.TryGetValue(i, out var shapes))
                        {
                            long unburned = Burner.Burn(buf.Ptr, wpx, hpx, wpx / wd, hpx / hd, shapes, style);
                            if (unburned != 0) throw new InvalidOperationException("burn verification failed on page " + (i + 1));
                        }

                        bool color = JpegCodec.DetectColor(buf.Ptr, (long)wpx * hpx);
                        JpegCodec.Encode(buf.Ptr, gray, wpx, hpx, color, opt.JpegQuality, jpeg);
                        int len = (int)jpeg.Length;
                        if (JpegCodec.Components(jpeg.GetBuffer(), len) != (color ? 3 : 1)) throw new InvalidOperationException("unexpected JPEG component count");
                        byte[]? z = opt.TryLossless ? lossless.TryEncode(buf.Ptr, gray.Ptr, wpx, hpx, color, len) : null;

                        int img = w.Reserve(), cont = w.Reserve(), pg = w.Reserve();
                        string head = "/Type /XObject /Subtype /Image /Width " + wpx + " /Height " + hpx +
                            " /ColorSpace " + (color ? "/DeviceRGB" : "/DeviceGray") + " /BitsPerComponent 8 /Filter ";
                        if (z != null) w.WriteStream(img, head + "/FlateDecode", z);
                        else w.WriteStream(img, head + "/DCTDecode", jpeg.GetBuffer(), 0, len);
                        string ws = MiniPdfWriter.F(wd), hs = MiniPdfWriter.F(hd);
                        w.WriteStream(cont, "", MiniPdfWriter.Latin1.GetBytes("q " + ws + " 0 0 " + hs + " 0 0 cm /Im0 Do Q"));
                        w.WriteObject(pg, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + ws + " " + hs + "] /Resources << /XObject << /Im0 " +
                            img + " 0 R >> >> /Contents " + cont + " 0 R >>");
                        kids.Append(kids.Length == 0 ? "" : " ").Append(pg).Append(" 0 R");
                        reports.Add(new FinalPageReport { WidthPx = wpx, HeightPx = hpx, ImageBytes = z?.Length ?? len, EffectiveDpi = eff, Color = color, Lossless = z != null });
                    }
                    finally { PdfiumNative.FPDF_ClosePage(page); }

                    if (opt.GcEveryPages > 0 && (i + 1) % opt.GcEveryPages == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                    progress?.Invoke(i + 1, n);
                }
            }
            w.WriteObject(pages, "<< /Type /Pages /Count " + n + " /Kids [" + kids + "] >>");
            w.WriteObject(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
            w.Finish(catalog);
            return reports;
        }
    }

    public sealed class FinalOutputResult
    {
        public List<FinalPageReport> Pages = new List<FinalPageReport>();

        /// <summary>0-based indexes of pages whose dpi was lowered automatically (huge pages).</summary>
        public List<int> ReducedPages = new List<int>();

        /// <summary>The same pages as original page numbers (0-based), for messages in view order.</summary>
        public List<int> ReducedBasePages = new List<int>();

        public long FileBytes;
    }

    /// <summary>Thrown when the written output did not pass <see cref="OutputVerifier"/>. The file has been deleted.</summary>
    public sealed class OutputVerificationException : Exception
    {
        public IReadOnlyList<string> Failures { get; }
        public OutputVerificationException(IReadOnlyList<string> failures) : base("output verification failed") { Failures = failures; }
    }

    /// <summary>
    /// File-level flow of the final output (ARCHITECTURE §7.2 / §8): refuse forbidden destinations, write
    /// "&lt;dest&gt;.part" in the destination folder, verify, then rename. On any failure or cancellation the .part is deleted.
    /// Runs on the PDFium worker thread.
    /// </summary>
    public static class FinalOutput
    {
        /// <summary>All pages in their original order and orientation (no page layout).</summary>
        public static FinalOutputResult Write(PdfDoc src, IReadOnlyList<PageInfo> pages, IReadOnlyList<RedactionItem> items, RedactionStyle style,
            string destPath, IEnumerable<string> forbiddenPaths, FinalExportOptions opt, Action<int, int>? progress = null, CancellationToken token = default) =>
            Write(src, OutputPlan.Identity(pages, items), style, destPath, forbiddenPaths, opt, progress, token);

        public static FinalOutputResult Write(PdfDoc src, OutputPlan plan, RedactionStyle style,
            string destPath, IEnumerable<string> forbiddenPaths, FinalExportOptions opt, Action<int, int>? progress = null, CancellationToken token = default)
        {
            SavePathGuard.EnsureAllowed(destPath, forbiddenPaths);
            string part = SavePathGuard.PartPath(destPath);
            var result = new FinalOutputResult();
            bool done = false;
            try
            {
                using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    result.Pages = FinalExporter.Export(src, plan, style, fs, opt, progress, token);
                    fs.Flush(true);
                }
                token.ThrowIfCancellationRequested();
                for (int i = 0; i < result.Pages.Count; i++)
                    if (result.Pages[i].EffectiveDpi < opt.Dpi - 0.01) { result.ReducedPages.Add(i); result.ReducedBasePages.Add(plan.Pages[i].BasePage); }

                var fails = new List<string>();
                using (var fs = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read))
                    fails.AddRange(OutputVerifier.VerifyStructure(fs, plan.Pages.Count));
                if (fails.Count == 0)
                    using (var doc = PdfDoc.FromFile(part))
                        fails.AddRange(OutputVerifier.VerifyWithPdfium(doc, plan.PageInfos, plan.Items, style));
                if (fails.Count > 0) throw new OutputVerificationException(fails);

                SavePathGuard.ReplaceWithPart(part, destPath);
                result.FileBytes = new FileInfo(destPath).Length;
                done = true;
                return result;
            }
            finally
            {
                if (!done) SavePathGuard.TryDelete(part);
            }
        }
    }
}
