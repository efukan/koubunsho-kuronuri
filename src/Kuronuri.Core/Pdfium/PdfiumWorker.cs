using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Kuronuri.Core.Pdfium
{
    /// <summary>
    /// PDFium is not thread-safe. Every P/Invoke into pdfium.dll runs on the single thread owned by this class.
    /// The thread is STA (the PoC ran on STA; WIC / FormattedText objects created here stay on this thread unless frozen).
    /// Jobs run in FIFO order. A job whose token is cancelled before it starts is skipped (stale render requests).
    /// </summary>
    public sealed class PdfiumWorker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly Thread _thread;
        private Exception? _initError;

        public PdfiumWorker()
        {
            using (var ready = new ManualResetEventSlim(false))
            {
                _thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "PDFium" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                ready.Wait();
            }
            if (_initError != null) throw new InvalidOperationException("PDFium initialization failed", _initError);
        }

        /// <summary>True when called from the worker thread.</summary>
        public bool IsWorkerThread => Thread.CurrentThread == _thread;

        private void Loop(ManualResetEventSlim ready)
        {
            try { PdfiumLibrary.Load(); }
            catch (Exception e) { _initError = e; }
            ready.Set();
            if (_initError != null) return;
            foreach (var job in _queue.GetConsumingEnumerable()) job();
        }

        public Task<T> Run<T>(Func<T> func, CancellationToken token = default)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (token.IsCancellationRequested) { tcs.SetCanceled(); return tcs.Task; }
            if (IsWorkerThread)
            {
                // Re-entrant call from inside a job: run inline (queuing would deadlock when awaited synchronously).
                try { tcs.SetResult(func()); } catch (Exception e) { tcs.SetException(e); }
                return tcs.Task;
            }
            Action job = () =>
            {
                if (token.IsCancellationRequested) { tcs.TrySetCanceled(); return; }
                try { tcs.TrySetResult(func()); }
                catch (OperationCanceledException) { tcs.TrySetCanceled(); }
                catch (Exception e) { tcs.TrySetException(e); }
            };
            try { _queue.Add(job); }
            catch (InvalidOperationException) { tcs.TrySetException(new ObjectDisposedException(nameof(PdfiumWorker))); }
            return tcs.Task;
        }

        public Task Run(Action action, CancellationToken token = default) => Run(() => { action(); return true; }, token);

        /// <summary>Synchronous convenience for callers that may block (printing, tests). Never call from the worker's own job with a different worker.</summary>
        public T Invoke<T>(Func<T> func) => Run(func).GetAwaiter().GetResult();

        public void Invoke(Action action) => Run(action).GetAwaiter().GetResult();

        public void Dispose()
        {
            if (_queue.IsAddingCompleted) return;
            _queue.CompleteAdding();
            if (!IsWorkerThread) _thread.Join(5000);
        }
    }
}
