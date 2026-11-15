using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Kuronuri.Core.Export;
using Kuronuri.Core.Geometry;
using Kuronuri.Core.Hybrid;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;
using Xunit;
using N = Kuronuri.Core.Pdfium.PdfiumNative;

namespace Kuronuri.Core.Tests
{
    public class HybridTests
    {
        private static StandardData D => StandardData.Instance;

        private static RedactionProject Project(RedactionStyle style = RedactionStyle.Black) =>
            new RedactionProject(new OriginalInfo(Util.Sha(D.Td.Bytes), D.Td.Bytes.Length, "original.pdf"), D.Pages, D.Items, style);

        [Fact]
        public void LabelSeparatesNumberFromReasonCode()
        {
            // "1 3号" was easy to read as "13号": the number gets a prefix and the code brackets (ARCHITECTURE §9.1).
            var shape = new Shape[] { new RectShape(10, 10, 20, 20) };
            var withReason = new RedactionItem(Guid.NewGuid(), 0, shape, new Reason("3号", "公共安全情報", ""));
            var unset = new RedactionItem(Guid.NewGuid(), 0, shape, null);
            var emptyCode = new RedactionItem(Guid.NewGuid(), 0, shape, new Reason("", "", "自由記述のみ"));
            Assert.Equal("No.1（3号）", HybridWriter.LabelText(1, withReason));
            Assert.Equal("No.13", HybridWriter.LabelText(13, unset));
            Assert.Equal("No.2", HybridWriter.LabelText(2, emptyCode));
            Assert.Equal("No.7", HybridWriter.NumberLabel(7));
        }

        [Fact]
        public void AttachmentsRoundTripByteIdentical()
        {
            byte[] hyb = Pdf.Run(() => HybridWriter.Build(D.Td.Bytes, D.Json, D.Items, new HybridOptions { DrawText = false }));
            Assert.True(Pdf.Run(() => HybridReader.TryRead(hyb, out var o, out var j) && Util.Sha(o!) == Util.Sha(D.Td.Bytes) && j!.SequenceEqual(D.Json)));
            Assert.Equal(D.Json, ProjectJson.Serialize(ProjectJson.Deserialize(D.Json), "0.0.0-test"));
        }

