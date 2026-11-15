using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuronuri.Core.Pdfium;

namespace Kuronuri.Core.Rendering
{
    /// <summary>
    /// Renders page bitmaps for the screen on the PDFium worker and caches them. Every bitmap is frozen so it can be
    /// used on the UI thread. Requests for pages that scrolled away are dropped through the cancellation token.
    /// </summary>
    public sealed class PageRenderService : IDisposable
    {
        /// <summary>Above this, only the visible region (plus margin) is rendered (ARCHITECTURE §6).</summary>
        public const long FullRenderLimitPixels = 16_000_000;

        /// <summary>WIC bitmaps are only freed by the GC; collect once this many bytes have left the cache.</summary>
        public const long CollectAfterEvictedBytes = 32L << 20;

        private readonly PdfiumWorker _worker;
        private readonly PdfDoc _doc;
        private readonly NativeBuffer _buf = new NativeBuffer();   // used on the worker thread only
        private bool _disposed;

        public PageRenderService(PdfiumWorker worker, PdfDoc doc, PageBitmapCache pageCache, PageBitmapCache thumbCache)
        {
            _worker = worker; _doc = doc; Pages = pageCache; Thumbs = thumbCache;
        }

        public PageBitmapCache Pages { get; }
        public PageBitmapCache Thumbs { get; }

        public Task<BitmapSource> RenderAsync(RenderKey key, CancellationToken token, bool thumbnail = false)
        {
            var cache = thumbnail ? Thumbs : Pages;
            var hit = cache.Get(key);
            if (hit != null) return Task.FromResult(hit);
            return _worker.Run(() =>
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PageRenderService));
                token.ThrowIfCancellationRequested();
                var again = cache.Get(key);
                if (again != null) return again;
                var bmp = RenderNow(key);
                cache.Put(key, bmp);
                if (cache.TakeEvicted(CollectAfterEvictedBytes)) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                return bmp;
            }, token);
        }

        /// <summary>Worker thread only.</summary>
        private BitmapSource RenderNow(RenderKey key)
        {
            using (var page = _doc.LoadPage(key.Page))
            {
                _buf.Ensure(key.Bytes);
                PageRenderer.RenderRegion(page.Handle, _buf.Ptr, key.FullW, key.FullH, key.X, key.Y, key.W, key.H);
                var bmp = BitmapSource.Create(key.W, key.H, 96, 96, PixelFormats.Bgr32, null, _buf.Ptr, checked((int)key.Bytes), key.W * 4);
                bmp.Freeze();
                return bmp;
            }
        }

        /// <summary>Must be called on the worker thread or after the worker queue is idle; clears the pixel buffer.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _worker.Run(() => _buf.Dispose());
        }
    }
}
