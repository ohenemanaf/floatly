using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Floatly.Models;
using Floatly.Services;
using Floatly.Views;

namespace Floatly;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _stateLoaded;
    private DispatcherTimer? _saveSoon;
    public StorageService Storage { get; } = new();
    public AppState State { get; private set; } = new();
    public ClockService Clock { get; private set; } = null!;
    public OverlayWindow Overlay { get; private set; } = null!;
    public static readonly (string Name, string Color)[] AccentThemes =
    {
        ("Violet", "#927BFF"), ("Ocean", "#46B7E8"), ("Mint", "#39CFA0"), ("Rose", "#F078AA")
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Local\\Floatly_DesktopOverlay_2026", out bool firstInstance);
        if (!firstInstance)
        {
            _singleInstance.Dispose(); _singleInstance = null;
            System.Windows.MessageBox.Show("Floatly is already running. Look for its icon in the system tray.", "Floatly");
            Shutdown(); return;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(Storage.Folder, "errors.log"),
                    DateTime.Now + Environment.NewLine + args.Exception + Environment.NewLine + Environment.NewLine);
            }
            catch { }
            System.Windows.MessageBox.Show("Floatly encountered an error. A diagnostic log was saved in its local data folder.\n\n" + args.Exception.Message,
                "Floatly", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        State = Storage.Load();
        _stateLoaded = true;
        ApplyTheme();
        Clock = new ClockService(State.FocusMinutes, State.BreakMinutes, State.CompletedFocusSessions);
        Clock.Alert += (title, message) => { Notify(title, message); System.Media.SystemSounds.Asterisk.Play(); };
        Clock.FocusSessionCompleted += () => { State.CompletedFocusSessions = Clock.CompletedFocusSessions; Save(); };
        Overlay = new OverlayWindow();
        Overlay.Show();

        var icon = System.Drawing.SystemIcons.Application;
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path)) icon = System.Drawing.Icon.ExtractAssociatedIcon(path) ?? icon;
        }
        catch { /* Windows may not expose an executable icon during debugging. */ }
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon, Visible = true, Text = "Floatly • Offline Desktop Tools"
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show floating button", null, (_, _) => Dispatcher.Invoke(() => Overlay.ShowAndActivate()));
        menu.Items.Add("Open tasks", null, (_, _) => Dispatcher.Invoke(() => Overlay.ShowToolWindow("Tasks")));
        menu.Items.Add("Open notes", null, (_, _) => Dispatcher.Invoke(() => Overlay.ShowToolWindow("Notes")));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => Overlay.ShowToolWindow("Settings")));
        menu.Items.Add("Hide floating button", null, (_, _) => Dispatcher.Invoke(() => Overlay.Hide()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit Floatly", null, (_, _) => Dispatcher.Invoke(Shutdown));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => Overlay.ShowAndActivate());
    }

    public void ApplyTheme()
    {
        string code = AccentThemes.FirstOrDefault(x => x.Name == State.Accent).Color ?? "#927BFF";
        Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(code));
        if (Overlay is not null) Overlay.RefreshStyle();
    }

    public void Save()
    {
        _saveSoon?.Stop();
        // Never write before the saved state was loaded: a second instance exits
        // early with an empty default state and would otherwise wipe the real data.
        if (_stateLoaded) Storage.Save(State);
    }

    // For values that change many times a second (sliders): one write after changes settle.
    public void SaveSoon()
    {
        if (_saveSoon is null)
        {
            _saveSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveSoon.Tick += (_, _) => Save();
        }
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    // Session-only opt-in. Default OFF every launch; do not silently collect clipboard contents.
    public void SaveClipboardSnapshot()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText()) return;
            string content = System.Windows.Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(content)) return;
            if (content.Length > 4000) content = content[..4000];
            State.ClipboardItems.RemoveAll(x => x.Content == content);
            State.ClipboardItems.Insert(0, new ClipboardEntry { Content = content });
            if (State.ClipboardItems.Count > 30)
                State.ClipboardItems.RemoveRange(30, State.ClipboardItems.Count - 30);
            Save();
        }
        catch (System.Runtime.InteropServices.ExternalException) { /* Clipboard locked by another app. */ }

    }

    public void Notify(string title, string message)
    {
        if (_tray is null) return;
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = message;
        _tray.ShowBalloonTip(4000);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Save(); } catch { }
        Clock?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        if (_singleInstance is not null) { _singleInstance.ReleaseMutex(); _singleInstance.Dispose(); }
        base.OnExit(e);
    }
}
