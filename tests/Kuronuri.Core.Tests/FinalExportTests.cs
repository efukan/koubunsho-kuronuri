using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Kuronuri.Core.Export;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;
using Xunit;

namespace Kuronuri.Core.Tests
{
    /// <summary>Scratch folder next to the test binaries (the product never writes temp files; the tests do not use %TEMP% either).</summary>
    public static class TestDir
    {
        public static string Make(string name)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "testout", name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public class FinalExportTests
    {
        private static StandardData D => StandardData.Instance;

        public static IEnumerable<object[]> Combos() => new[]
        {
            new object[] { RedactionStyle.Black, false },
            new object[] { RedactionStyle.Black, true },
            new object[] { RedactionStyle.WhiteWithBorder, true },
        };

        private static (byte[] Pdf, List<FinalPageReport> Rep) Export(IReadOnlyList<RedactionItem> items, RedactionStyle style, FinalExportOptions opt, byte[]? src = null) =>
            Pdf.Run(() =>
            {
                var ms = new MemoryStream();
                using var doc = PdfDoc.FromBytes(src ?? D.Td.Bytes);
                var rep = FinalExporter.Export(doc, items, style, ms, opt);
                return (ms.ToArray(), rep);
            });

        [Theory]
        [MemberData(nameof(Combos))]
        public void OutputPassesAllVerificationsAndContainsNoSecret(RedactionStyle style, bool lossless)
        {
            var (fin, rep) = Export(D.Items, style, new FinalExportOptions { TryLossless = lossless });
            Assert.All(rep, r => Assert.Equal(lossless, r.Lossless));
            Assert.All(rep, r => Assert.True(r.Color));
            Assert.Empty(OutputVerifier.VerifyStructure(fin, 5));
            Assert.Empty(Pdf.Run(() => OutputVerifier.VerifyWithPdfium(fin, D.Pages, D.Items, style)));
            Assert.Equal(0, Util.SecretCount(fin));
            Assert.Equal(0, Util.Count(fin, Encoding.ASCII.GetBytes("Helvetica")));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnredactedContentKeepsItsPositionAndRedactionsAreBurned(bool lossless)
        {
            var (fin, _) = Export(D.Items, RedactionStyle.Black, new FinalExportOptions { TryLossless = lossless });
            int tol = lossless ? 2 : 40;
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(fin);
                for (int i = 0; i < 5; i++)
                {
                    using var page = doc.LoadPage(i);
                    using var r = Raster.Render(page.Handle, 100);
                    foreach (var mk in D.Td.Markers[i])
                    {
                        CoordMap.UserToDisplay(D.Geoms[i], mk.UX, mk.UY, out double xd, out double yd);
                        if (mk.Name == "blue") Assert.True(Util.Near(r, xd, yd, 0, 0, 0, 48, out string g1), "p" + (i + 1) + " blue " + g1);
                        else Assert.True(Util.Near(r, xd, yd, mk.R, mk.G, mk.B, tol, out string g2), "p" + (i + 1) + " " + mk.Name + g2);
                    }
                    var rc = (RectShape)D.Items[i * 2].Shapes[0];
                    Assert.True(Util.Near(r, rc.X - 3, rc.Y - 3, 255, 255, 255, 40, out string g3), "p" + (i + 1) + " outside " + g3);
                }
            });
        }

        [Fact]
        public void VerifierRejectsBytesAppendedAfterEof()
        {
            var (fin, _) = Export(D.Items, RedactionStyle.Black, new FinalExportOptions());
            byte[] tampered = fin.Concat(Encoding.ASCII.GetBytes("\n1 0 obj\n<< /X (SECRET) >>\nendobj\n")).ToArray();
            Assert.NotEmpty(OutputVerifier.VerifyStructure(tampered, 5));
        }

        [Fact]
        public void VerifierRejectsInjectedObjectAndChangedImage()
        {
            var (fin, _) = Export(D.Items, RedactionStyle.Black, new FinalExportOptions());
            string s = MiniPdfWriter.Latin1.GetString(fin);
            // Rewrite the catalog with an extra entry of the same length -> must not match the whitelist.
            string bad = s.Replace("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Catalog /Pages 2 0 R /A 1>>");
            Assert.NotEmpty(OutputVerifier.VerifyStructure(MiniPdfWriter.Latin1.GetBytes(bad), 5));
            // Flip one byte inside the first image stream (lossless: Adler-32 / inflate must fail).
            var (fz, _) = Export(D.Items, RedactionStyle.Black, new FinalExportOptions { TryLossless = true });
            int at = MiniPdfWriter.Latin1.GetString(fz).IndexOf("/FlateDecode", StringComparison.Ordinal);
            int streamStart = MiniPdfWriter.Latin1.GetString(fz).IndexOf("stream\n", at, StringComparison.Ordinal) + 7;
            fz[streamStart + 40] ^= 0x55;
            Assert.NotEmpty(OutputVerifier.VerifyStructure(fz, 5));
        }

