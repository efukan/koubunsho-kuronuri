using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Kuronuri.Core.Export;

namespace Kuronuri.Core.Tests
{
    /// <summary>
    /// Builds a one-page PDF protected with the standard security handler (RC4 40-bit, R2). With an empty user password
    /// it is a "permissions only" PDF that opens without a password; with a user password PDFium must refuse it.
    /// </summary>
    public static class EncryptedPdf
    {
        public const string Text = "Hello";

        private static readonly byte[] Pad =
        {
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
        };

        public static byte[] Build(string userPassword = "user", string ownerPassword = "owner")
        {
            const int P = -44;   // printing etc. not allowed: a typical "permissions only" document
            byte[] id = Enumerable.Range(1, 16).Select(i => (byte)(i * 7)).ToArray();
            byte[] ownerKey = Md5(Padded(ownerPassword)).Take(5).ToArray();
            byte[] o = Rc4(ownerKey, Padded(userPassword));
            byte[] key = Md5(Padded(userPassword).Concat(o).Concat(BitConverter.GetBytes(P)).Concat(id).ToArray()).Take(5).ToArray();
            byte[] u = Rc4(key, Pad);

            var ms = new MemoryStream();
            var w = new MiniPdfWriter(ms);
            int catalog = w.Reserve(), pages = w.Reserve(), font = w.Reserve(), cont = w.Reserve(), page = w.Reserve(), enc = w.Reserve();
            w.WriteObject(font, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            byte[] content = Encoding.ASCII.GetBytes("BT /F1 24 Tf 72 700 Td (" + Text + ") Tj ET");
            w.WriteStream(cont, "", Rc4(ObjectKey(key, cont), content));
            w.WriteObject(page, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 " + font + " 0 R >> >> /Contents " + cont + " 0 R >>");
            w.WriteObject(pages, "<< /Type /Pages /Count 1 /Kids [" + page + " 0 R] >>");
            w.WriteObject(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
            w.WriteObject(enc, "<< /Filter /Standard /V 1 /R 2 /Length 40 /O <" + Hex(o) + "> /U <" + Hex(u) + "> /P " + P + " >>");
            w.Finish(catalog, " /Encrypt " + enc + " 0 R /ID [<" + Hex(id) + "> <" + Hex(id) + ">]");
            return ms.ToArray();
        }

        private static byte[] ObjectKey(byte[] key, int num) =>
            Md5(key.Concat(new[] { (byte)num, (byte)(num >> 8), (byte)(num >> 16), (byte)0, (byte)0 }).ToArray()).Take(Math.Min(16, key.Length + 5)).ToArray();

        private static byte[] Padded(string pw)
        {
            byte[] b = Encoding.ASCII.GetBytes(pw);
            return b.Concat(Pad).Take(32).ToArray();
        }

        private static byte[] Md5(byte[] d) { using (var m = MD5.Create()) return m.ComputeHash(d); }

        private static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-", "");

        private static byte[] Rc4(byte[] key, byte[] data)
        {
            var s = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 255;
                (s[i], s[j]) = (s[j], s[i]);
            }
            var r = new byte[data.Length];
            for (int k = 0, i = 0, j = 0; k < data.Length; k++)
            {
                i = (i + 1) & 255; j = (j + s[i]) & 255;
                (s[i], s[j]) = (s[j], s[i]);
                r[k] = (byte)(data[k] ^ s[(s[i] + s[j]) & 255]);
            }
            return r;
        }
    }
}
