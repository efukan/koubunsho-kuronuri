using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kuronuri.Core.Editing;
using Kuronuri.Core.Hybrid;
using Kuronuri.Core.Model;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Rendering;

namespace Kuronuri.App
{
    public enum SourceKind { Original, Review }

    /// <summary>Things the user should be told once after opening (ARCHITECTURE §14).</summary>
    public sealed class OpenWarnings
    {
        public bool Xfa;
        public bool HasAttachments;
        public bool HasBookmarks;
    }

    /// <summary>
    /// One opened document: the PDFium handle of the original (read-only), its page geometry, the redaction items with
    /// their undo history, the style and the selection. All PDFium access goes through <see cref="Worker"/>.
    /// </summary>
    public sealed class DocumentSession : IDisposable
    {
        /// <summary>Application version (Directory.Build.props), recorded in the review PDF's project JSON.</summary>
        public static readonly string AppVersion = typeof(DocumentSession).Assembly.GetName().Version is { } v ? v.Major + "." + v.Minor + "." + v.Build : "0.0.0";
        public const long LargeOriginalBytes = 300L * 1024 * 1024;

        private DocumentSession(PdfiumWorker worker, PdfDoc doc, SourceKind kind, string path, byte[]? originalBytes,
            OriginalInfo original, List<PageInfo> pages, RedactionStyle style)
        {
            Worker = worker; Doc = doc; Kind = kind; OriginalBytes = originalBytes; Original = original; Pages = pages;
            Style = style; _savedStyle = style;
            if (kind == SourceKind.Original) OriginalPath = path; else ReviewPath = path;
            Undo = new UndoStack(Items);
        }

        public PdfiumWorker Worker { get; }
        public PdfDoc Doc { get; }
        public SourceKind Kind { get; }

        /// <summary>The original PDF on disk (null when the document came from a review PDF). Never written.</summary>
        public string? OriginalPath { get; }

        /// <summary>The review PDF this session was loaded from or last saved to ("上書き保存" target).</summary>
        public string? ReviewPath { get; set; }

        /// <summary>The original bytes when they came from a review PDF (zero-cleared on Dispose).</summary>
        public byte[]? OriginalBytes { get; }

        public OriginalInfo Original { get; }
        public IReadOnlyList<PageInfo> Pages { get; }
        public ItemList Items { get; } = new ItemList();
        public UndoStack Undo { get; }
        public FreehandGrouper Grouper { get; } = new FreehandGrouper();
        public OpenWarnings Warnings { get; private set; } = new OpenWarnings();
        public PageRenderService? Renderer { get; set; }

        private RedactionStyle _style, _savedStyle;
        public RedactionStyle Style
        {
            get => _style;
            set { if (_style == value) return; _style = value; StyleChanged?.Invoke(this, EventArgs.Empty); }
        }
        public event EventHandler? StyleChanged;

        public bool IsModified => Undo.IsModified || _style != _savedStyle;

        public void MarkSaved() { _savedStyle = _style; Undo.MarkSaved(); }

        /// <summary>Display name used in the title bar and default output names.</summary>
        public string DisplayFileName => Original.FileName;

        public string? Folder => Path.GetDirectoryName(OriginalPath ?? ReviewPath ?? "");

        /// <summary>Files that must never be the destination of any save.</summary>
        public IEnumerable<string> ProtectedFiles => new[] { OriginalPath }.Where(p => p != null).Select(p => p!);

        /// <summary>Destinations a final output must never overwrite: the original and the open review PDF.</summary>
        public IEnumerable<string> ProtectedFilesForFinal => new[] { OriginalPath, ReviewPath }.Where(p => p != null).Select(p => p!);

        public RedactionProject ToProject() => new RedactionProject(Original, Pages, Items.Items.ToList(), Style, Layout.IsIdentity ? null : Layout);

        // ------------------------------------------------------------------ page layout (ARCHITECTURE §4.4)

        /// <summary>Order / rotation / exclusion. Items keep original page numbers and base coordinates.</summary>
        public PageLayout Layout => Items.Layout ?? PageLayout.Identity(Pages.Count);

        public int BaseOf(int view) => Layout.BaseOf(view);
        public int ViewOf(int basePage) => Layout.ViewOf(basePage);
        public int RotationOfView(int view) => Layout.RotationOf(Layout.BaseOf(view));

        /// <summary>Size of the page at view position <paramref name="view"/> as it is displayed (user rotation applied).</summary>
        public (double W, double H) ViewSize(int view)
        {
            int b = Layout.BaseOf(view);
            return Kuronuri.Core.Geometry.CoordMap.ViewSize(Layout.RotationOf(b), Pages[b].Width, Pages[b].Height);
        }

        // ------------------------------------------------------------------ selection

        private HashSet<Guid> _selected = new HashSet<Guid>();
        public IReadOnlyCollection<Guid> SelectedIds => _selected;
        public event EventHandler? SelectionChanged;

        public IReadOnlyList<RedactionItem> SelectedItems =>
            Items.Items.Where(i => _selected.Contains(i.Id)).ToList();

        public bool IsSelected(Guid id) => _selected.Contains(id);

