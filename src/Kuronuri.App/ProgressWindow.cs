using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Kuronuri.App
{
    /// <summary>Modal progress dialog (built in code; no XAML). Closing is only possible when the task finishes or via Cancel.</summary>
    public sealed class ProgressWindow : Window
    {
        private readonly ProgressBar _bar;
        private readonly TextBlock _text;
        private readonly Button _cancel;
        private readonly int? _total;
        private bool _done;

        public event EventHandler? CancelRequested;

        public ProgressWindow(string title, int? total)
        {
            _total = total;
            Title = "公文書くろぬり";
            Width = 420; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("UiFont");
            FontSize = 13;
            Themes.ThemeManager.Attach(this);
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 8), FontSize = 13 });
            _bar = new ProgressBar { Height = 4, IsIndeterminate = total == null, Maximum = Math.Max(1, total ?? 1) };
            panel.Children.Add(_bar);
            _text = new TextBlock { Margin = new Thickness(0, 6, 0, 0), Text = total == null ? "" : "0 / " + total + " ページ" };
            panel.Children.Add(_text);
            _cancel = new Button { Content = "中止", Width = 88, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Visibility = total == null ? Visibility.Collapsed : Visibility.Visible };
            _cancel.Click += (s, e) => { _cancel.IsEnabled = false; _text.Text = "中止しています…"; CancelRequested?.Invoke(this, EventArgs.Empty); };
            panel.Children.Add(_cancel);
            Content = panel;
        }

        public void SetProgress(int done)
        {
            if (_total == null || !_cancel.IsEnabled) return;
            _bar.Value = done;
            _text.Text = done + " / " + _total + " ページ";
        }

        public void CloseFromTask()
        {
            _done = true;
            if (IsLoaded) Close();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (_done) Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_done)
            {
                e.Cancel = true;
                if (_total != null && _cancel.IsEnabled) _cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            base.OnClosing(e);
        }
    }
}
