using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

// Explicit WPF aliases prevent collisions when Windows Forms / System.Drawing are referenced.
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Floatly.Views;

public partial class OverlayWindow : Window
{
    private const double MenuSize = 360;
    private const double Center = MenuSize / 2;
    private double Small => Math.Max(78, ((App)System.Windows.Application.Current).State.BubbleSize + 14);
    private bool _expanded;
    private double _restLeft, _restTop; // bubble position to return to when the menu closes
    private int _page;
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
    private readonly (string icon, string label)[][] _pages =
    {
        new[] { ("✓", "Tasks"), ("✎", "Notes"), ("↗", "Launcher"), ("◷", "Timer"), ("▤", "Clipboard") },
        new[] { ("▣", "Screenshot"), ("=", "Calculator"), ("⚙", "Settings"), ("♫", "System"), ("◎", "Focus") }
    };

    public OverlayWindow()
    {
        InitializeComponent();
        var state = ((App)System.Windows.Application.Current).State;
        Width = Small; Height = Small;
        Canvas.SetLeft(Bubble, (Small - state.BubbleSize) / 2);
        Canvas.SetTop(Bubble, (Small - state.BubbleSize) / 2);
        Bubble.Width = state.BubbleSize; Bubble.Height = state.BubbleSize;
        Bubble.CornerRadius = new CornerRadius(state.BubbleSize / 2);
        // (-1, -1) means "never placed". Other negative values are valid: a monitor
        // left of or above the primary one has negative coordinates.
        bool placed = !(state.BubbleLeft == -1 && state.BubbleTop == -1);
        var area = SystemParameters.WorkArea;
        Left = placed ? state.BubbleLeft : area.Right - Small - 12;
        Top = placed ? state.BubbleTop : area.Top + area.Height / 2 - Small / 2;
        RefreshStyle();
        // Clicking anywhere else closes the open menu.
        Deactivated += (_, _) => Collapse();
        SourceInitialized += (_, _) =>
        {
            _source = (HwndSource)PresentationSource.FromVisual(this);
            _source.AddHook(WindowMessage);
            ClampToVisibleArea();
            state.BubbleLeft = Left; state.BubbleTop = Top;
            RefreshHotkey();
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleMenu(); handled = true;
        }
        if (msg == WmClipboardUpdate && _clipboardListening)
            ((App)System.Windows.Application.Current).SaveClipboardSnapshot();
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

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
        if (((App)System.Windows.Application.Current).State.HotkeyEnabled)
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

    public void RefreshStyle()
    {
        var app = (App)System.Windows.Application.Current;
        var accent = (Color)ColorConverter.ConvertFromString(App.AccentThemes.FirstOrDefault(x => x.Name == app.State.Accent).Color ?? "#927BFF");
        Bubble.Background = new LinearGradientBrush(Color.FromRgb((byte)Math.Min(255, accent.R + 23),
            (byte)Math.Min(255, accent.G + 23), (byte)Math.Min(255, accent.B + 10)), accent, new Point(0, 0), new Point(1, 1));
        Bubble.Opacity = app.State.BubbleOpacity;
        if (_expanded) DrawMenu();
    }

    public void SetBubbleOpacity(double opacity) => Bubble.Opacity = opacity;

    public void SetBubbleSize(int size)
    {
        var app = (App)System.Windows.Application.Current;
        app.State.BubbleSize = Math.Clamp(size, 48, 72);
        Bubble.Width = app.State.BubbleSize;
        Bubble.Height = app.State.BubbleSize;
        Bubble.CornerRadius = new CornerRadius(app.State.BubbleSize / 2);
        if (_expanded) DrawMenu();
        else
        {
            Width = Small; Height = Small;
            Canvas.SetLeft(Bubble, (Small - app.State.BubbleSize) / 2);
            Canvas.SetTop(Bubble, (Small - app.State.BubbleSize) / 2);
            ClampToVisibleArea();
            Remember();
        }
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
        if (_expanded) Collapse();
        if (_toolWindow is not null && _toolWindow.IsLoaded)
        {
            _toolWindow.ShowTool(tool);
            _toolWindow.Show();
            _toolWindow.Activate();
        }
        else
        {
            _toolWindow = new ToolWindow(tool);
            _toolWindow.Closed += (_, _) => _toolWindow = null;
            _toolWindow.Show();
        }
    }

    private Rect WorkAreaAt(Point screenPixel)
    {
        // Windows display bounds are physical pixels; convert to WPF device-independent units.
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)screenPixel.X, (int)screenPixel.Y));
        double scaleX = 1, scaleY = 1;
        var visual = PresentationSource.FromVisual(this);
        if (visual?.CompositionTarget is not null)
        {
            scaleX = visual.CompositionTarget.TransformToDevice.M11;
            scaleY = visual.CompositionTarget.TransformToDevice.M22;
        }
        var r = screen.WorkingArea;
        return new Rect(r.Left / scaleX, r.Top / scaleY, r.Width / scaleX, r.Height / scaleY);
    }

    private Rect CurrentWorkArea()
    {
        var centerPhysical = PointToScreen(new Point(Width / 2, Height / 2));
        return WorkAreaAt(centerPhysical);
    }

    private void ClampToVisibleArea()
    {
        var area = CurrentWorkArea();
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private void Remember()
    {
        var app = (App)System.Windows.Application.Current;
        app.State.BubbleLeft = Left;
        app.State.BubbleTop = Top;
        app.Save();
    }

    private void Bubble_Down(object sender, MouseButtonEventArgs e)
    {
        if (_expanded) return;
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
        var visual = PresentationSource.FromVisual(this)?.CompositionTarget;
        double dx = (pos.X - _startMouse.X) / (visual?.TransformToDevice.M11 ?? 1);
        double dy = (pos.Y - _startMouse.Y) / (visual?.TransformToDevice.M22 ?? 1);
        if (Math.Abs(dx) + Math.Abs(dy) > 5) _moved = true;
        if (!_moved) return;
        var area = WorkAreaAt(pos);
        Left = Math.Clamp(_startWindow.X + dx, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(_startWindow.Y + dy, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private void Bubble_Up(object sender, MouseButtonEventArgs e)
    {
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
        Left = Left + Small / 2 < area.Left + area.Width / 2 ? area.Left + 8 : area.Right - Small - 8;
    }

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        _restLeft = Left; _restTop = Top;
        var currentCenterX = Left + Small / 2;
        var currentCenterY = Top + Small / 2;
        var area = CurrentWorkArea();
        Width = MenuSize; Height = MenuSize;
        Left = Math.Clamp(currentCenterX - Center, area.Left, Math.Max(area.Left, area.Right - MenuSize));
        Top = Math.Clamp(currentCenterY - Center, area.Top, Math.Max(area.Top, area.Bottom - MenuSize));
        DrawMenu();
        Activate(); // so that clicking another window deactivates and closes the menu
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        Surface.Children.Clear();
        Surface.Children.Add(Bubble);
        var state = ((App)System.Windows.Application.Current).State;
        Width = Small; Height = Small;
        Left = _restLeft; Top = _restTop;
        Canvas.SetLeft(Bubble, (Small - state.BubbleSize) / 2);
        Canvas.SetTop(Bubble, (Small - state.BubbleSize) / 2);
        ClampToVisibleArea();
    }

    private void DrawMenu()
    {
        Surface.Children.Clear();
        var back = new Ellipse
        {
            Width = 345, Height = 345,
            Fill = new SolidColorBrush(Color.FromArgb(245, 21, 25, 43)),
            Stroke = new SolidColorBrush(Color.FromArgb(120, 145, 128, 250)),
            StrokeThickness = 1,
            Effect = new DropShadowEffect { BlurRadius = 29, ShadowDepth = 7, Opacity = 0.45 }
        };
        back.MouseLeftButtonUp += (_, _) => Collapse();
        Canvas.SetLeft(back, 7.5); Canvas.SetTop(back, 7.5); Surface.Children.Add(back);
        // Layout (offsets from the centre): tools on a 110 ring, the More pill just under
        // the bubble in the gap between the two lower tools, the page label below them.
        var label = new TextBlock { Text = _page == 0 ? "YOUR ESSENTIALS  •  1/2" : "MORE TOOLS  •  2/2", Foreground = Brushes.LightGray,
            FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = .78, IsHitTestVisible = false,
            Width = MenuSize, TextAlignment = TextAlignment.Center };
        Canvas.SetLeft(label, 0); Canvas.SetTop(label, Center + 128); Surface.Children.Add(label);
        var idle = new SolidColorBrush(Color.FromRgb(44, 49, 76));
        var hover = new SolidColorBrush(Color.FromRgb(62, 68, 104));
        var tools = _pages[_page];
        for (int i = 0; i < tools.Length; i++)
        {
            var tool = tools[i];
            double angle = (-90 + 72 * i) * Math.PI / 180;
            const double radius = 110;
            var button = new Button
            {
                Width = 76, Height = 58,
                Background = idle,
                BorderThickness = new Thickness(0), Foreground = Brushes.White,
                Cursor = Cursors.Hand, ToolTip = "Open " + tool.label,
                Opacity = ((App)System.Windows.Application.Current).State.ReduceMotion ? 1 : 0,
                Content = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = tool.icon, FontSize = 19, FontFamily = new FontFamily("Segoe UI Symbol"), HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = tool.label, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center }
                    }
                }
            };
            button.Template = RoundedButtonTemplate(16);
            button.MouseEnter += (_, _) => button.Background = hover;
            button.MouseLeave += (_, _) => button.Background = idle;
            button.Click += (_, _) => ShowToolWindow(tool.label);
            Canvas.SetLeft(button, Center + Math.Cos(angle) * radius - button.Width / 2);
            Canvas.SetTop(button, Center + Math.Sin(angle) * radius - button.Height / 2);
            Surface.Children.Add(button);
            if (!((App)System.Windows.Application.Current).State.ReduceMotion)
                button.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(155 + i * 24)));
        }
        var more = new Button
        {
            Content = _page == 0 ? "More ›" : "‹ Back",
            Background = new SolidColorBrush(Color.FromRgb(69, 55, 118)), Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Width = 52, Height = 26,
            Cursor = Cursors.Hand, FontSize = 11, Template = RoundedButtonTemplate(13),
            ToolTip = _page == 0 ? "Show five more tools" : "Back to the first five tools"
        };
        more.Click += (_, _) => { _page = 1 - _page; DrawMenu(); };
        Canvas.SetLeft(more, Center - 26); Canvas.SetTop(more, Center + 42); Surface.Children.Add(more);
        Surface.Children.Add(Bubble);
        Canvas.SetLeft(Bubble, Center - Bubble.Width / 2);
        Canvas.SetTop(Bubble, Center - Bubble.Height / 2);
    }

    private static ControlTemplate RoundedButtonTemplate(int radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty,
            new Binding(nameof(Button.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.PaddingProperty, new Thickness(5));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void Bubble_RightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => ShowToolWindow("Settings");
        var hide = new MenuItem { Header = "Hide (restore from tray)" };
        hide.Click += (_, _) => { if (_expanded) Collapse(); Hide(); };
        var quit = new MenuItem { Header = "Exit Floatly" };
        quit.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(settings); menu.Items.Add(hide); menu.Items.Add(new Separator()); menu.Items.Add(quit);
        menu.PlacementTarget = Bubble; menu.IsOpen = true; e.Handled = true;
    }
}