        public void Select(IEnumerable<Guid> ids)
        {
            var next = new HashSet<Guid>(ids.Where(id => Items.IndexOf(id) >= 0));
            if (next.SetEquals(_selected)) return;
            _selected = next;
            Grouper.Close();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Drops ids that no longer exist (after undo / delete).</summary>
        public void PruneSelection()
        {
            if (_selected.All(id => Items.IndexOf(id) >= 0)) return;
            _selected = new HashSet<Guid>(_selected.Where(id => Items.IndexOf(id) >= 0));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // ------------------------------------------------------------------ loading (worker thread)

        /// <summary>Worker thread. Opens a PDF: a review PDF restores its embedded original and items, anything else is a new original.</summary>
        public static DocumentSession Load(PdfiumWorker worker, string path, RedactionStyle defaultStyle = RedactionStyle.Black)
        {
            if (!worker.IsWorkerThread) throw new InvalidOperationException("must run on the PDFium worker");
            var file = PdfDoc.FromFile(path);
            try
            {
                ReviewContent? review = ReviewFile.TryLoad(file);
                if (review != null)
                {
                    file.Dispose();
                    var doc = PdfDoc.FromBytes(review.Original, clearOnDispose: true);
                    try
                    {
                        var pages = ReadPages(doc);
                        var s = new DocumentSession(worker, doc, SourceKind.Review, path, review.Original, review.Project.Original, pages, review.Project.Style);
                        s.Items.Reset(review.Project.Items);
                        s.Items.ResetLayout(review.Project.EffectiveLayout);
                        s.Undo.Clear();
                        s.Warnings = ReadWarnings(doc);
                        return s;
                    }
                    catch { doc.Dispose(); throw; }
                }
                var pagesO = ReadPages(file);
                string sha;
                long len;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
                {
                    len = fs.Length;
                    sha = ReviewFile.Sha256Hex(fs);
                }
                var session = new DocumentSession(worker, file, SourceKind.Original, path, null,
                    new OriginalInfo(sha, len, Path.GetFileName(path)), pagesO, defaultStyle);
                session.Items.ResetLayout(PageLayout.Identity(pagesO.Count));
                session.Undo.Clear();
                session.Warnings = ReadWarnings(file);
                return session;
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }

        private static List<PageInfo> ReadPages(PdfDoc doc)
        {
            int n = doc.PageCount;
            if (n <= 0) throw new PdfLoadException(PdfErrorKind.NoPages);
            var pages = new List<PageInfo>(n);
            for (int i = 0; i < n; i++)
                using (var p = doc.LoadPage(i))
                    pages.Add(new PageInfo(p.Width, p.Height, PdfiumNative.FPDFPage_GetRotation(p.Handle) * 90));
            return pages;
        }

        private static OpenWarnings ReadWarnings(PdfDoc doc)
        {
            int form = PdfiumNative.FPDF_GetFormType(doc.Handle);
            return new OpenWarnings
            {
                Xfa = form == PdfiumNative.FORMTYPE_XFA_FULL || form == PdfiumNative.FORMTYPE_XFA_FOREGROUND,
                HasAttachments = PdfiumNative.FPDFDoc_GetAttachmentCount(doc.Handle) > 0,
                HasBookmarks = PdfiumNative.FPDFBookmark_GetFirstChild(doc.Handle, IntPtr.Zero) != IntPtr.Zero,
            };
        }

        /// <summary>Worker thread. The original bytes for embedding into a review PDF, checked against the hash taken at open time.</summary>
        public byte[] ReadOriginalBytes()
        {
            if (OriginalBytes != null) return OriginalBytes;
            byte[] data;
            using (var fs = new FileStream(OriginalPath!, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fs.Length != Original.Length) throw new OriginalChangedException();
                data = new byte[fs.Length];
                int done = 0;
                while (done < data.Length) { int n = fs.Read(data, done, data.Length - done); if (n <= 0) break; done += n; }
                if (done != data.Length) throw new OriginalChangedException();
            }
            if (ReviewFile.Sha256Hex(data) != Original.Sha256) { Array.Clear(data, 0, data.Length); throw new OriginalChangedException(); }
            return data;
        }

        // ------------------------------------------------------------------ page text (ARCHITECTURE §11.5; memory only)

        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Kuronuri.Core.Text.PageText> _texts =
            new System.Collections.Concurrent.ConcurrentDictionary<int, Kuronuri.Core.Text.PageText>();

        public Kuronuri.Core.Text.PageText? CachedText(int page) => _texts.TryGetValue(page, out var t) ? t : null;

        /// <summary>Worker thread: the page text, read once and kept for the lifetime of the session.</summary>
        public Kuronuri.Core.Text.PageText ReadText(int page) => _texts.GetOrAdd(page, p => Kuronuri.Core.Text.PageText.Read(Doc, p));

        public System.Threading.Tasks.Task<Kuronuri.Core.Text.PageText> GetTextAsync(int page)
        {
            var t = CachedText(page);
            return t != null ? System.Threading.Tasks.Task.FromResult(t) : Worker.Run(() => ReadText(page));
        }

        public void Dispose()
        {
            _texts.Clear();
            var renderer = Renderer;
            Renderer = null;
            Worker.Run(() =>
            {
                renderer?.Dispose();
                Doc.Dispose();   // also zero-clears OriginalBytes when they came from a review PDF
            });
        }
    }

    /// <summary>The original file on disk is no longer the file that was opened.</summary>
    public sealed class OriginalChangedException : Exception
    {
        public OriginalChangedException() : base("original changed on disk") { }
    }
}
