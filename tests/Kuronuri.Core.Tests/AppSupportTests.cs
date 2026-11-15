using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Printing;
using Kuronuri.Core.Rendering;
using Kuronuri.Core.Settings;
using Xunit;

namespace Kuronuri.Core.Tests
{
    public class BitmapCacheTests
    {
        private static BitmapSource Bmp(int w, int h)
        {
            var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, new byte[w * h * 4], w * 4);
            b.Freeze();
            return b;
        }

        [Fact]
        public void EvictsLeastRecentlyUsedWithinBudget()
        {
            var c = new PageBitmapCache(3 * 800);   // three 10x10 bitmaps (cost = 2 x 400 bytes)
            for (int p = 0; p < 3; p++) c.Put(RenderKey.Full(p, 10, 10), Bmp(10, 10));
            Assert.NotNull(c.Get(RenderKey.Full(0, 10, 10)));   // 0 becomes most recent
            c.Put(RenderKey.Full(3, 10, 10), Bmp(10, 10));
            Assert.Null(c.Get(RenderKey.Full(1, 10, 10)));
            Assert.NotNull(c.Get(RenderKey.Full(0, 10, 10)));
            Assert.Equal(3, c.Count);
            Assert.True(c.UsedBytes <= c.BudgetBytes);
        }

        [Fact]
        public void PinnedPagesAreEvictedLast()
        {
            var c = new PageBitmapCache(2 * 800);
            c.SetPinnedPages(new[] { 0 });
            c.Put(RenderKey.Full(0, 10, 10), Bmp(10, 10));
            c.Put(RenderKey.Full(1, 10, 10), Bmp(10, 10));
            c.Put(RenderKey.Full(2, 10, 10), Bmp(10, 10));
            Assert.NotNull(c.Get(RenderKey.Full(0, 10, 10)));
            Assert.Null(c.Get(RenderKey.Full(1, 10, 10)));
            // One huge entry over budget is still kept (the most recent one is never dropped).
            c.Put(RenderKey.Full(5, 100, 100), Bmp(100, 100));
            Assert.NotNull(c.Get(RenderKey.Full(5, 100, 100)));
            Assert.Equal(1, c.Count);
        }

        [Fact]
        public void EntryLimitApplies()
        {
            var c = new PageBitmapCache(long.MaxValue, 200);
            for (int p = 0; p < 250; p++) c.Put(RenderKey.Full(p, 2, 2), Bmp(2, 2));
            Assert.Equal(200, c.Count);
            Assert.Null(c.Get(RenderKey.Full(0, 2, 2)));
            Assert.NotNull(c.Get(RenderKey.Full(249, 2, 2)));
        }