        [Fact]
        public void VerifierRejectsAnOutputWhoseRedactionsWereNotBurned()
        {
            var (fin, _) = Export(new List<RedactionItem>(), RedactionStyle.Black, new FinalExportOptions());
            Assert.NotEmpty(Pdf.Run(() => OutputVerifier.VerifyWithPdfium(fin, D.Pages, D.Items, RedactionStyle.Black)));
        }

        [Fact]
        public void DenseBlackTextPageIsGrayAndLossless()
        {
            var dense = TestPdfFactory.Build(new[] { new TestPdfFactory.PageSpec { NoMarkers = true, TextLines = 68 } }, false);
            var (fin, rep) = Export(new List<RedactionItem>(), RedactionStyle.Black, new FinalExportOptions(), dense.Bytes);
            Assert.False(rep[0].Color);
            Assert.Contains("/DeviceGray", MiniPdfWriter.Latin1.GetString(fin));
            Assert.Empty(OutputVerifier.VerifyStructure(fin, 1));
            Assert.Empty(Pdf.Run(() => OutputVerifier.VerifyWithPdfium(fin, new[] { new PageInfo(595, 842, 0) }, new List<RedactionItem>(), RedactionStyle.Black)));
        }

        [Fact]
        public void HugePageIsReducedAndReported()
        {
            var a0 = TestPdfFactory.Build(new[] { new TestPdfFactory.PageSpec { MediaBox = new double[] { 0, 0, 2384, 3370 }, TextLines = 20 } }, false);
            string dir = TestDir.Make("a0");
            string dest = Path.Combine(dir, "a0_開示用.pdf");
            var res = Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(a0.Bytes);
                return FinalOutput.Write(doc, new[] { new PageInfo(2384, 3370, 0) }, new List<RedactionItem>(), RedactionStyle.Black, dest, new string[0], new FinalExportOptions { Dpi = 300 });
            });
            Assert.True((long)res.Pages[0].WidthPx * res.Pages[0].HeightPx <= 60_000_000);
            Assert.Equal(new[] { 0 }, res.ReducedPages);
            Assert.True(File.Exists(dest));
        }

        [Fact]
        public void FileOutputWritesPartThenRenamesAndReplaces()
        {
            string dir = TestDir.Make("file");
            string dest = Path.Combine(dir, "out_開示用.pdf");
            File.WriteAllText(dest, "old");
            var seenPart = false;
            var res = Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(D.Td.Bytes);
                return FinalOutput.Write(doc, D.Pages, D.Items, RedactionStyle.Black, dest, new string[0], new FinalExportOptions(),
                    (done, total) => { if (File.Exists(dest + ".part")) seenPart = true; });
            });
            Assert.True(seenPart);
            Assert.False(File.Exists(dest + ".part"));
            byte[] written = File.ReadAllBytes(dest);
            Assert.Equal(res.FileBytes, written.LongLength);
            using (var fs = File.OpenRead(dest)) Assert.Empty(OutputVerifier.VerifyStructure(fs, 5));
            Assert.Equal(0, Util.SecretCount(written));
        }

        [Fact]
        public void CancellationDeletesThePartFile()
        {
            string dir = TestDir.Make("cancel");
            string dest = Path.Combine(dir, "x.pdf");
            using var cts = new CancellationTokenSource();
            Assert.ThrowsAny<OperationCanceledException>(() => Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(D.Td.Bytes);
                return FinalOutput.Write(doc, D.Pages, D.Items, RedactionStyle.Black, dest, new string[0], new FinalExportOptions(),
                    (done, total) => { if (done == 2) cts.Cancel(); }, cts.Token);
            }));
            Assert.Empty(Directory.GetFiles(dir));
        }

        [Fact]
        public void VerificationFailureDeletesTheOutput()
        {
            string dir = TestDir.Make("verifyfail");
            string dest = Path.Combine(dir, "x.pdf");
            // Pages claim a different size than the document -> the PDFium check fails.
            var wrongPages = D.Pages.Select(p => new PageInfo(p.Width + 10, p.Height, p.Rotation)).ToList();
            var e = Assert.Throws<OutputVerificationException>(() => Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(D.Td.Bytes);
                return FinalOutput.Write(doc, wrongPages, D.Items, RedactionStyle.Black, dest, new string[0], new FinalExportOptions());
            }));
            Assert.NotEmpty(e.Failures);
            Assert.Empty(Directory.GetFiles(dir));
        }

        [Fact]
        public void OriginalAndOpenReviewFileAreRefusedAsDestination()
        {
            string dir = TestDir.Make("guard");
            string orig = Path.Combine(dir, "原本.pdf");
            File.WriteAllBytes(orig, D.Td.Bytes);
            string review = Path.Combine(dir, "原本_庁内確認用.pdf");
            File.WriteAllBytes(review, D.Td.Bytes);
            var forbidden = new[] { orig, review };
            Assert.False(SavePathGuard.IsAllowed(orig, forbidden));
            Assert.False(SavePathGuard.IsAllowed(orig.ToUpperInvariant(), forbidden));
            Assert.False(SavePathGuard.IsAllowed(Path.Combine(dir, "sub", "..", "原本.pdf"), forbidden));
            Assert.False(SavePathGuard.IsAllowed(review, forbidden));
            Assert.True(SavePathGuard.IsAllowed(Path.Combine(dir, "原本_開示用.pdf"), forbidden));

            // The original is held open read-only (as the app does) while the guard checks identity.
            using (var held = new FileStream(orig, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<ForbiddenDestinationException>(() => Pdf.Run(() =>
                {
                    using var doc = PdfDoc.FromBytes(D.Td.Bytes);
                    return FinalOutput.Write(doc, D.Pages, D.Items, RedactionStyle.Black, orig, forbidden, new FinalExportOptions());
                }));
            }
            Assert.Equal(D.Td.Bytes, File.ReadAllBytes(orig));
            Assert.False(File.Exists(orig + ".part"));
        }

        [Fact]
        public void HardLinkToTheOriginalIsRefused()
        {
            string dir = TestDir.Make("hardlink");
            string orig = Path.Combine(dir, "a.pdf"), link = Path.Combine(dir, "b.pdf");
            File.WriteAllBytes(orig, D.Td.Bytes);
            if (!CreateHardLink(link, orig, IntPtr.Zero)) return;   // file system without hard links: nothing to test
            Assert.False(SavePathGuard.IsAllowed(link, new[] { orig }));
        }

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string newFile, string existing, IntPtr security);

        [Fact]
        public void ThreeHundredPagesKeepMemoryFlat()
        {
            var specs = Enumerable.Range(0, 300).Select(_ => new TestPdfFactory.PageSpec { TextLines = 40 }).ToList();
            string dir = TestDir.Make("big300");
            string path = Path.Combine(dir, "big300.pdf"), outPath = Path.Combine(dir, "big300_開示用.pdf");
            File.WriteAllBytes(path, TestPdfFactory.Build(specs, false).Bytes);
            var items = Enumerable.Range(0, 300).Select(i => new RedactionItem(Guid.NewGuid(), i, new Shape[] { new RectShape(100, 100, 200, 30) }, null)).ToList();
            var pages = Enumerable.Range(0, 300).Select(_ => new PageInfo(595, 842, 0)).ToList();
            var proc = Process.GetCurrentProcess();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var ws = new Dictionary<int, long>();
            long peak = 0;
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromFile(path);
                FinalOutput.Write(doc, pages, items, RedactionStyle.Black, outPath, new[] { path }, new FinalExportOptions(), (done, total) =>
                {
                    proc.Refresh();
                    long w = proc.WorkingSet64;
                    if (w > peak) peak = w;
                    if (done == 20 || done == 150 || done == 300) ws[done] = w;
                });
            });
            Assert.True(ws[300] - ws[20] < 100L * 1024 * 1024, "working set grew from " + ws[20] / 1048576 + " to " + ws[300] / 1048576 + " MB");
            Assert.True(peak < 600L * 1024 * 1024, "peak " + peak / 1048576 + " MB");
            using (var fs = File.OpenRead(outPath)) Assert.Empty(OutputVerifier.VerifyStructure(fs, 300));
        }
    }
}
