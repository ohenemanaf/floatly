using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

// Explicit WPF aliases prevent collisions when Windows Forms / System.Drawing are referenced.
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;

namespace Floatly.Views;

public partial class OverlayWindow : Window
{
    private const double MenuSize = 340;
    private const double Center = MenuSize / 2;
    private const double Pad = 14;          // clear space around the button for its shadow and stretch
    private const double RingRadius = 108;  // distance from the centre to each tool
    private static App Host => (App)System.Windows.Application.Current;
    private double Small => Host.State.BubbleSize + Pad * 2;
    private bool _expanded;
    private bool _closing;
    private double _restLeft, _restTop; // button position to return to when the menu closes
    private Point _startMouse;
    private Point _startWindow;
    private bool _pressed;
    private bool _moved;
    private ToolWindow? _toolWindow;
    private HwndSource? _source;
    private bool _hotkeyRegistered;
    private bool _clipboardListening;
    private const int HotkeyId = 0xF108;
    private const int WmHotkey = 0x0312;
    private const int WmClipboardUpdate = 0x031D;

    public OverlayWindow()
    {
        InitializeComponent();
        var state = Host.State;
        Width = Small; Height = Small;
        PlaceBubble();
        // (-1, -1) means "never placed". Other negative values are valid: a monitor
        // left of or above the primary one has negative coordinates.
        bool placed = !(state.BubbleLeft == -1 && state.BubbleTop == -1);
        var area = SystemParameters.WorkArea;
        Left = placed ? state.BubbleLeft : area.Right - Small;
        Top = placed ? state.BubbleTop : area.Top + area.Height / 2 - Small / 2;
        RefreshStyle();
        // Clicking anywhere else, or pressing Esc, closes the open menu.
        Deactivated += (_, _) => Collapse();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Collapse(); };
        SourceInitialized += (_, _) =>
        {
            _source = (HwndSource)PresentationSource.FromVisual(this);
            _source.AddHook(WindowMessage);
            ClampToVisibleArea();
            state.BubbleLeft = Left; state.BubbleTop = Top;
            RefreshHotkey();
        };
    }

    // ── Windows integration: hotkey and clipboard listener ──────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleMenu(); handled = true;
        }
        if (msg == WmClipboardUpdate && _clipboardListening)
            Host.SaveClipboardSnapshot();
        return IntPtr.Zero;
    }

    public bool ClipboardListening => _clipboardListening;
    public bool SetClipboardListening(bool enabled)
    {
        if (_source is null) return false;
        if (_clipboardListening == enabled) return true;
        if (enabled) _clipboardListening = AddClipboardFormatListener(_source.Handle);
        else { RemoveClipboardFormatListener(_source.Handle); _clipboardListening = false; }
        return _clipboardListening == enabled;
    }

    // False only when the hotkey is wanted but Windows refused it (another program owns it).
    public bool RefreshHotkey()
    {
        if (_source is null) return true;
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _hotkeyRegistered = false;
        }
        if (Host.State.HotkeyEnabled)
        {
            // MOD_CONTROL (0x2) + MOD_ALT (0x1) + MOD_NOREPEAT (0x4000), VK_SPACE (0x20)
            _hotkeyRegistered = RegisterHotKey(_source.Handle, HotkeyId, 0x4003, 0x20);
            return _hotkeyRegistered;
        }
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_source is not null)
        {
            if (_hotkeyRegistered) UnregisterHotKey(_source.Handle, HotkeyId);
            if (_clipboardListening) RemoveClipboardFormatListener(_source.Handle);
            _source.RemoveHook(WindowMessage);
        }
        base.OnClosed(e);
    }

    // ── Appearance ──────────────────────────────────────────────────────────
    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public void RefreshStyle()
    {
        var accent = (Color)ColorConverter.ConvertFromString(
            App.AccentThemes.FirstOrDefault(x => x.Name == Host.State.Accent).Color ?? "#927BFF");
        // Glass orb: light where the "light" hits (upper left), saturated body, darker rim.
        var orb = new RadialGradientBrush { GradientOrigin = new Point(0.32, 0.24), Center = new Point(0.5, 0.5), RadiusX = 0.72, RadiusY = 0.72 };
        orb.GradientStops.Add(new GradientStop(Mix(accent, Colors.White, 0.55), 0));
        orb.GradientStops.Add(new GradientStop(accent, 0.55));
        orb.GradientStops.Add(new GradientStop(Mix(accent, Colors.Black, 0.42), 1));
        orb.Freeze();
        OrbBase.Fill = orb;
        Bubble.Opacity = Host.State.BubbleOpacity;
    }

    public void SetBubbleOpacity(double opacity) => Bubble.Opacity = opacity;

    public void SetBubbleSize(int size)
    {
        Host.State.BubbleSize = Math.Clamp(size, 48, 72);
        if (_expanded || _closing) { PlaceBubble(); return; }
        Width = Small; Height = Small;
        PlaceBubble();
        ClampToVisibleArea();
        Remember();
    }

    // Centres the button in the window: in its small window when closed, in the menu when open.
    private void PlaceBubble()
    {
        double size = Host.State.BubbleSize;
        Bubble.Width = size; Bubble.Height = size;
        double origin = (_expanded || _closing) ? Center - size / 2 : Pad;
        Canvas.SetLeft(Bubble, origin); Canvas.SetTop(Bubble, origin);
    }

    public void ShowAndActivate()
    {
        Show(); Topmost = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
    }

    public void ToggleMenu()
    {
        ShowAndActivate();
        if (_expanded) Collapse(); else Expand();
    }

    public void ShowToolWindow(string tool)
    {
        // Where the button rests, even if the menu is still open around it.
        bool open = _expanded || _closing;
        double restLeft = open ? _restLeft : Left, restTop = open ? _restTop : Top;
        var area = CurrentWorkArea();
        Collapse();
        if (_toolWindow is not null && _toolWindow.IsLoaded)
        {
            _toolWindow.ShowTool(tool);
            _toolWindow.Show();
            _toolWindow.Activate();
            return;
        }
        _toolWindow = new ToolWindow(tool);
        _toolWindow.Closed += (_, _) => _toolWindow = null;
        // Open beside the button, on whichever side has room.
        bool onRight = restLeft + Small / 2 > area.Left + area.Width / 2;
        double left = onRight ? restLeft - _toolWindow.Width - 6 : restLeft + Small + 6;
        double top = restTop + Small / 2 - _toolWindow.Height / 2;
        _toolWindow.Left = Math.Clamp(left, area.Left + 8, Math.Max(area.Left + 8, area.Right - _toolWindow.Width - 8));
        _toolWindow.Top = Math.Clamp(top, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - _toolWindow.Height - 8));
        _toolWindow.Show();
    }

    // ── Screen geometry ─────────────────────────────────────────────────────
    private double DeviceScale => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;

    private Rect WorkAreaAt(Point screenPixel)
    {
        // Windows display bounds are physical pixels; convert to WPF device-independent units.
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)screenPixel.X, (int)screenPixel.Y));
        double scale = DeviceScale;
        var r = screen.WorkingArea;
        return new Rect(r.Left / scale, r.Top / scale, r.Width / scale, r.Height / scale);
    }

    private Rect CurrentWorkArea() => WorkAreaAt(PointToScreen(new Point(Width / 2, Height / 2)));

    private void ClampToVisibleArea()
    {
        var area = CurrentWorkArea();
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private void Remember()
    {
        Host.State.BubbleLeft = Left;
        Host.State.BubbleTop = Top;
        Host.Save();
    }

    // ── Button: click, drag, liquid motion ──────────────────────────────────
    private bool Motion => !Host.State.ReduceMotion;

    // Springs the button to a new stretch; different timings per axis read as a liquid wobble.
    private void Stretch(double x, double y, int milliseconds, bool spring)
    {
        if (!Motion) return;
        IEasingFunction ease = spring
            ? new ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = EasingMode.EaseOut }
            : new QuadraticEase { EasingMode = EasingMode.EaseOut };
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(x, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(y, TimeSpan.FromMilliseconds(milliseconds * 1.35)) { EasingFunction = ease });
    }

    private bool _flowing;
    private void Flow(bool on)
    {
        on = on && Motion;
        if (on == _flowing) return;
        _flowing = on;
        if (on)
        {
            Caustic.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(Caustic.Angle, Caustic.Angle + 360, TimeSpan.FromSeconds(7)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            double angle = Caustic.Angle % 360;
            Caustic.BeginAnimation(RotateTransform.AngleProperty, null);
            Caustic.Angle = angle;
        }
    }

    private void Bubble_Enter(object sender, MouseEventArgs e)
    {
        if (_pressed) return;
        Stretch(1.1, 1.1, 520, spring: true);
        Flow(true);
    }

    private void Bubble_Leave(object sender, MouseEventArgs e)
    {
        if (_pressed) return;
        Stretch(1, 1, 520, spring: true);
        if (!_expanded) Flow(false);
    }

    private void Bubble_Down(object sender, MouseButtonEventArgs e)
    {
        Stretch(1.14, 0.88, 90, spring: false); // squish under the pointer
        if (_expanded || _closing) return;
        _pressed = true; _moved = false;
        _startMouse = PointToScreen(e.GetPosition(this));
        _startWindow = new Point(Left, Top);
        Bubble.CaptureMouse();
        e.Handled = true;
    }

    private void Bubble_Move(object sender, MouseEventArgs e)
    {
        if (!_pressed || _expanded) return;
        var pos = PointToScreen(e.GetPosition(this));
        double scale = DeviceScale;
        double dx = (pos.X - _startMouse.X) / scale;
        double dy = (pos.Y - _startMouse.Y) / scale;
        if (Math.Abs(dx) + Math.Abs(dy) > 5) _moved = true;
        if (!_moved) return;
        var area = WorkAreaAt(pos);
        Left = Math.Clamp(_startWindow.X + dx, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(_startWindow.Y + dy, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private void Bubble_Up(object sender, MouseButtonEventArgs e)
    {
        double rest = Bubble.IsMouseOver ? 1.1 : 1;
        Stretch(rest, rest, 620, spring: true);
        if (_expanded) { Collapse(); e.Handled = true; return; }
        if (!_pressed) return;
        _pressed = false; Bubble.ReleaseMouseCapture();
        if (_moved) { SnapToEdge(); Remember(); }
        else Expand();
        e.Handled = true;
    }

    private void SnapToEdge()
    {
        var area = CurrentWorkArea();
        Left = Left + Small / 2 < area.Left + area.Width / 2 ? area.Left : area.Right - Small;
    }

    // ── Ring menu ───────────────────────────────────────────────────────────
    private void Expand()
    {
        if (_expanded) return;
        if (_closing) FinishClosing();
        _restLeft = Left; _restTop = Top;
        double centreX = Left + Small / 2, centreY = Top + Small / 2;
        var area = CurrentWorkArea();
        double left = Math.Clamp(centreX - Center, area.Left, Math.Max(area.Left, area.Right - MenuSize));
        double top = Math.Clamp(centreY - Center, area.Top, Math.Max(area.Top, area.Bottom - MenuSize));
        CaptureBackdrop(left, top);
        _expanded = true;
        Width = MenuSize; Height = MenuSize; Left = left; Top = top;
        PlaceBubble();
        BuildItems();
        Bubble.Opacity = 1; // a faded resting button is still solid while it is being used
        MenuHost.Visibility = Visibility.Visible;
        if (Motion)
        {
            var settle = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut };
            MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = settle });
            MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = settle });
            MenuHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            GlyphTurn.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(45, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else { MenuHost.Opacity = 1; MenuScale.ScaleX = 1; MenuScale.ScaleY = 1; }
        Flow(true);
        Activate(); // so that clicking another window deactivates and closes the menu
    }

    // Frosted glass: copy the screen behind the menu once and let the blur effect soften it.
    private void CaptureBackdrop(double left, double top)
    {
        Backdrop.Source = null;
        try
        {
            double scale = DeviceScale;
            // The Backdrop image overhangs the 320px disc by 24 on each side; the disc sits 10 in.
            int x = (int)Math.Round((left + 10 - 24) * scale), y = (int)Math.Round((top + 10 - 24) * scale);
            int size = (int)Math.Round(368 * scale);
            using var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size);
            IntPtr handle = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                Backdrop.Source = source;
            }
            finally { DeleteObject(handle); }
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException)
        {
            // No snapshot (locked desktop, odd display setup): the tint alone still gives a dark glass disc.
        }
        // Without a snapshot the disc must carry itself, so make the tint nearly opaque.
        Tint.Fill = new SolidColorBrush(Backdrop.Source is null ? Color.FromArgb(240, 19, 22, 34) : Color.FromArgb(150, 19, 22, 34));
    }

    private void BuildItems()
    {
        Items.Children.Clear();
        var tools = Icons.Tools;
        for (int i = 0; i < tools.Length; i++)
        {
            var (name, icon) = tools[i];
            double angle = (-90 + 360.0 / tools.Length * i) * Math.PI / 180;
            double dx = Math.Cos(angle) * RingRadius, dy = Math.Sin(angle) * RingRadius;
            var slide = new TranslateTransform();
            var button = new Button
            {
                Width = 64, Height = 60, Content = name, Tag = Icons.Geometry(icon),
                Style = (Style)FindResource("RingItem"), RenderTransform = slide
            };
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => ShowToolWindow(name);
            Canvas.SetLeft(button, Center + dx - button.Width / 2);
            Canvas.SetTop(button, Center + dy - button.Height / 2 + 4);
            Items.Children.Add(button);
            if (!Motion) continue;
            // Tools flow outward from the button, one after another.
            var delay = TimeSpan.FromMilliseconds(20 * i);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            button.Opacity = 0;
            button.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { BeginTime = delay });
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-dx * 0.45, 0, TimeSpan.FromMilliseconds(300)) { BeginTime = delay, EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-dy * 0.45, 0, TimeSpan.FromMilliseconds(300)) { BeginTime = delay, EasingFunction = ease });
        }
    }

    private void Disc_Click(object sender, MouseButtonEventArgs e) => Collapse();

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        _closing = true;
        if (!Motion) { FinishClosing(); return; }
        GlyphTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
        fade.Completed += (_, _) => { if (_closing) FinishClosing(); };
        MenuHost.BeginAnimation(OpacityProperty, fade);
        var shrink = new DoubleAnimation(0.86, TimeSpan.FromMilliseconds(130));
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    // Puts the window back to button size at its resting place.
    private void FinishClosing()
    {
        _closing = false;
        MenuHost.BeginAnimation(OpacityProperty, null);
        MenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        MenuHost.Visibility = Visibility.Collapsed;
        Items.Children.Clear();
        Backdrop.Source = null;
        Bubble.Opacity = Host.State.BubbleOpacity;
        Width = Small; Height = Small;
        Left = _restLeft; Top = _restTop;
        PlaceBubble();
        ClampToVisibleArea();
        if (!Bubble.IsMouseOver) Flow(false);
    }

    private void Bubble_RightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => ShowToolWindow("Settings");
        var hide = new MenuItem { Header = "Hide (restore from tray)" };
        hide.Click += (_, _) => { Collapse(); if (_closing) FinishClosing(); Hide(); };
        var quit = new MenuItem { Header = "Exit Floatly" };
        quit.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(settings); menu.Items.Add(hide); menu.Items.Add(new Separator()); menu.Items.Add(quit);
        menu.PlacementTarget = Bubble; menu.IsOpen = true; e.Handled = true;
    }
}
