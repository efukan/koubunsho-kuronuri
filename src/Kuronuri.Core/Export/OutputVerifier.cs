using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;

namespace Kuronuri.Core.Export
{
    /// <summary>
    /// Verifies a final output file. An empty list means it passed. The structure check seeks through the file and only
    /// reads the xref, the object heads and (for lossless images) streams the image data through the inflater, so a large
    /// output never has to be held in memory.
    /// </summary>
    public static class OutputVerifier
    {
        private static readonly Regex RxTail = new Regex(@"\nstartxref\n(\d+)\n%%EOF\n$");
        private static readonly Regex RxXref = new Regex(@"^xref\n0 (\d+)\n((?:\d{10} \d{5} [nf] \n)+)trailer\n<< /Size (\d+) /Root 1 0 R >>\nstartxref\n\d+\n%%EOF\n$");
        private static readonly Regex RxCatalog = new Regex(@"^<< /Type /Catalog /Pages 2 0 R >>$");
        private static readonly Regex RxPages = new Regex(@"^<< /Type /Pages /Count (\d+) /Kids \[\d+ 0 R(?: \d+ 0 R)*\] >>$");
        private static readonly Regex RxPage = new Regex(@"^<< /Type /Page /Parent 2 0 R /MediaBox \[0 0 [\d.]+ [\d.]+\] /Resources << /XObject << /Im0 \d+ 0 R >> >> /Contents \d+ 0 R >>$");
        private static readonly Regex RxContents = new Regex(@"^<< /Length (\d+) >>$");
        private static readonly Regex RxContentBody = new Regex(@"^q [\d.]+ 0 0 [\d.]+ 0 0 cm /Im0 Do Q$");
        private static readonly Regex RxImage = new Regex(@"^<< /Type /XObject /Subtype /Image /Width (\d+) /Height (\d+) /ColorSpace /Device(RGB|Gray) /BitsPerComponent 8 /Filter /(DCTDecode|FlateDecode) /Length (\d+) >>$");

        private const string StreamEnd = "\nendstream\nendobj\n";   // 18 bytes
        private const int MaxHeadBytes = 64 * 1024;                  // an object head (3 lines) is never longer than this

        public static List<string> VerifyStructure(byte[] pdf, int pageCount)
        {
            using (var ms = new MemoryStream(pdf, false)) return VerifyStructure(ms, pageCount);
        }

        /// <summary>
        /// Whitelist check: walks every object through the xref table. Each object must be exactly one of the five
        /// known shapes, objects must be contiguous (no hidden bytes), and nothing may follow %%EOF.
        /// </summary>
        public static List<string> VerifyStructure(Stream pdf, int pageCount)
        {
            var fails = new List<string>();
            var L = MiniPdfWriter.Latin1;
            long fileLen = pdf.Length;
            int tailLen = (int)Math.Min(64, fileLen);
            var mt = RxTail.Match(L.GetString(ReadAt(pdf, fileLen - tailLen, tailLen)));
            if (!mt.Success) { fails.Add("tail: startxref/EOF not found at the end"); return fails; }
            long xref = long.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture);
            if (xref <= 0 || xref >= fileLen) { fails.Add("xref offset out of range"); return fails; }
            long xrefLen = fileLen - xref;
            long maxXref = 20L * (3 + 3L * pageCount) + 256;
            if (xrefLen > maxXref) { fails.Add("xref/trailer is too long"); return fails; }
            var mx = RxXref.Match(L.GetString(ReadAt(pdf, xref, (int)xrefLen)));
            if (!mx.Success) { fails.Add("xref/trailer is not in the expected form"); return fails; }
            int size = int.Parse(mx.Groups[1].Value, CultureInfo.InvariantCulture);
            if (mx.Groups[3].Value != mx.Groups[1].Value) fails.Add("trailer /Size mismatch");
            if (size != 3 + 3 * pageCount) fails.Add("object count is " + (size - 1) + ", expected " + (2 + 3 * pageCount));
            string entries = mx.Groups[2].Value;
            if (entries.Length != size * 20) { fails.Add("xref entry count mismatch"); return fails; }

