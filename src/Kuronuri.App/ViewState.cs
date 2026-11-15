using System;

namespace Kuronuri.App
{
    public enum FitMode { None, Page, Width }

    /// <summary>
    /// The one view state shared by the left and right panes (ARCHITECTURE §6). Both panes draw from it, so they are in
    /// sync by construction. Zoom 1.0 = 100% (1pt = 96/72 DIP). ScrollX/Y = top-left of the viewport inside the page, in DIP.
    /// </summary>
    public sealed class ViewState
    {
        public const double MinZoom = 0.10, MaxZoom = 8.0;

        private int _pageIndex;
        private double _zoom = 1, _scrollX, _scrollY;
        private bool _peek;

        public event EventHandler? Changed;

        /// <summary>Raised only when the page changes (before <see cref="Changed"/>).</summary>
        public event EventHandler? PageChanged;

        public int PageCount { get; private set; }
        public double PageWidthPt { get; private set; } = 595;
        public double PageHeightPt { get; private set; } = 842;
        public double ViewportW { get; private set; } = 800;
        public double ViewportH { get; private set; } = 600;
        public FitMode Fit { get; private set; } = FitMode.Page;

        public int PageIndex => _pageIndex;
        public double Zoom => _zoom;
        public double ScrollX => _scrollX;
        public double ScrollY => _scrollY;
        public double DipPerPt => _zoom * 96.0 / 72.0;
        public double PageW => PageWidthPt * DipPerPt;
        public double PageH => PageHeightPt * DipPerPt;
        public double MaxScrollX => Math.Max(0, PageW - ViewportW);
        public double MaxScrollY => Math.Max(0, PageH - ViewportH);

        /// <summary>Space held: overlays are drawn at 25% opacity.</summary>
        public bool Peek { get => _peek; set { if (_peek == value) return; _peek = value; Raise(); } }

        private Func<int, (double W, double H)> _pageSize = _ => (595, 842);

        public void Reset(int pageCount, Func<int, (double W, double H)> pageSize)
        {
            PageCount = pageCount;
            _pageSize = pageSize;
            _pageIndex = 0;
            (PageWidthPt, PageHeightPt) = _pageSize(0);
            Fit = FitMode.Page;
            ApplyFit();
            _scrollX = _scrollY = 0;
            PageChanged?.Invoke(this, EventArgs.Empty);
            Raise();
        }

        public void SetViewport(double w, double h)
        {
            if (w <= 0 || h <= 0 || (Math.Abs(w - ViewportW) < 0.5 && Math.Abs(h - ViewportH) < 0.5)) return;
            ViewportW = w; ViewportH = h;
            ApplyFit();
            Clamp();
            Raise();
        }

        /// <param name="toBottom">Show the bottom of the new page (used when scrolling up past the top).</param>
        public void GoToPage(int index, bool toBottom = false)
        {
            if (PageCount == 0) return;
            index = Math.Max(0, Math.Min(PageCount - 1, index));
            bool changed = index != _pageIndex;
            _pageIndex = index;
            (PageWidthPt, PageHeightPt) = _pageSize(index);
            ApplyFit();
            _scrollX = Math.Min(_scrollX, MaxScrollX);
            _scrollY = toBottom ? MaxScrollY : 0;
            Clamp();
            if (changed) PageChanged?.Invoke(this, EventArgs.Empty);
            Raise();
        }

        public void ScrollBy(double dx, double dy) => ScrollTo(_scrollX + dx, _scrollY + dy);

        public void ScrollTo(double x, double y)
        {
            _scrollX = x; _scrollY = y;
            Clamp();
            Raise();
        }

        /// <summary>Centres a point of the page (pt) in the viewport.</summary>
        public void CenterOn(double xPt, double yPt) => ScrollTo(xPt * DipPerPt - ViewportW / 2, yPt * DipPerPt - ViewportH / 2);

        public void SetFit(FitMode fit)
        {
            Fit = fit;
            ApplyFit();
            Clamp();
            Raise();
        }

        /// <summary>Zooms keeping the page point under (vx, vy) (viewport DIP) fixed.</summary>
        public void ZoomAt(double newZoom, double vx, double vy)
        {
            newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));
            // page point under the cursor before the change
            double ox = PageW < ViewportW ? (ViewportW - PageW) / 2 : -_scrollX;
            double oy = PageH < ViewportH ? (ViewportH - PageH) / 2 : -_scrollY;
            double px = (vx - ox) / DipPerPt, py = (vy - oy) / DipPerPt;
            Fit = FitMode.None;
            _zoom = newZoom;
            _scrollX = px * DipPerPt - vx;
            _scrollY = py * DipPerPt - vy;
            Clamp();
            Raise();
        }

        public void SetZoom(double z) => ZoomAt(z, ViewportW / 2, ViewportH / 2);

        private void ApplyFit()
        {
            const double margin = 16;
            double w = Math.Max(50, ViewportW - margin), h = Math.Max(50, ViewportH - margin);
            double k = 96.0 / 72.0;
            if (Fit == FitMode.Page) _zoom = Math.Min(w / (PageWidthPt * k), h / (PageHeightPt * k));
            else if (Fit == FitMode.Width) _zoom = (w - SystemScrollAllowance) / (PageWidthPt * k);
            _zoom = Math.Max(MinZoom, Math.Min(MaxZoom, _zoom));
        }

        private const double SystemScrollAllowance = 0;

        private void Clamp()
        {
            _scrollX = Math.Max(0, Math.Min(MaxScrollX, _scrollX));
            _scrollY = Math.Max(0, Math.Min(MaxScrollY, _scrollY));
        }

        private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
