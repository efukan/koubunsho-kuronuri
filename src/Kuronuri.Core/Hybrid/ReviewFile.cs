using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Kuronuri.Core.Export;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;

namespace Kuronuri.Core.Hybrid
{
    public enum ReviewSaveMethod { Vector, Raster }

    public sealed class ReviewSaveResult
    {
        public ReviewSaveMethod Method;
        public long FileBytes;
    }

    /// <summary>A file looks like a review PDF but its embedded original does not match the recorded hash / length.</summary>
    public sealed class ReviewFileCorruptException : Exception
    {
        public ReviewFileCorruptException() : base("review file is damaged") { }
    }

    /// <summary>Result of reading a review PDF: the embedded original and the restored project.</summary>
    public sealed class ReviewContent
    {
        public ReviewContent(byte[] original, RedactionProject project) { Original = original; Project = project; }
        public byte[] Original { get; }
        public RedactionProject Project { get; }
    }

    /// <summary>
    /// Save / load flow of the review PDF (ARCHITECTURE §9.2, §9.3). Runs on the PDFium worker thread.
    /// Save: vector method → read back from disk (hash, page count, JSON) → on any failure the raster method (§9.4).
    /// The file is written as "&lt;dest&gt;.part" and replaces the destination only after the read-back check passed.
    /// </summary>
    public static class ReviewFile
    {
        public static string Sha256Hex(byte[] data)
        {
            using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        public static string Sha256Hex(Stream data)
        {
            using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        /// <param name="forbiddenPaths">The original PDF (never overwritten). The currently open review PDF may be overwritten.</param>
        /// <param name="forceRaster">Skip the vector method (tests, or a document known to fail import).</param>
        public static ReviewSaveResult Save(byte[] original, RedactionProject project, string appVersion, string destPath,
            IEnumerable<string> forbiddenPaths, HybridOptions? opt = null, bool forceRaster = false)
        {
            opt ??= new HybridOptions();
            SavePathGuard.EnsureAllowed(destPath, forbiddenPaths);
            if (project.Original.Length != original.LongLength || project.Original.Sha256 != Sha256Hex(original))
                throw new InvalidOperationException("project does not describe this original");
            byte[] json = ProjectJson.Serialize(project, appVersion);
            int pageCount;
            using (var src = PdfDoc.FromBytes(original)) pageCount = src.PageCount;

            string part = SavePathGuard.PartPath(destPath);
            bool done = false;
            try
            {
                if (!forceRaster)
                {
                    try
                    {
                        byte[] vec = HybridWriter.Build(original, json, project.Items, opt, project.Layout);
                        WriteFile(part, vec);
                        if (ReadBackOk(part, project, json, pageCount)) return Finish(ReviewSaveMethod.Vector);
                    }
                    catch (Exception e) when (!(e is IOException) && !(e is UnauthorizedAccessException)) { /* fall back below */ }
                }
                using (var src = PdfDoc.FromBytes(original))
                using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    HybridRasterWriter.Build(src, original, json, project.Items, fs, opt: opt, layout: project.Layout);
                    fs.Flush(true);
                }
                if (!ReadBackOk(part, project, json, pageCount)) throw new InvalidOperationException("review file read-back failed");
                return Finish(ReviewSaveMethod.Raster);
            }
            finally
            {
                if (!done) SavePathGuard.TryDelete(part);
            }

            ReviewSaveResult Finish(ReviewSaveMethod method)
            {
                SavePathGuard.ReplaceWithPart(part, destPath);
                done = true;
                return new ReviewSaveResult { Method = method, FileBytes = new FileInfo(destPath).Length };
            }
        }

        private static void WriteFile(string path, byte[] data)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
        }

        private static bool ReadBackOk(string path, RedactionProject project, byte[] json, int pageCount)
        {
            try
            {
                using (var doc = PdfDoc.FromFile(path))
                {
                    if (doc.PageCount != pageCount) return false;
                    if (!HybridReader.TryRead(doc, out var orig, out var js)) return false;
                    try
                    {
                        return orig!.LongLength == project.Original.Length && Sha256Hex(orig) == project.Original.Sha256 && js!.SequenceEqual(json);
                    }
                    finally { Array.Clear(orig!, 0, orig!.Length); }
                }
            }
            catch (PdfLoadException) { return false; }
        }

        /// <summary>
        /// Reads a review PDF from an opened document. Returns null when the document is not a review PDF (it is then
        /// opened as a new original). Throws <see cref="ReviewFileCorruptException"/> when it is one but the embedded
        /// original does not match the recorded hash / length.
        /// </summary>
        public static ReviewContent? TryLoad(PdfDoc doc)
        {
            if (!HybridReader.HasReviewAttachments(doc)) return null;
            if (!HybridReader.TryRead(doc, out var original, out var json)) return null;
            RedactionProject project;
            try { project = ProjectJson.Deserialize(json!); }
            catch (Exception e) when (e is InvalidDataException || e is System.Runtime.Serialization.SerializationException || e is FormatException)
            {
                Array.Clear(original!, 0, original!.Length);
                return null;   // someone else's attachments with the same names: treat the file as an original
            }
            if (original!.LongLength != project.Original.Length || Sha256Hex(original) != project.Original.Sha256)
            {
                Array.Clear(original, 0, original.Length);
                throw new ReviewFileCorruptException();
            }
            // The original must open, and the recorded page geometry must match it.
            using (var src = PdfDoc.FromBytes(original))
            {
                if (src.PageCount != project.Pages.Count) throw new ReviewFileCorruptException();
                if (project.Items.Any(i => i.PageIndex < 0 || i.PageIndex >= src.PageCount)) throw new ReviewFileCorruptException();
            }
            return new ReviewContent(original, project);
        }

        public static string DefaultReviewName(string originalFileName) =>
            Path.GetFileNameWithoutExtension(originalFileName) + "_庁内確認用.pdf";

        public static string DefaultFinalName(string originalFileName) =>
            Path.GetFileNameWithoutExtension(originalFileName) + "_開示用.pdf";
    }
}