        [Fact]
        public void VectorReviewKeepsPagesTextAndShowsOverlaysAtTheRightPlace()
        {
            byte[] hyb = Pdf.Run(() => HybridWriter.Build(D.Td.Bytes, D.Json, D.Items, new HybridOptions { DrawText = false }));
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(hyb);
                Assert.Equal(5, doc.PageCount);
                for (int i = 0; i < doc.PageCount; i++)
                {
                    using var page = doc.LoadPage(i);
                    var g = PageGeometry.FromPage(page.Handle);
                    Assert.True(Math.Abs(g.Wd - D.Pages[i].Width) <= 0.01 && Math.Abs(g.Hd - D.Pages[i].Height) <= 0.01 && g.Rotation == D.Geoms[i].Rotation);
                    Assert.Contains(TestPdfFactory.SecretText, Util.PageText(page.Handle));   // reviewers can search
                    using var r = Raster.Render(page.Handle, 100);
                    var rc = (RectShape)D.Items[i * 2].Shapes[0];
                    r.Rgb(rc.X + 1.5, rc.Y + 1.5, out int cr, out int cg, out int cb);
                    Assert.True(cr >= 200 && cg >= 100 && cg <= 235 && cr - cg >= 20 && Math.Abs(cg - cb) <= 12, "p" + (i + 1) + " rect(" + cr + "," + cg + "," + cb + ")");
                    r.Rgb(rc.X - 3, rc.Y - 3, out cr, out cg, out cb);
                    Assert.True(cr >= 240 && cg >= 240 && cb >= 240, "p" + (i + 1) + " outside");
                    foreach (var mk in D.Td.Markers[i])
                    {
                        CoordMap.UserToDisplay(D.Geoms[i], mk.UX, mk.UY, out double xd, out double yd);
                        r.Rgb(xd, yd, out cr, out cg, out cb);
                        if (mk.Name == "blue") Assert.True(cr >= 40 && cb <= 235 && cb >= 100, "p" + (i + 1) + " stroke");
                        else Assert.True(Math.Abs(cr - mk.R) <= 12 && Math.Abs(cg - mk.G) <= 12 && Math.Abs(cb - mk.B) <= 12, "p" + (i + 1) + " " + mk.Name);
                    }
                }
            });
        }

        [Fact]
        public void TextIsDrawnAsOutlinesWithoutFontsAndSharedWatermarkMatchesPerPage()
        {
            byte[] plain = Pdf.Run(() => HybridWriter.Build(D.Td.Bytes, D.Json, D.Items, new HybridOptions { DrawText = false }));
            byte[] withText = Pdf.Run(() => HybridWriter.Build(D.Td.Bytes, D.Json, D.Items, new HybridOptions()));
            byte[] perPage = Pdf.Run(() => HybridWriter.Build(D.Td.Bytes, D.Json, D.Items, new HybridOptions { SharedWatermark = false }));
            Pdf.Run(() =>
            {
                using var a = PdfDoc.FromBytes(plain);
                using var b = PdfDoc.FromBytes(withText);
                using var c = PdfDoc.FromBytes(perPage);
                Assert.Equal(Util.WithPage(a, 0, Util.CharCount), Util.WithPage(b, 0, Util.CharCount));
                Assert.True(Diff(a, b, 0, 0) > 500);
                for (int i = 0; i < 5; i++) Assert.True(Diff(c, b, i, 24) < 200, "page " + (i + 1));
            });
            Assert.Equal(Util.Count(plain, Encoding.ASCII.GetBytes("/Font")), Util.Count(withText, Encoding.ASCII.GetBytes("/Font")));
            Assert.True(withText.Length < perPage.Length);
        }

        private static unsafe long Diff(PdfDoc a, PdfDoc b, int index, int tol)
        {
            using var pa = a.LoadPage(index);
            using var pb = b.LoadPage(index);
            using var ra = Raster.Render(pa.Handle, 100);
            using var rb = Raster.Render(pb.Handle, 100);
            byte* x = (byte*)ra.Ptr, y = (byte*)rb.Ptr;
            long d = 0;
            for (long k = 0; k < (long)ra.W * ra.H * 4; k++) if (Math.Abs(x[k] - y[k]) > tol) { d++; k |= 3; }
            return d;
        }

        [Fact]
        public void SaveLoadRoundTripAndResaveIsStable()
        {
            string dir = TestDir.Make("review");
            string dest = Path.Combine(dir, ReviewFile.DefaultReviewName("原本.pdf"));
            Assert.EndsWith("原本_庁内確認用.pdf", dest);
            var project = Project(RedactionStyle.WhiteWithBorder);
            var lens = new List<long>();
            for (int k = 0; k < 4; k++)
            {
                var res = Pdf.Run(() => ReviewFile.Save(D.Td.Bytes, project, "1.0.0", dest, new string[0]));
                Assert.Equal(ReviewSaveMethod.Vector, res.Method);
                Assert.False(File.Exists(dest + ".part"));
                lens.Add(new FileInfo(dest).Length);
                var loaded = Pdf.Run(() => { using var doc = PdfDoc.FromFile(dest); return ReviewFile.TryLoad(doc); });
                Assert.NotNull(loaded);
                Assert.Equal(D.Td.Bytes, loaded!.Original);
                Assert.Equal(project.Items.Select(i => i.Id), loaded.Project.Items.Select(i => i.Id));
                Assert.Equal(project.Items.Select(i => i.Shapes.Count), loaded.Project.Items.Select(i => i.Shapes.Count));
                Assert.Equal(RedactionStyle.WhiteWithBorder, loaded.Project.Style);
                project = loaded.Project;
            }
            Assert.All(lens, l => Assert.Equal(lens[0], l));
        }

        [Fact]
        public void RasterFallbackRoundTripsAndShowsOverlays()
        {
            string dir = TestDir.Make("raster");
            string dest = Path.Combine(dir, "r.pdf");
            var res = Pdf.Run(() => ReviewFile.Save(D.Td.Bytes, Project(), "1.0.0", dest, new string[0], forceRaster: true));
            Assert.Equal(ReviewSaveMethod.Raster, res.Method);
            byte[] hyb = File.ReadAllBytes(dest);
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(hyb);
                var loaded = ReviewFile.TryLoad(doc);
                Assert.NotNull(loaded);
                Assert.Equal(D.Td.Bytes, loaded!.Original);
                for (int i = 0; i < doc.PageCount; i++)
                {
                    using var page = doc.LoadPage(i);
                    Assert.Equal(0, Util.CharCount(page.Handle));
                    using var r = Raster.Render(page.Handle, 100);
                    var rc = (RectShape)D.Items[i * 2].Shapes[0];
                    r.Rgb(rc.X + 1.5, rc.Y + 1.5, out int cr, out int cg, out int cb);
                    Assert.True(cr >= 200 && cg >= 100 && cg <= 235 && cr - cg >= 20, "p" + (i + 1) + "(" + cr + "," + cg + "," + cb + ")");
                }
            });
        }

        [Fact]
        public unsafe void RasterFallbackDrawsLabelsAndWatermark()
        {
            var ms1 = new MemoryStream(); var ms2 = new MemoryStream();
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(D.Td.Bytes);
                HybridRasterWriter.Build(doc, D.Td.Bytes, D.Json, D.Items, ms1, opt: new HybridOptions { DrawText = false });
                HybridRasterWriter.Build(doc, D.Td.Bytes, D.Json, D.Items, ms2, opt: new HybridOptions());
            });
            long[] diffs = Pdf.Run(() =>
            {
                using var a = PdfDoc.FromBytes(ms1.ToArray());
                using var b = PdfDoc.FromBytes(ms2.ToArray());
                return Enumerable.Range(0, 5).Select(i => Diff(a, b, i, 24)).ToArray();
            });
            Assert.All(diffs, d => Assert.True(d > 500, "changed pixels " + d));
            // The raster watermark sits where the vector one does (same transform), compared on page 1.
            byte[] vec = Pdf.Run(() => HybridWriter.Build(D.Td.Bytes, D.Json, D.Items, new HybridOptions()));
            byte[] vecPlain = Pdf.Run(() => HybridWriter.Build(D.Td.Bytes, D.Json, D.Items, new HybridOptions { DrawText = false }));
            Pdf.Run(() =>
            {
                using var v = PdfDoc.FromBytes(vec); using var vp = PdfDoc.FromBytes(vecPlain);
                using var r = PdfDoc.FromBytes(ms2.ToArray()); using var rp = PdfDoc.FromBytes(ms1.ToArray());
                for (int i = 0; i < 5; i++)
                {
                    var mv = ChangedMask(v, vp, i); var mr = ChangedMask(r, rp, i);
                    int both = 0, any = 0;
                    for (int k = 0; k < mv.Length; k++) { if (mv[k] && mr[k]) both++; if (mv[k] || mr[k]) any++; }
                    Assert.True(both > 0.6 * any, "page " + (i + 1) + " overlap " + both + "/" + any);
                }
            });
        }

        private static unsafe bool[] ChangedMask(PdfDoc a, PdfDoc b, int index)
        {
            using var pa = a.LoadPage(index); using var pb = b.LoadPage(index);
            using var ra = Raster.Render(pa.Handle, 50); using var rb = Raster.Render(pb.Handle, 50);
            var m = new bool[ra.W * ra.H];
            uint* x = (uint*)ra.Ptr, y = (uint*)rb.Ptr;
            for (int k = 0; k < m.Length; k++)
            {
                int d = 0;
                for (int c = 0; c < 24; c += 8) d = Math.Max(d, Math.Abs((int)((x[k] >> c) & 0xFF) - (int)((y[k] >> c) & 0xFF)));
                m[k] = d > 20;
            }
            return m;
        }

        [Fact]
        public void PlainPdfIsNotAReviewFile()
        {
            Pdf.Run(() =>
            {
                using var doc = PdfDoc.FromBytes(D.Td.Bytes);   // has a "secret.txt" attachment only
                Assert.Null(ReviewFile.TryLoad(doc));
            });
        }

        [Fact]
        public void TamperedEmbeddedOriginalIsReportedAsCorrupt()
        {
            var project = Project() with { Original = new OriginalInfo(new string('0', 64), D.Td.Bytes.Length, "original.pdf") };
            byte[] json = ProjectJson.Serialize(project, "1.0.0");
            byte[] hyb = Pdf.Run(() => HybridWriter.Build(D.Td.Bytes, json, D.Items, new HybridOptions { DrawText = false }));
            Assert.Throws<ReviewFileCorruptException>(() => Pdf.Run(() => { using var doc = PdfDoc.FromBytes(hyb); return ReviewFile.TryLoad(doc); }));
        }

        [Fact]
        public void ForeignJsonWithTheSameAttachmentNamesOpensAsOriginal()
        {
            byte[] hyb = Pdf.Run(() => HybridWriter.Build(D.Td.Bytes, Encoding.UTF8.GetBytes("{\"app\":\"other\"}"), D.Items, new HybridOptions { DrawText = false }));
            Assert.Null(Pdf.Run(() => { using var doc = PdfDoc.FromBytes(hyb); return ReviewFile.TryLoad(doc); }));
        }

        [Fact]
        public void SaveRefusesTheOriginalPathAndOverwritesAReviewThroughPart()
        {
            string dir = TestDir.Make("reviewguard");
            string orig = Path.Combine(dir, "a.pdf");
            File.WriteAllBytes(orig, D.Td.Bytes);
            Assert.Throws<ForbiddenDestinationException>(() => Pdf.Run(() => ReviewFile.Save(D.Td.Bytes, Project(), "1.0.0", orig, new[] { orig })));
            Assert.Equal(D.Td.Bytes, File.ReadAllBytes(orig));

            string review = Path.Combine(dir, "a_庁内確認用.pdf");
            File.WriteAllText(review, "old");
            Pdf.Run(() => ReviewFile.Save(D.Td.Bytes, Project(), "1.0.0", review, new[] { orig }));
            Assert.NotEqual("old", File.ReadAllText(review).Substring(0, 3));
            Assert.Equal(new[] { orig, review }.OrderBy(x => x), Directory.GetFiles(dir).OrderBy(x => x));
        }

        [Fact]
        public void ProjectForAnotherOriginalIsRejected()
        {
            var bad = Project() with { Original = new OriginalInfo("00", 1, "x.pdf") };
            string dir = TestDir.Make("mismatch");
            Assert.Throws<InvalidOperationException>(() => Pdf.Run(() => ReviewFile.Save(D.Td.Bytes, bad, "1.0.0", Path.Combine(dir, "x.pdf"), new string[0])));
            Assert.Empty(Directory.GetFiles(dir));
        }
    }
}
