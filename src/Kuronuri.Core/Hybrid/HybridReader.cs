using System;
using System.Runtime.InteropServices;
using System.Text;
using Kuronuri.Core.Pdfium;
using N = Kuronuri.Core.Pdfium.PdfiumNative;

namespace Kuronuri.Core.Hybrid
{
    public static class HybridReader
    {
        /// <summary>Extracts the embedded original and project JSON. Returns false when the PDF is not a review file.</summary>
        public static bool TryRead(byte[] pdf, out byte[]? original, out byte[]? projectJson)
        {
            using (var doc = PdfDoc.FromBytes(pdf)) return TryRead(doc, out original, out projectJson);
        }

        /// <summary>Same as above on an already opened document (e.g. one opened from a file stream).</summary>
        public static bool TryRead(PdfDoc doc, out byte[]? original, out byte[]? projectJson)
        {
            original = null; projectJson = null;
            int n = N.FPDFDoc_GetAttachmentCount(doc.Handle);
            for (int i = 0; i < n; i++)
            {
                IntPtr att = N.FPDFDoc_GetAttachment(doc.Handle, i);
                if (att == IntPtr.Zero) continue;
                string name = Name(att);
                if (name == HybridWriter.OriginalName && original == null) original = Data(att);
                else if (name == HybridWriter.ProjectName && projectJson == null) projectJson = Data(att);
            }
            return original != null && projectJson != null;
        }

        /// <summary>True when the document has any attachment with one of the two review-file names (cheap check, no data read).</summary>
        public static bool HasReviewAttachments(PdfDoc doc)
        {
            bool o = false, j = false;
            int n = N.FPDFDoc_GetAttachmentCount(doc.Handle);
            for (int i = 0; i < n; i++)
            {
                IntPtr att = N.FPDFDoc_GetAttachment(doc.Handle, i);
                if (att == IntPtr.Zero) continue;
                string name = Name(att);
                o |= name == HybridWriter.OriginalName;
                j |= name == HybridWriter.ProjectName;
            }
            return o && j;
        }

        private static string Name(IntPtr att)
        {
            uint len = N.FPDFAttachment_GetName(att, null, 0);
            if (len < 2) return "";
            var b = new byte[len];
            N.FPDFAttachment_GetName(att, b, len);
            return Encoding.Unicode.GetString(b, 0, (int)len - 2);
        }

        private static byte[] Data(IntPtr att)
        {
            if (!N.FPDFAttachment_GetFile(att, IntPtr.Zero, 0, out uint len)) return new byte[0];
            var b = new byte[len];
            if (len == 0) return b;
            var h = GCHandle.Alloc(b, GCHandleType.Pinned);
            try { if (!N.FPDFAttachment_GetFile(att, h.AddrOfPinnedObject(), len, out _)) throw new InvalidOperationException("FPDFAttachment_GetFile failed"); }
            finally { h.Free(); }
            return b;
        }
    }
}
