using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

// Explicit WPF aliases prevent collisions when Windows Forms / System.Drawing are referenced.
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Floatly.Views;

// User-initiated primary-monitor region selection. Escape cancels without saving anything.
public sealed class RegionSelectWindow : Window
{
    private readonly System.Drawing.Bitmap _captured;
    private readonly Canvas _surface;
    private readonly Rectangle _selection;
    private Point? _origin;
    public System.Drawing.Rectangle SelectedPixels { get; private set; }

    public RegionSelectWindow(System.Drawing.Bitmap captured)
    {
        _captured = captured;
        Title = "Select screenshot region";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Primary monitor is intentionally used for region selection; full desktop capture is separate.
        double dpiX = captured.Width / Math.Max(1, SystemParameters.PrimaryScreenWidth);
        double dpiY = captured.Height / Math.Max(1, SystemParameters.PrimaryScreenHeight);
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Primary display missing.");
        Left = bounds.Left / dpiX;
        Top = bounds.Top / dpiY;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        var root = new Grid { Background = Brushes.Black };
        using var stream = new MemoryStream();
        captured.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = stream; bmp.EndInit(); bmp.Freeze();
        root.Children.Add(new Image { Source = bmp, Stretch = Stretch.Fill, Opacity = .72 });
        _surface = new Canvas { Background = Brushes.Transparent };
        root.Children.Add(_surface);
        _selection = new Rectangle
        {
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 156, 139, 255)),
            Stroke = Brushes.White, StrokeThickness = 2,
            Visibility = Visibility.Collapsed, IsHitTestVisible = false
        };
        _surface.Children.Add(_selection);
        var tip = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 20, 22, 38)),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 12, 18, 12),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 28, 0, 0), IsHitTestVisible = false,
            Child = new TextBlock { Text = "Drag to select an area   •   Esc to cancel", FontSize = 15, Foreground = Brushes.White }
        };
        root.Children.Add(tip);
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
        _surface.MouseLeftButtonDown += (_, e) =>
        {
            _origin = e.GetPosition(_surface); _surface.CaptureMouse();
            _selection.Visibility = Visibility.Visible;
            UpdateSelection(_origin.Value, _origin.Value);
        };
        _surface.MouseMove += (_, e) =>
        {
            if (_origin is not null) UpdateSelection(_origin.Value, e.GetPosition(_surface));
        };
        _surface.MouseLeftButtonUp += (_, e) =>
        {
            if (_origin is null) return;
            var end = e.GetPosition(_surface); UpdateSelection(_origin.Value, end);
            _surface.ReleaseMouseCapture();
            var start = _origin.Value; _origin = null;
            double x = Math.Min(start.X, end.X), y = Math.Min(start.Y, end.Y);
            double w = Math.Abs(start.X - end.X), h = Math.Abs(start.Y - end.Y);
            if (w < 6 || h < 6) { _selection.Visibility = Visibility.Collapsed; return; }
            double scaleX = _captured.Width / Math.Max(1, _surface.ActualWidth);
            double scaleY = _captured.Height / Math.Max(1, _surface.ActualHeight);
            int sx = Math.Clamp((int)Math.Round(x * scaleX), 0, _captured.Width - 1);
            int sy = Math.Clamp((int)Math.Round(y * scaleY), 0, _captured.Height - 1);
            int sw = Math.Clamp((int)Math.Round(w * scaleX), 1, _captured.Width - sx);
            int sh = Math.Clamp((int)Math.Round(h * scaleY), 1, _captured.Height - sy);
            SelectedPixels = new System.Drawing.Rectangle(sx, sy, sw, sh);
            DialogResult = true; Close();
        };
    }

    private void UpdateSelection(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        Canvas.SetLeft(_selection, x); Canvas.SetTop(_selection, y);
        _selection.Width = Math.Abs(b.X - a.X);
        _selection.Height = Math.Abs(b.Y - a.Y);
    }
}
