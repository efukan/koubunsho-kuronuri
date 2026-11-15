using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kuronuri.Core.Settings;

namespace Kuronuri.App
{
    /// <summary>Settings and reason-preset editor (ARCHITECTURE §12). Built in code; no XAML.</summary>
    public sealed class SettingsWindow : Window
    {
        public sealed class PresetRow
        {
            public string Code { get; set; } = "";
            public string Label { get; set; } = "";
        }

        private readonly ObservableCollection<PresetRow> _rows;
        private readonly ComboBox _dpi, _printDpi, _style;
        private readonly TextBox _quality, _widths;

        public AppSettings Result { get; private set; }

        public SettingsWindow(AppSettings current)
        {
            Result = current;
            Title = "設定";
            Width = 520; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI, Meiryo, MS Gothic");

            _rows = new ObservableCollection<PresetRow>(current.Reasons.Select(r => new PresetRow { Code = r.Code, Label = r.Label }));
            var grid = new DataGrid
            {
                ItemsSource = _rows, AutoGenerateColumns = false, CanUserAddRows = true, CanUserDeleteRows = true,
                HeadersVisibility = DataGridHeadersVisibility.Column, Height = 220, Margin = new Thickness(0, 4, 0, 0),
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "号番号（code）", Binding = new System.Windows.Data.Binding(nameof(PresetRow.Code)), Width = 120 });
            grid.Columns.Add(new DataGridTextColumn { Header = "名称（label）", Binding = new System.Windows.Data.Binding(nameof(PresetRow.Label)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

            _dpi = Combo(AppSettings.ExportDpiChoices.Select(d => d + " dpi").ToArray(), Array.IndexOf(AppSettings.ExportDpiChoices, current.ExportDpi));
            _printDpi = Combo(AppSettings.PrintDpiChoices.Select(d => d + " dpi").ToArray(), Array.IndexOf(AppSettings.PrintDpiChoices, current.PrintDpi));
            _style = Combo(new[] { "黒塗り", "白塗り+黒枠" }, current.DefaultStyle == "whiteBorder" ? 1 : 0);
            _quality = new TextBox { Text = current.JpegQuality.ToString(CultureInfo.InvariantCulture), Width = 60, HorizontalAlignment = HorizontalAlignment.Left };
            _widths = new TextBox { Text = string.Join(", ", current.BrushWidthsMm.Select(w => w.ToString("0.#", CultureInfo.InvariantCulture))), Width = 200, HorizontalAlignment = HorizontalAlignment.Left };

            var form = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            form.ColumnDefinitions.Add(new ColumnDefinition());
            AddRow(form, "確定出力の解像度", _dpi);
            AddRow(form, "JPEG 品質（50〜100）", _quality);
            AddRow(form, "既定のスタイル", _style);
            AddRow(form, "ブラシの太さ（mm, カンマ区切り）", _widths);
            AddRow(form, "印刷の解像度", _printDpi);

            var ok = new Button { Content = "OK", Width = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (s, e) => { if (Apply()) DialogResult = true; };
            var cancel = new Button { Content = "キャンセル", Width = 88, IsCancel = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock { Text = "理由のプリセット（号番号は自治体の条例に合わせて編集してください）", TextWrapping = TextWrapping.Wrap });
            root.Children.Add(grid);
            root.Children.Add(form);
            root.Children.Add(new TextBlock
            {
                Text = "設定は EXE と同じフォルダの settings.json に保存されます。文書に関する情報は保存しません。",
                TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 10, 0, 0),
            });
            root.Children.Add(buttons);
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private static ComboBox Combo(string[] items, int selected)
        {
            var c = new ComboBox { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var i in items) c.Items.Add(i);
            c.SelectedIndex = Math.Max(0, selected);
            return c;
        }

        private static void AddRow(Grid g, string label, FrameworkElement control)
        {
            int r = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
            Grid.SetRow(t, r); Grid.SetRow(control, r); Grid.SetColumn(control, 1);
            control.Margin = new Thickness(0, 4, 0, 4);
            g.Children.Add(t); g.Children.Add(control);
        }

        private bool Apply()
        {
            if (!int.TryParse(_quality.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int q) || q < 50 || q > 100)
            {
                MessageBox.Show(this, "JPEG 品質は 50〜100 の数で指定してください。", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            var widths = _widths.Text.Split(new[] { ',', '、', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN).ToList();
            if (widths.Count == 0 || widths.Any(w => double.IsNaN(w) || w < 0.5 || w > 50))
            {
                MessageBox.Show(this, "ブラシの太さは 0.5〜50 (mm) の数をカンマ区切りで指定してください。", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            var presets = _rows.Where(r => !string.IsNullOrWhiteSpace(r.Code)).Select(r => new ReasonPreset(r.Code.Trim(), (r.Label ?? "").Trim())).ToList();
            if (presets.Select(p => p.Code).Distinct().Count() != presets.Count)
            {
                MessageBox.Show(this, "号番号が重複しています。", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            Result = new AppSettings
            {
                Reasons = presets,
                ExportDpi = AppSettings.ExportDpiChoices[Math.Max(0, _dpi.SelectedIndex)],
                JpegQuality = q,
                DefaultStyle = _style.SelectedIndex == 1 ? "whiteBorder" : "black",
                BrushWidthsMm = widths,
                PrintDpi = AppSettings.PrintDpiChoices[Math.Max(0, _printDpi.SelectedIndex)],
            }.Normalize();
            return true;
        }
    }
}
