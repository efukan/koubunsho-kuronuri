using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;
using Xunit;
using N = Kuronuri.Core.Pdfium.PdfiumNative;

namespace Kuronuri.Core.Tests
{
    public class PdfiumLayerTests
    {
        private static StandardData D => StandardData.Instance;

        [Fact]
        public void LoadedDllMatchesPinnedHash()
        {
            Pdf.Run(() => { });
            var fi = new FileInfo(PdfiumLibrary.DllPath);
            Assert.Equal(7830016, fi.Length);
            Assert.Equal("ba884cc5c6f2c3cd7c2ecca9dce36c2e341f78eb586abd691a2bbc017aa9871e", Util.Sha(File.ReadAllBytes(fi.FullName)));
        }

        [Fact]
        public void OpensFromMemoryAndSecretsArePresent()
        {
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(D.Td.Bytes);
                Assert.Equal(5, doc.PageCount);
                Assert.Contains(TestPdfFactory.SecretText, Util.WithPage(doc, 0, Util.PageText));
                Assert.True(N.FPDF_GetMetaText(doc.Handle, "Title", null, 0) > 2);
                Assert.NotEqual(IntPtr.Zero, N.FPDFBookmark_GetFirstChild(doc.Handle, IntPtr.Zero));
                Assert.Equal(1, N.FPDFDoc_GetAttachmentCount(doc.Handle));
                Assert.Equal(1, Util.WithPage(doc, 0, p => N.FPDFPage_GetAnnotCount(p)));
            });
        }

        [Fact]
        public void OpensFromReadOnlyStream()
        {
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromStream(new MemoryStream(D.Td.Bytes, false));
                Assert.Equal(5, doc.PageCount);
                Assert.Contains(TestPdfFactory.SecretText, Util.WithPage(doc, 3, Util.PageText));
            });
        }

        [Fact]
        public void BrokenInputThrowsTypedErrorWithoutDetails()
        {
            var e = Assert.Throws<PdfLoadException>(() => Pdf.Run(() => { using var d = PdfDoc.FromBytes(new byte[] { 1, 2, 3, 4 }); }));
            Assert.Equal(PdfErrorKind.Format, e.Kind);
        }

        [Fact]
        public void DisplaySizesAndMarkerPixelsMatchFormula()
        {
            double[][] expected = { new[] { 595.0, 842 }, new[] { 842.0, 595 }, new[] { 1191.0, 842 }, new[] { 782.0, 555 }, new[] { 595.0, 842 } };
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(D.Td.Bytes);
                for (int i = 0; i < doc.PageCount; i++)
                {
                    var g = D.Geoms[i];
                    Assert.InRange(g.Wd, expected[i][0] - 0.01, expected[i][0] + 0.01);
                    Assert.InRange(g.Hd, expected[i][1] - 0.01, expected[i][1] + 0.01);
                    double[] m = CoordMap.DisplayToUserMatrix(g);
                    using var page = doc.LoadPage(i);
                    using var r = Raster.Render(page.Handle, 100);
                    foreach (var mk in D.Td.Markers[i])
                    {
                        CoordMap.UserToDisplay(g, mk.UX, mk.UY, out double xd, out double yd);
                        Assert.True(Util.Near(r, xd, yd, mk.R, mk.G, mk.B, 12, out string got), "page " + (i + 1) + " " + mk.Name + got);
                        N.FPDF_PageToDevice(page.Handle, 0, 0, r.W, r.H, 0, mk.UX, mk.UY, out int dx, out int dy);
                        Assert.True(Math.Abs(dx - xd * r.Sx) <= 1.5 && Math.Abs(dy - yd * r.Sy) <= 1.5);
                        CoordMap.DisplayToUser(m, xd, yd, out double ux, out double uy);
                        Assert.True(Math.Abs(ux - mk.UX) < 1e-6 && Math.Abs(uy - mk.UY) < 1e-6);
                    }
                    foreach (var px in new[] { new[] { 100, 200 }, new[] { r.W - 50, r.H - 80 } })
                    {
                        N.FPDF_DeviceToPage(page.Handle, 0, 0, r.W, r.H, 0, px[0], px[1], out double X, out double Y);
                        CoordMap.DisplayToUser(m, px[0] / r.Sx, px[1] / r.Sy, out double ux, out double uy);
                        Assert.True(Math.Abs(ux - X) <= 0.75 && Math.Abs(uy - Y) <= 0.75);
                    }
                }
            });
        }

        [Fact]
        public void RegionRenderEqualsCropOfFullRender()
        {
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(D.Td.Bytes);
                for (int i = 0; i < doc.PageCount; i++)
                {
                    using var page = doc.LoadPage(i);
                    using var full = Raster.Render(page.Handle, 100);
                    int rx = full.W / 5, ry = full.H / 4, rw = full.W / 2, rh = full.H / 3;
                    using var buf = new NativeBuffer();
                    buf.Ensure((long)rw * rh * 4);
                    PageRenderer.RenderRegion(page.Handle, buf.Ptr, full.W, full.H, rx, ry, rw, rh);
                    long diff = CountDiff(full, buf.Ptr, rx, ry, rw, rh);
                    Assert.True(diff < rw * rh / 1000, "page " + (i + 1) + " differing pixels " + diff);
                }
            });
        }

        private static unsafe long CountDiff(Raster full, IntPtr region, int rx, int ry, int rw, int rh)
        {
            uint* q = (uint*)region;
            long diff = 0;
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                {
                    uint a = full.Pixel(rx + x, ry + y), b = q[(long)y * rw + x];
                    for (int c = 0; c < 24; c += 8) if (Math.Abs((int)((a >> c) & 0xFF) - (int)((b >> c) & 0xFF)) > 8) { diff++; break; }
                }
            return diff;
        }

        [Fact]
        public void PixelSizeReducesHugePages()
        {
            PageRenderer.PixelSize(2384, 3370, 300, 60_000_000, out int w, out int h, out double eff);
            Assert.True((long)w * h <= 60_000_000);
            Assert.InRange(eff, 190, 200);
            PageRenderer.PixelSize(595, 842, 200, 60_000_000, out w, out h, out eff);
            Assert.Equal(1653, w); Assert.Equal(2339, h); Assert.Equal(200, eff, 6);
        }

        [Fact]
        public void WorkerRunsEverythingOnOneThreadInOrder()
        {
            var w = Pdf.Worker;
            var order = new System.Collections.Concurrent.ConcurrentQueue<int>();
            var tasks = Enumerable.Range(0, 20).Select(i => w.Run(() => { order.Enqueue(i); return Thread.CurrentThread.ManagedThreadId; })).ToArray();
            Task.WaitAll(tasks);
            Assert.Single(tasks.Select(t => t.Result).Distinct());
            Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, tasks[0].Result);
            Assert.Equal(Enumerable.Range(0, 20), order);
            Assert.True(w.Invoke(() => w.IsWorkerThread));
            Assert.Equal(ApartmentState.STA, w.Invoke(() => Thread.CurrentThread.GetApartmentState()));
        }

        [Fact]
        public void WorkerSkipsCancelledJobsAndPropagatesExceptions()
        {
            var w = Pdf.Worker;
            using var gate = new ManualResetEventSlim(false);
            var blocker = w.Run(() => gate.Wait(5000));
            using var cts = new CancellationTokenSource();
            bool ran = false;
            var stale = w.Run(() => { ran = true; return 1; }, cts.Token);
            cts.Cancel();
            gate.Set();
            blocker.Wait();
            Assert.ThrowsAny<OperationCanceledException>(() => stale.GetAwaiter().GetResult());
            Assert.False(ran);
            Assert.Throws<InvalidOperationException>(() => w.Invoke<int>(() => throw new InvalidOperationException("x")));
            Assert.Equal(7, w.Invoke(() => w.Invoke(() => 7)));   // re-entrant call runs inline
        }
    }
}
