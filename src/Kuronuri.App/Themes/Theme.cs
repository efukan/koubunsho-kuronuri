using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Kuronuri.App.Themes
{
    /// <summary>
    /// Light / dark switching (ARCHITECTURE §11.7). The first merged dictionary of the application resources is the
    /// palette; replacing it re-colours everything that uses DynamicResource.
    /// </summary>
    public static class ThemeManager
    {
        public const string Light = "light", Dark = "dark";

        public static string Current { get; private set; } = Light;

        public static event EventHandler? Changed;

        public static void Apply(string theme)
        {
            theme = theme == Dark ? Dark : Light;
            var app = Application.Current;
            var palette = new ResourceDictionary { Source = new Uri("pack://application:,,,/Kuronuri;component/Themes/" + (theme == Dark ? "Dark" : "Light") + ".xaml") };
            if (app.Resources.MergedDictionaries.Count > 0) app.Resources.MergedDictionaries[0] = palette;
            else app.Resources.MergedDictionaries.Add(palette);
            Current = theme;
            foreach (Window w in app.Windows) ApplyTitleBar(w);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        [DllImport("dwmapi", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19, DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        /// <summary>Dark title bar in dark mode (Windows 10 2004+ / 11). Silently does nothing elsewhere.</summary>
        public static void ApplyTitleBar(Window w)
        {
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int on = Current == Dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        /// <summary>Call from a window constructor: applies the title bar colour once the handle exists.</summary>
        public static void Attach(Window w)
        {
            w.SourceInitialized += (s, e) => ApplyTitleBar(w);
            w.SetResourceReference(Control.BackgroundProperty, "Bg.Window");
            w.SetResourceReference(Control.ForegroundProperty, "Text.Primary");
        }
    }

    /// <summary>Command-bar button: a Fluent icon and an optional label. With <see cref="IsDropDown"/>, a click opens the ContextMenu.</summary>
    public class IconButton : Button
    {
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(IconButton), new PropertyMetadata(""));
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(IconButton), new PropertyMetadata(""));
        public static readonly DependencyProperty IsDropDownProperty = DependencyProperty.Register(nameof(IsDropDown), typeof(bool), typeof(IconButton), new PropertyMetadata(false));


        public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public bool IsDropDown { get => (bool)GetValue(IsDropDownProperty); set => SetValue(IsDropDownProperty, value); }

        protected override void OnClick()
        {
            if (IsDropDown && ContextMenu != null)
            {
                ContextMenu.PlacementTarget = this;
                ContextMenu.Placement = PlacementMode.Bottom;
                ContextMenu.IsOpen = true;
                return;
            }
            base.OnClick();
        }
    }

    /// <summary>Command-bar toggle (radio group): tools and styles.</summary>
    public class IconRadio : RadioButton
    {
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(IconRadio), new PropertyMetadata(""));
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(IconRadio), new PropertyMetadata(""));


        public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    }
}