        [Fact]
        public void DefaultBudgetIsClamped()
        {
            long b = PageBitmapCache.DefaultBudget();
            Assert.InRange(b, 96L << 20, 512L << 20);
        }
    }

    public class PageRenderServiceTests
    {
        [Fact]
        public void RendersFullAndRegionAndCaches()
        {
            var d = StandardData.Instance;
            var doc = Pdf.Run(() => PdfDoc.FromBytes(d.Td.Bytes));
            try
            {
                var svc = new PageRenderService(Pdf.Worker, doc, new PageBitmapCache(64L << 20), new PageBitmapCache(long.MaxValue, 200));
                var key = RenderKey.Full(1, 842, 595);
                var a = svc.RenderAsync(key, CancellationToken.None).Result;
                Assert.True(a.IsFrozen);
                Assert.Equal(842, a.PixelWidth);
                Assert.Same(a, svc.RenderAsync(key, CancellationToken.None).Result);
                var region = svc.RenderAsync(new RenderKey(1, 842, 595, 100, 50, 200, 100), CancellationToken.None).Result;
                Assert.Equal(200, region.PixelWidth);
                Assert.Equal(100, region.PixelHeight);
                using var cts = new CancellationTokenSource();
                cts.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => svc.RenderAsync(RenderKey.Full(2, 50, 50), cts.Token).GetAwaiter().GetResult());
                svc.Dispose();
            }
            finally { Pdf.Run(() => doc.Dispose()); }
        }
    }

    public class PrintingTests
    {
        private static void Sta(Action a)
        {
            Exception? err = null;
            var t = new Thread(() => { try { a(); } catch (Exception e) { err = e; } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start(); t.Join();
            if (err != null) throw new Exception("STA thread failed", err);
        }

        private static long ToXps(System.Windows.Documents.DocumentPaginator pag)
        {
            var ms = new MemoryStream();
            var uri = new Uri("pack://kuronuri-test-" + Guid.NewGuid().ToString("N") + ".xps");
            var pkg = System.IO.Packaging.Package.Open(ms, FileMode.Create, FileAccess.ReadWrite);
            System.IO.Packaging.PackageStore.AddPackage(uri, pkg);
            try
            {
                var xps = new System.Windows.Xps.Packaging.XpsDocument(pkg, System.IO.Packaging.CompressionOption.Normal, uri.AbsoluteUri);
                System.Windows.Xps.Packaging.XpsDocument.CreateXpsDocumentWriter(xps).Write(pag);
                xps.Close();
            }
            finally { System.IO.Packaging.PackageStore.RemovePackage(uri); pkg.Close(); }
            return ms.Length;
        }

        [Fact]
        public void BothPaginatorsRenderEveryPageOnDemand()
        {
            var d = StandardData.Instance;
            var doc = Pdf.Run(() => PdfDoc.FromBytes(d.Td.Bytes));
            try
            {
                Sta(() =>
                {
                    var paper = new Size(793.7, 1122.5);
                    var red = new RedactedPaginator(Pdf.Worker, doc, d.Items, RedactionStyle.Black, 150, paper);
                    long len = ToXps(red);
                    red.Release();
                    Assert.True(red.Calls >= 5 && len > 10000, "calls " + red.Calls + " len " + len);
                    var rev = new ReviewPaginator(Pdf.Worker, doc, d.Items, 150, paper, "【庁内確認用・外部交付厳禁】");
                    long len2 = ToXps(rev);
                    rev.Release();
                    Assert.True(rev.Calls >= 5 && len2 > 10000);
                });
            }
            finally { Pdf.Run(() => doc.Dispose()); }
        }

        [Fact]
        public void LayoutNeverEnlargesAndRotatesLandscape()
        {
            var paper = new Size(794, 1123);   // A4 portrait at 96 dpi
            var t = PrintLayout.Place(595, 842, paper, out double pw, out double ph);
            var r = t.TransformBounds(new Rect(0, 0, pw, ph));
            Assert.Equal(793.33, r.Width, 1);   // actual size
            Assert.Equal(794 / 2.0, r.X + r.Width / 2, 1);

            t = PrintLayout.Place(842, 595, paper, out pw, out ph);   // landscape page -> rotated, actual size
            r = t.TransformBounds(new Rect(0, 0, pw, ph));
            Assert.Equal(ph, r.Width, 1);
            Assert.Equal(pw, r.Height, 1);
            Assert.True(r.X >= -0.01 && r.Y >= -0.01);

            t = PrintLayout.Place(1191, 1684, paper, out pw, out ph);   // A2 -> shrunk
            r = t.TransformBounds(new Rect(0, 0, pw, ph));
            Assert.True(r.Width <= paper.Width + 0.01 && r.Height <= paper.Height + 0.01);

            t = PrintLayout.Place(100, 100, paper, out pw, out ph);   // small -> never enlarged
            r = t.TransformBounds(new Rect(0, 0, pw, ph));
            Assert.Equal(pw, r.Width, 3);
        }

        [Fact]
        public void JobNameHasNoFileName() => Assert.Equal("Kuronuri", PrintJob.Name);
    }

    public class SettingsTests
    {
        [Fact]
        public void MissingFileGivesDefaults()
        {
            string dir = TestDir.Make("settings-missing");
            var s = SettingsStore.Load(Path.Combine(dir, "settings.json"));
            Assert.Equal(6, s.Reasons.Count);
            Assert.Equal("1号", s.Reasons[0].Code);
            Assert.Equal(200, s.ExportDpi);
            Assert.Equal(new[] { 2.0, 4, 6, 10 }, s.BrushWidthsMm);
        }

        [Fact]
        public void RoundTripAndNormalize()
        {
            string dir = TestDir.Make("settings-rt");
            string path = Path.Combine(dir, "settings.json");
            var s = new AppSettings { ExportDpi = 300, DefaultStyle = "whiteBorder" };
            s.Reasons.Add(new ReasonPreset("7号", "独自"));
            Assert.True(SettingsStore.TrySave(path, s));
            var back = SettingsStore.Load(path);
            Assert.Equal(300, back.ExportDpi);
            Assert.Equal(RedactionStyle.WhiteWithBorder, back.Style);
            Assert.Equal("独自", back.Reasons.Last().Label);
            Assert.Contains("独自", File.ReadAllText(path, Encoding.UTF8));

            File.WriteAllText(path, "{\"app\":\"kuronuri\",\"exportDpi\":123,\"jpegQuality\":5,\"brushWidthsMm\":[]}");
            var fixedUp = SettingsStore.Load(path);
            Assert.Equal(200, fixedUp.ExportDpi);
            Assert.Equal(85, fixedUp.JpegQuality);
            Assert.Equal(4, fixedUp.BrushWidthsMm.Count);
            Assert.Equal(6, fixedUp.Reasons.Count);   // missing member -> default presets

            File.WriteAllText(path, "not json");
            Assert.Equal(200, SettingsStore.Load(path).ExportDpi);
        }

        [Fact]
        public void WindowLayoutRoundTripsAndInvalidValuesAreDropped()
        {
            string dir = TestDir.Make("settings-window");
            string path = Path.Combine(dir, "settings.json");
            var s = new AppSettings { Window = new WindowLayout { Left = 10, Top = 20, Width = 1200, Height = 800, Maximized = true, ThumbsWidth = 180, ListWidth = 300, ShowThumbs = false, TwoPane = false } };
            Assert.True(SettingsStore.TrySave(path, s));
            var back = SettingsStore.Load(path);
            Assert.NotNull(back.Window);
            Assert.Equal(1200, back.Window!.Width);
            Assert.True(back.Window.Maximized);
            Assert.False(back.Window.ShowThumbs);
            Assert.False(back.Window.TwoPane);
            Assert.True(back.Window.ShowList);

            File.WriteAllText(path, "{\"app\":\"kuronuri\",\"window\":{\"left\":0,\"top\":0,\"width\":50,\"height\":50}}");
            Assert.Null(SettingsStore.Load(path).Window);   // too small -> ignored
            Assert.Null(new AppSettings().Window);           // default: no layout (shipped settings.json has none)
        }

        [Fact]
        public void WindowLayoutVisibilityCheck()
        {
            var w = new WindowLayout { Left = 100, Top = 100, Width = 1000, Height = 700 };
            Assert.True(w.MostlyInside(0, 0, 1920, 1080));
            Assert.False(w.MostlyInside(1920, 0, 1920, 1080));   // on a monitor that is no longer there
            var half = new WindowLayout { Left = 1500, Top = 100, Width = 1000, Height = 700 };
            Assert.False(half.MostlyInside(0, 0, 1920, 1080));
        }

        [Fact]
        public void ShippedDefaultSettingsFileEqualsBuiltInDefaults()
        {
            string path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "docs", "settings.default.json"));
            var shipped = AppSettings.Parse(File.ReadAllBytes(path));
            var d = new AppSettings();
            Assert.Equal(d.Reasons.Select(r => r.ToString()), shipped.Reasons.Select(r => r.ToString()));
            Assert.Equal(d.ExportDpi, shipped.ExportDpi);
            Assert.Equal(d.JpegQuality, shipped.JpegQuality);
            Assert.Equal(d.DefaultStyle, shipped.DefaultStyle);
            Assert.Equal(d.BrushWidthsMm, shipped.BrushWidthsMm);
            Assert.Equal(d.PrintDpi, shipped.PrintDpi);
        }

        [Fact]
        public void UnwritableLocationDoesNotThrow()
        {
            string dir = TestDir.Make("settings-ro");
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{}");
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                Assert.False(SettingsStore.TrySave(path, new AppSettings()));
                Assert.False(SettingsStore.TrySave(Path.Combine(dir, "no", "such", "dir", "settings.json"), new AppSettings()));
                Assert.NotNull(SettingsStore.Load(path));
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        }
    }
}
