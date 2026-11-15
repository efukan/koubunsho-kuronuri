using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kuronuri.Core.Export;
using Kuronuri.Core.Hybrid;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;
using Kuronuri.Core.Text;
using Xunit;
using Xunit.Abstractions;

namespace Kuronuri.Core.Tests
{
    /// <summary>
    /// Broken-input robustness: deterministic mutations of valid PDFs go through the whole pipeline (open, render, text,
    /// final output + verification, review PDF + read-back). The test passes when the process survives and every failure
    /// surfaces as a managed exception that the app already handles. A native crash in PDFium would kill the test host.
    /// </summary>
    public class RobustnessTests
    {
        private readonly ITestOutputHelper _out;
        public RobustnessTests(ITestOutputHelper output) { _out = output; }

        private static byte[] Mutate(byte[] src, Random rnd)
        {
            var b = (byte[])src.Clone();
            switch (rnd.Next(5))
            {
                case 0: // flip a few random bytes
                    for (int k = rnd.Next(1, 20); k > 0; k--) b[rnd.Next(b.Length)] = (byte)rnd.Next(256);
                    return b;
                case 1: // truncate
                    return b.Take(rnd.Next(16, b.Length)).ToArray();
                case 2: // damage digits (offsets, lengths, numbers in dictionaries)
                    for (int k = rnd.Next(1, 12); k > 0; k--)
                    {
                        int at = rnd.Next(b.Length);
                        for (int j = at; j < Math.Min(b.Length, at + 200); j++) if (b[j] >= (byte)'0' && b[j] <= (byte)'9') { b[j] = (byte)('0' + rnd.Next(10)); break; }
                    }
                    return b;
                case 3: // duplicate a slice somewhere else
                {
                    int a = rnd.Next(b.Length), len = rnd.Next(1, Math.Min(400, b.Length - a)), to = rnd.Next(b.Length);
                    var list = b.ToList();
                    list.InsertRange(to, b.Skip(a).Take(len));
                    return list.ToArray();
                }
                default: // damage the xref / trailer region
                {
                    int tail = Math.Max(0, b.Length - rnd.Next(10, 400));
                    for (int k = rnd.Next(1, 8); k > 0; k--) b[rnd.Next(tail, b.Length)] = (byte)rnd.Next(32, 127);
                    return b;
                }
            }
        }

        [Fact]
        public void MutatedPdfsNeverCrashTheProcess()
        {
            var sources = new List<byte[]>
            {
                StandardData.Instance.Td.Bytes,
                TextPdf.Build((new[] { "TEL 03-1234-5678", "Mail a@example.jp" }, 0), (new[] { "Rotated page" }, 90)),
                EncryptedPdf.Build("", "owner"),
            };
            var rnd = new Random(20261115);
            int opened = 0, rejected = 0, exported = 0, reviewOk = 0;
            var failures = new Dictionary<string, int>();
            const int Cases = 1500;
            for (int n = 0; n < Cases; n++)
            {
                byte[] pdf = Mutate(sources[n % sources.Count], rnd);
                Pdf.Run(() =>
                {
                    PdfDoc doc;
                    try { doc = PdfDoc.FromBytes(pdf); }
                    catch (PdfLoadException) { rejected++; return; }
                    using (doc)
                    {
                        opened++;
                        int pages = Math.Min(doc.PageCount, 6);
                        var infos = new List<PageInfo>();
                        try
                        {
                            for (int i = 0; i < pages; i++)
                            {
                                using var p = doc.LoadPage(i);
                                infos.Add(new PageInfo(p.Width, p.Height, 0));
                                using (Raster.Render(p.Handle, 24)) { }
                                PageText.Read(doc, i);
                            }
                            if (pages == 0) return;
                            var items = new List<RedactionItem> { new RedactionItem(Guid.NewGuid(), 0, new Shape[] { new RectShape(10, 10, 50, 20) }, null) };
                            var ms = new MemoryStream();
                            FinalExporter.Export(doc, items, RedactionStyle.Black, ms, new FinalExportOptions { Dpi = 36, MaxPixelsPerPage = 4_000_000 });
                            if (doc.PageCount == pages && OutputVerifier.VerifyStructure(ms.ToArray(), pages).Count == 0) exported++;

                            var json = ProjectJson.Serialize(new RedactionProject(new OriginalInfo(Util.Sha(pdf), pdf.Length, "x.pdf"), infos, items, RedactionStyle.Black), "t");
                            byte[] hyb = HybridWriter.Build(pdf, json, items, new HybridOptions { DrawText = false });
                            if (HybridReader.TryRead(hyb, out var o, out _) && o!.SequenceEqual(pdf)) reviewOk++;
                        }
                        catch (Exception e) when (e is PdfLoadException || e is InvalidOperationException)   // the types the app handles; anything else fails the test
                        {
                            string k = e.GetType().Name;
                            failures[k] = failures.TryGetValue(k, out int c) ? c + 1 : 1;
                        }
                    }
                });
            }
            _out.WriteLine("cases=" + Cases + " opened=" + opened + " rejected=" + rejected + " exported+verified=" + exported + " review round-trip=" + reviewOk +
                           " handled failures: " + string.Join(", ", failures.Select(f => f.Key + "=" + f.Value)));
            Assert.Equal(Cases, opened + rejected);
        }
    }
}
