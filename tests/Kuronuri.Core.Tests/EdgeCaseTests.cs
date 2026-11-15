using System;
using System.Collections.Generic;
using System.IO;
using Kuronuri.Core.Export;
using Kuronuri.Core.Hybrid;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Xunit;

namespace Kuronuri.Core.Tests
{
    /// <summary>ARCHITECTURE §14.</summary>
    public class EdgeCaseTests
    {
        [Fact]
        public void PasswordRequiredPdfIsRefusedWithItsKind()
        {
            byte[] pdf = EncryptedPdf.Build("user", "owner");
            var e = Assert.Throws<PdfLoadException>(() => Pdf.Run(() => { using var d = PdfDoc.FromBytes(pdf); }));
            Assert.Equal(PdfErrorKind.PasswordRequired, e.Kind);
            Assert.DoesNotContain("user", e.Message);
            Assert.Contains("パスワード", PdfLoadException.Describe(e.Kind));
        }

        [Fact]
        public void PermissionsOnlyPdfOpensAndExportsAndSavesForReview()
        {
            byte[] pdf = EncryptedPdf.Build("", "owner");
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(pdf);
                Assert.Equal(1, doc.PageCount);
                Assert.Contains(EncryptedPdf.Text, Util.WithPage(doc, 0, Util.PageText));
                var items = new List<RedactionItem> { new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(60, 100, 120, 40) }, null) };
                var pages = new[] { new PageInfo(595, 842, 0) };
                var ms = new MemoryStream();
                FinalExporter.Export(doc, items, RedactionStyle.Black, ms, new FinalExportOptions());
                Assert.Empty(OutputVerifier.VerifyStructure(ms.ToArray(), 1));
                Assert.Empty(OutputVerifier.VerifyWithPdfium(ms.ToArray(), pages, items, RedactionStyle.Black));

                // Review save: either method is fine, but it must round-trip the encrypted original byte for byte.
                string dir = TestDir.Make("encrypted-review");
                string dest = Path.Combine(dir, "r.pdf");
                var project = new RedactionProject(new OriginalInfo(Util.Sha(pdf), pdf.Length, "e.pdf"), pages, items, RedactionStyle.Black);
                var saved = ReviewFile.Save(pdf, project, "1.0.0", dest, new string[0]);
                Assert.Equal(ReviewSaveMethod.Vector, saved.Method);   // ARCHITECTURE §17: page import works for permission-only PDFs
                using var back = PdfDoc.FromFile(dest);
                var content = ReviewFile.TryLoad(back);
                Assert.NotNull(content);
                Assert.Equal(pdf, content!.Original);
            });
        }

        [Fact]
        public void ExtremelyTallPageIsExportedWithinTheJpegSideLimit()
        {
            // 30 x 40000 pt: at 200 dpi the long side would be 111,112 px, beyond the JPEG limit, while the pixel count
            // (about 9 MP) is far under the 60 MP budget. Found by the fuzz test (RobustnessTests).
            var tall = TestPdfFactory.Build(new[] { new TestPdfFactory.PageSpec { MediaBox = new double[] { 0, 0, 30, 40000 }, NoMarkers = true } }, false);
            var items = new List<RedactionItem> { new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(2, 30000, 20, 500) }, null) };
            var pages = new[] { new PageInfo(30, 40000, 0) };
            foreach (bool lossless in new[] { false, true })
            {
                var ms = new MemoryStream();
                var rep = Pdf.Run(() => { using var doc = PdfDoc.FromBytes(tall.Bytes); return FinalExporter.Export(doc, items, RedactionStyle.Black, ms, new FinalExportOptions { TryLossless = lossless }); });
                Assert.True(rep[0].HeightPx <= Kuronuri.Core.Rendering.PageRenderer.MaxEncodableSide && rep[0].WidthPx >= 1);
                Assert.True(rep[0].EffectiveDpi < 200);   // reported to the user as a reduced page
                Assert.Empty(OutputVerifier.VerifyStructure(ms.ToArray(), 1));
                Assert.Empty(Pdf.Run(() => OutputVerifier.VerifyWithPdfium(ms.ToArray(), pages, items, RedactionStyle.Black)));
            }
        }

        [Fact]
        public void ZeroPageFileIsNotAPdfWeOpen()
        {
            var ms = new MemoryStream();
            var w = new MiniPdfWriter(ms);
            int catalog = w.Reserve(), pages = w.Reserve();
            w.WriteObject(pages, "<< /Type /Pages /Count 0 /Kids [] >>");
            w.WriteObject(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
            w.Finish(catalog);
            int count = Pdf.Run(() => { using var d = PdfDoc.FromBytes(ms.ToArray()); return d.PageCount; });
            Assert.Equal(0, count);   // the app refuses this with PdfErrorKind.NoPages (DocumentSession.Load)
        }

        [Fact]
        public void StreamLongerThan4GbIsRejectedBeforeParsing()
        {
            var e = Assert.Throws<PdfLoadException>(() => Pdf.Run(() => { using var d = PdfDoc.FromStream(new FakeLengthStream(5L << 30)); }));
            Assert.Equal(PdfErrorKind.TooLarge, e.Kind);
        }

        [Fact]
        public void MissingFileIsAFileError()
        {
            var e = Assert.Throws<PdfLoadException>(() => Pdf.Run(() => { using var d = PdfDoc.FromFile(Path.Combine(TestDir.Make("missing"), "nope.pdf")); }));
            Assert.Equal(PdfErrorKind.File, e.Kind);
        }

        private sealed class FakeLengthStream : Stream
        {
            private readonly long _len;
            public FakeLengthStream(long len) { _len = len; }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _len;
            public override long Position { get; set; }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override long Seek(long offset, SeekOrigin origin) => Position = offset;
            public override void SetLength(long value) { }
            public override void Write(byte[] buffer, int offset, int count) { }
        }
    }
}
