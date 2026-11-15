using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kuronuri.App
{
    public enum InfoSeverity { Success, Info, Warning }

    /// <summary>
    /// In-window notification (ARCHITECTURE §11.8): severity icon, title, message, one optional action, close button.
    /// Success / info close themselves after 8 seconds; warnings stay until closed. A new notice replaces the old one.
    /// Built in code; colours come from the theme palette.
    /// </summary>
    public sealed class InfoBar : Border
    {
        private readonly TextBlock _icon, _title, _message;
        private readonly Button _action, _close;
        private readonly DispatcherTimer _timer;
        private Action? _onAction;
        private bool _autoClose;

        public InfoBar()
        {
            CornerRadius = new CornerRadius(6);
            BorderThickness = new Thickness(1);
            SetResourceReference(BorderBrushProperty, "Stroke.Control");
            Padding = new Thickness(14, 10, 8, 10);
            Visibility = Visibility.Collapsed;
            MaxWidth = 760;

            var iconFont = (FontFamily)Application.Current.FindResource("IconFont");
            _icon = new TextBlock { FontFamily = iconFont, FontSize = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
            _title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
            _title.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
            _message = new TextBlock { TextWrapping = TextWrapping.Wrap };
            _message.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
            _action = new Button { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            _action.Click += (s, e) => { var a = _onAction; Hide(); a?.Invoke(); };
            _close = new Button { Content = "", FontFamily = iconFont, FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = "閉じる" };
            _close.SetResourceReference(StyleProperty, "SubtleButton");
            _close.Click += (s, e) => Hide();

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_title);
            text.Children.Add(_message);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(text, 1); Grid.SetColumn(_action, 2); Grid.SetColumn(_close, 3);
            grid.Children.Add(_icon); grid.Children.Add(text); grid.Children.Add(_action); grid.Children.Add(_close);
            Child = grid;

            _timer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Normal, (s, e) => Hide(), Dispatcher);
            _timer.Stop();
        }

        public void Show(InfoSeverity severity, string title, string message = "", string? actionLabel = null, Action? action = null)
        {
            _timer.Stop();
            string key = severity == InfoSeverity.Success ? "Success" : severity == InfoSeverity.Warning ? "Warning" : "Info";
            SetResourceReference(BackgroundProperty, "InfoBar." + key + ".Bg");
            _icon.SetResourceReference(TextBlock.ForegroundProperty, "InfoBar." + key + ".Icon");
            _icon.Text = severity == InfoSeverity.Success ? "" : severity == InfoSeverity.Warning ? "" : "";
            _title.Text = title;
            _message.Text = message;
            _message.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _onAction = action;
            _action.Content = actionLabel ?? "";
            _action.Visibility = actionLabel != null && action != null ? Visibility.Visible : Visibility.Collapsed;
            Visibility = Visibility.Visible;
            _autoClose = severity != InfoSeverity.Warning && _action.Visibility != Visibility.Visible;   // notices with a button stay until used or closed
            if (_autoClose) _timer.Start();
        }

        public void Hide()
        {
            _timer.Stop();
            _onAction = null;
            Visibility = Visibility.Collapsed;
        }

        /// <summary>Keeps the notice while the pointer is over it (so it can be read and its button pressed).</summary>
        protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e) { base.OnMouseEnter(e); _timer.Stop(); }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (Visibility == Visibility.Visible && _autoClose) _timer.Start();
        }
    }
}
