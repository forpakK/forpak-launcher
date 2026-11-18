using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ForpakLauncher
{
    /// <summary>Petite notification en bas à droite de l'écran (ami connecté, invitation...).</summary>
    public static class VeloxToast
    {
        private static readonly List<Window> Open = new();

        public static void Show(string title, string text, string buttonText = null, Action onClick = null, int seconds = 8)
        {
            var accent = (Brush)Application.Current.Resources["Accent"];
            var w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ShowInTaskbar = false, Topmost = true, ShowActivated = false, SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize
            };
            var panel = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = accent });
            panel.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = Brushes.White, Margin = new Thickness(0, 3, 0, 0), MaxWidth = 300, TextWrapping = TextWrapping.Wrap });
            if (buttonText != null)
            {
                var b = new Button { Content = buttonText, Margin = new Thickness(0, 9, 0, 0), Padding = new Thickness(14, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
                if (Application.Current.TryFindResource("PrimaryButton") is Style st) b.Style = st;
                b.Click += (_, _) => { w.Close(); onClick?.Invoke(); };
                panel.Children.Add(b);
            }
            var border = new Border
            {
                CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x0D, 0x15, 0x26)),
                BorderBrush = accent, BorderThickness = new Thickness(1), Child = panel, MinWidth = 240,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.5 }
            };
            border.MouseLeftButtonUp += (_, _) => { if (buttonText == null) w.Close(); };
            w.Content = new Grid { Margin = new Thickness(10), Children = { border } };
            w.Loaded += (_, _) =>
            {
                var area = SystemParameters.WorkArea;
                double y = area.Bottom - 8;
                foreach (var o in Open) y -= o.ActualHeight;
                w.Left = area.Right - w.ActualWidth - 8;
                w.Top = y - w.ActualHeight;
                w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            };
            w.Closed += (_, _) => Open.Remove(w);
            Open.Add(w);
            w.Show();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) => { timer.Stop(); if (w.IsVisible) w.Close(); };
            timer.Start();
        }
    }
}