            var byOffset = new SortedDictionary<long, int>();
            for (int i = 1; i < size; i++)
            {
                string e = entries.Substring(i * 20, 20);
                if (e[17] != 'n') { fails.Add("free entry " + i); continue; }
                byOffset[long.Parse(e.Substring(0, 10), CultureInfo.InvariantCulture)] = i;
            }
            if (byOffset.Count != size - 1) { fails.Add("duplicate or free object offsets"); return fails; }
            if (byOffset.Keys.First() != MiniPdfWriter.Header.Length) fails.Add("unexpected bytes between header and first object");
            if (L.GetString(ReadAt(pdf, 0, Math.Min(MiniPdfWriter.Header.Length, (int)fileLen))) != MiniPdfWriter.Header) fails.Add("header mismatch");

            int nCatalog = 0, nPages = 0, nPage = 0, nContents = 0, nImage = 0;
            var offsets = byOffset.Keys.ToList();
            for (int k = 0; k < offsets.Count; k++)
            {
                long start = offsets[k];
                long end = k + 1 < offsets.Count ? offsets[k + 1] : xref;
                if (end <= start || end > fileLen) { fails.Add("object offsets out of order"); break; }
                int num = byOffset[start];
                byte[] head = ReadAt(pdf, start, (int)Math.Min(MaxHeadBytes, end - start));
                int pos = 0;
                string h = ReadLine(head, ref pos);
                if (h != num.ToString(CultureInfo.InvariantCulture) + " 0 obj") { fails.Add("object header mismatch at " + start); continue; }
                string dict = ReadLine(head, ref pos);
                string next = ReadLine(head, ref pos);
                long p = start + pos;   // absolute position after the three lines
                Match m;
                if (next == "endobj")
                {
                    if (RxCatalog.IsMatch(dict)) nCatalog++;
                    else if ((m = RxPages.Match(dict)).Success) { nPages++; if (m.Groups[1].Value != pageCount.ToString(CultureInfo.InvariantCulture)) fails.Add("/Count mismatch"); }
                    else if (RxPage.IsMatch(dict)) nPage++;
                    else fails.Add("unknown object " + num);
                }
                else if (next == "stream")
                {
                    bool isImage = (m = RxImage.Match(dict)).Success;
                    if (!isImage) m = RxContents.Match(dict);
                    if (!m.Success) { fails.Add("unknown stream object " + num); continue; }
                    long len = long.Parse(m.Groups[isImage ? 5 : 1].Value, CultureInfo.InvariantCulture);
                    if (p + len + StreamEnd.Length > fileLen) { fails.Add("stream length out of range in " + num); continue; }
                    if (isImage)
                    {
                        nImage++;
                        if (m.Groups[4].Value == "DCTDecode")
                        {
                            byte[] a = ReadAt(pdf, p, 2), z = len >= 4 ? ReadAt(pdf, p + len - 2, 2) : new byte[2];
                            if (len < 4 || a[0] != 0xFF || a[1] != 0xD8 || z[0] != 0xFF || z[1] != 0xD9) fails.Add("image " + num + " is not a complete JPEG");
                        }
                        else
                        {
                            long expected = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * (m.Groups[3].Value == "RGB" ? 3 : 1);
                            if (!ZlibOk(pdf, p, len, expected)) fails.Add("image " + num + " is not a valid zlib stream of the declared size");
                        }
                    }
                    else
                    {
                        nContents++;
                        if (len > 256 || !RxContentBody.IsMatch(L.GetString(ReadAt(pdf, p, (int)len)))) fails.Add("unexpected content stream in " + num);
                    }
                    p += len;
                    if (L.GetString(ReadAt(pdf, p, StreamEnd.Length)) != StreamEnd) { fails.Add("stream terminator missing in " + num); continue; }
                    p += StreamEnd.Length;
                }
                else { fails.Add("unexpected token after dictionary in " + num); continue; }
                if (p != end) fails.Add("gap or overlap after object " + num);
            }
            if (nCatalog != 1 || nPages != 1 || nPage != pageCount || nContents != pageCount || nImage != pageCount)
                fails.Add("object kinds: catalog=" + nCatalog + " pages=" + nPages + " page=" + nPage + " contents=" + nContents + " image=" + nImage);
            return fails;
        }

