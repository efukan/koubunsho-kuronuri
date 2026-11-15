using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Kuronuri.Core.Export
{
    /// <summary>
    /// Minimal streaming PDF writer. Every object is written on the spot; only the offsets are kept in memory.
    /// Dictionaries are always written on a single line (the verifier relies on this).
    /// </summary>
    public sealed class MiniPdfWriter
    {
        public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        public const string Header = "%PDF-1.7\n%âãÏÓ\n";   // 15 bytes

        private readonly Stream _out;
        private readonly List<long> _offsets = new List<long> { 0 };   // index = object number; [0] is the free entry
        private long _pos;

        public MiniPdfWriter(Stream output) { _out = output; Raw(Header); }

        public int ObjectCount => _offsets.Count - 1;

        public static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>Allocates the next object number.</summary>
        public int Reserve() { _offsets.Add(-1); return _offsets.Count - 1; }

        public void WriteObject(int num, string dict)
        {
            Begin(num);
            Raw(num.ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + dict + "\nendobj\n");
        }

        /// <param name="dictEntries">Entries without the surrounding brackets and without /Length (may be empty).</param>
        public void WriteStream(int num, string dictEntries, byte[] data, int offset, int count)
        {
            Begin(num);
            string head = dictEntries.Length == 0 ? "" : dictEntries + " ";
            Raw(num.ToString(CultureInfo.InvariantCulture) + " 0 obj\n<< " + head + "/Length " + count.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
            _out.Write(data, offset, count); _pos += count;
            Raw("\nendstream\nendobj\n");
        }

        public void WriteStream(int num, string dictEntries, byte[] data) => WriteStream(num, dictEntries, data, 0, data.Length);

        /// <summary>Writes xref, trailer and EOF. extraTrailer is appended inside the trailer dictionary (e.g. " /Info 5 0 R").</summary>
        public void Finish(int rootNum, string extraTrailer = "")
        {
            long xref = _pos;
            var sb = new StringBuilder();
            sb.Append("xref\n0 ").Append(_offsets.Count).Append("\n0000000000 65535 f \n");
            for (int i = 1; i < _offsets.Count; i++)
            {
                if (_offsets[i] < 0) throw new InvalidOperationException("object " + i + " was reserved but never written");
                sb.Append(_offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");   // exactly 20 bytes
            }
            sb.Append("trailer\n<< /Size ").Append(_offsets.Count).Append(" /Root ").Append(rootNum).Append(" 0 R").Append(extraTrailer)
              .Append(" >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            Raw(sb.ToString());
            _out.Flush();
        }

        private void Begin(int num)
        {
            if (_offsets[num] >= 0) throw new InvalidOperationException("object " + num + " written twice");
            _offsets[num] = _pos;
        }

        private void Raw(string s) { byte[] b = Latin1.GetBytes(s); _out.Write(b, 0, b.Length); _pos += b.Length; }

        /// <summary>zlib (RFC 1950) wrapper around DeflateStream, for /FlateDecode. net48 has no ZLibStream.</summary>
        public static byte[] ZlibCompress(byte[] data, CompressionLevel level = CompressionLevel.Optimal) => ZlibCompress(data, 0, data.Length, level);

        public static byte[] ZlibCompress(byte[] data, int offset, int count, CompressionLevel level)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x9C);
                using (var ds = new DeflateStream(ms, level, true)) ds.Write(data, offset, count);
                uint a = 1, b = 0;
                for (int i = offset; i < offset + count; i++) { a = (a + data[i]) % 65521; b = (b + a) % 65521; }
                uint adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }
    }
}