        private static byte[] ReadAt(Stream s, long pos, int count)
        {
            var b = new byte[Math.Max(0, count)];
            s.Position = pos;
            int done = 0;
            while (done < b.Length)
            {
                int n = s.Read(b, done, b.Length - done);
                if (n <= 0) break;
                done += n;
            }
            if (done < b.Length) Array.Resize(ref b, done);
            return b;
        }

        private static string ReadLine(byte[] b, ref int pos)
        {
            int s = pos;
            while (pos < b.Length && b[pos] != (byte)'\n') pos++;
            string line = MiniPdfWriter.Latin1.GetString(b, s, pos - s);
            if (pos < b.Length) pos++;
            return line;
        }

        /// <summary>Inflates a zlib stream in place (streaming) and checks both the decoded length and the Adler-32 trailer.</summary>
        private static bool ZlibOk(Stream pdf, long pos, long len, long expected)
        {
            if (len < 6) return false;
            byte[] hdr = ReadAt(pdf, pos, 2);
            if (hdr.Length < 2 || hdr[0] != 0x78 || ((hdr[0] << 8) | hdr[1]) % 31 != 0) return false;
            byte[] tr = ReadAt(pdf, pos + len - 4, 4);
            uint stored = ((uint)tr[0] << 24) | ((uint)tr[1] << 16) | ((uint)tr[2] << 8) | tr[3];
            uint a = 1, b = 0; long total = 0;
            try
            {
                using (var window = new WindowStream(pdf, pos + 2, len - 6))
                using (var ds = new System.IO.Compression.DeflateStream(window, System.IO.Compression.CompressionMode.Decompress, true))
                {
                    var chunk = new byte[64 * 1024]; int n;
                    while ((n = ds.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        total += n;
                        if (total > expected) return false;
                        for (int i = 0; i < n; i++) { a = (a + chunk[i]) % 65521; b = (b + a) % 65521; }
                    }
                }
            }
            catch (InvalidDataException) { return false; }
            return total == expected && ((b << 16) | a) == stored;
        }

        /// <summary>Read-only view of [start, start+length) of another seekable stream.</summary>
        private sealed class WindowStream : Stream
        {
            private readonly Stream _s; private readonly long _start, _len; private long _pos;
            public WindowStream(Stream s, long start, long len) { _s = s; _start = start; _len = len; }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _len;
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                long left = _len - _pos;
                if (left <= 0) return 0;
                if (count > left) count = (int)left;
                _s.Position = _start + _pos;
                int n = _s.Read(buffer, offset, count);
                _pos += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        public static List<string> VerifyWithPdfium(byte[] pdf, IReadOnlyList<PageInfo> pages, IReadOnlyList<RedactionItem> items, RedactionStyle style)
        {
            using (var doc = PdfDoc.FromBytes(pdf)) return VerifyWithPdfium(doc, pages, items, style);
        }

        /// <summary>Re-opens the output with PDFium: no text, one image per page, no annotations / attachments / bookmarks / metadata, and burned pixels.</summary>
        public static List<string> VerifyWithPdfium(PdfDoc doc, IReadOnlyList<PageInfo> pages, IReadOnlyList<RedactionItem> items, RedactionStyle style)
        {
            var fails = new List<string>();
            if (doc.PageCount != pages.Count) { fails.Add("page count"); return fails; }
            if (PdfiumNative.FPDFDoc_GetAttachmentCount(doc.Handle) != 0) fails.Add("attachments present");
            if (PdfiumNative.FPDFBookmark_GetFirstChild(doc.Handle, IntPtr.Zero) != IntPtr.Zero) fails.Add("bookmarks present");
            foreach (string tag in new[] { "Title", "Author", "Subject", "Keywords", "Creator", "Producer", "CreationDate", "ModDate" })
                if (PdfiumNative.FPDF_GetMetaText(doc.Handle, tag, null, 0) > 2) fails.Add("metadata present: " + tag);

            for (int i = 0; i < pages.Count; i++)
            {
                IntPtr page = PdfiumNative.FPDF_LoadPage(doc.Handle, i);
                if (page == IntPtr.Zero) { fails.Add("page " + (i + 1) + " load"); continue; }
                try
                {
                    string P = "page " + (i + 1) + ": ";
                    if (Math.Abs(PdfiumNative.FPDF_GetPageWidthF(page) - pages[i].Width) > 0.01 || Math.Abs(PdfiumNative.FPDF_GetPageHeightF(page) - pages[i].Height) > 0.01) fails.Add(P + "size");
                    if (PdfiumNative.FPDFPage_GetRotation(page) != 0) fails.Add(P + "rotation");
                    if (PdfiumNative.FPDFPage_GetAnnotCount(page) != 0) fails.Add(P + "annotations");
                    if (PdfiumNative.FPDFPage_CountObjects(page) != 1 ||
                        PdfiumNative.FPDFPageObj_GetType(PdfiumNative.FPDFPage_GetObject(page, 0)) != PdfiumNative.FPDF_PAGEOBJ_IMAGE) fails.Add(P + "objects");
                    IntPtr tp = PdfiumNative.FPDFText_LoadPage(page);
                    if (tp != IntPtr.Zero) { if (PdfiumNative.FPDFText_CountChars(tp) != 0) fails.Add(P + "text"); PdfiumNative.FPDFText_ClosePage(tp); }

                    var onPage = items.Where(x => x.PageIndex == i).ToList();
                    if (onPage.Count == 0) continue;
                    using (var r = Raster.Render(page, 100))
                    {
                        double border = style == RedactionStyle.WhiteWithBorder ? Math.Max(2 / r.Sx, 0.75) + 2.5 : 1.5;
                        foreach (var item in onPage)
                            foreach (var s in item.Shapes)
                                foreach (var pt in InteriorSamples(s, border))
                                {
                                    r.Rgb(pt.X, pt.Y, out int cr, out int cg, out int cb);
                                    bool ok = style == RedactionStyle.Black ? (cr <= 48 && cg <= 48 && cb <= 48) : (cr >= 207 && cg >= 207 && cb >= 207);
                                    if (!ok) { fails.Add(P + "pixel not burned at " + pt.X.ToString("0.#", CultureInfo.InvariantCulture) + "," + pt.Y.ToString("0.#", CultureInfo.InvariantCulture)); break; }
                                }
                    }
                }
                finally { PdfiumNative.FPDF_ClosePage(page); }
            }
            return fails;
        }

        private static IEnumerable<Point2> InteriorSamples(Shape s, double inset)
        {
            if (s is RectShape r)
            {
                if (r.W <= 2 * inset || r.H <= 2 * inset) { yield return new Point2(r.X + r.W / 2, r.Y + r.H / 2); yield break; }
                for (int iy = 0; iy <= 4; iy++)
                    for (int ix = 0; ix <= 6; ix++)
                        yield return new Point2(r.X + inset + (r.W - 2 * inset) * ix / 6.0, r.Y + inset + (r.H - 2 * inset) * iy / 4.0);
            }
            else foreach (var p in ((StrokeShape)s).Points) yield return p;
        }
    }
}
