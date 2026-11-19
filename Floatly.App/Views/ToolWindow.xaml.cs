using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Floatly.Models;
using Floatly.Services;

// Explicit WPF aliases prevent collisions when Windows Forms / System.Drawing are referenced.
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Floatly.Views;

public partial class ToolWindow : Window
{
    private readonly App _app = (App)System.Windows.Application.Current;
    private string _currentTool = "";
    private string _taskFilter = "All";
    private int _newDue;                      // index into DueChoices for the task being typed
    private string _newPriority = "Normal";
    private string _noteSearch = "";
    private Guid? _openNote;                  // note shown in the editor; null shows the list
    private NoteItem? _editingNote;
    private TextBox? _noteEditor;
    private TextBox? _noteTitle;
    private readonly DispatcherTimer _noteAutosave = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.4) };
    private TextBlock? _timerDisplay;
    private TextBlock? _stopwatchDisplay;
    private TextBlock? _focusDisplay;
    private TextBlock? _sessionStats;
    private int _timerMinutes = 5;            // what Reset returns the countdown to
    private bool _startupReverting;
    private (bool, bool, bool) _clockSignature;
    private string? _lastCapture;

    private static readonly string[] DueChoices = { "No date", "Today", "Tomorrow", "Next week" };
    private static readonly string[] Priorities = { "Normal", "Important", "Urgent" };
    private static readonly string[] TaskFilters = { "All", "Active", "Done", "Today", "Overdue" };

    public ToolWindow(string initialTool)
    {
        InitializeComponent();
        CloseButton.Tag = Icons.Geometry(Icons.Close);
        SourceInitialized += (_, _) => ApplyGlass();
        _noteAutosave.Tick += (_, _) => SavePendingNotes();
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
        };
        _app.Clock.Changed += UpdateClocks;
        ShowTool(initialTool);
    }

    // ── Glass window ────────────────────────────────────────────────────────
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    // Windows 11 22H2+ can draw a live blurred backdrop behind the window. Older systems
    // get the same layout on a solid dark surface.
    private void ApplyGlass()
    {
        bool glass = false;
        IntPtr handle = new WindowInteropHelper(this).Handle;
        // The extended frame would otherwise draw Windows' own minimise/maximise/close
        // buttons over the panel. Dropping the system menu style removes them.
        const int GwlStyle = -16, WsSysMenu = 0x80000, WsMinimizeBox = 0x20000, WsMaximizeBox = 0x10000;
        SetWindowLong(handle, GwlStyle, GetWindowLong(handle, GwlStyle) & ~(WsSysMenu | WsMinimizeBox | WsMaximizeBox));
        if (Environment.OSVersion.Version.Build >= 22621 && HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
            int darkMode = 1, roundCorners = 2, acrylic = 3;
            DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));      // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(handle, 33, ref roundCorners, sizeof(int));  // DWMWA_WINDOW_CORNER_PREFERENCE
            glass = DwmSetWindowAttribute(handle, 38, ref acrylic, sizeof(int)) == 0; // DWMWA_SYSTEMBACKDROP_TYPE
        }
        if (!glass) Tint.Background = Brush("#171A27");
    }

    // ── Navigation ──────────────────────────────────────────────────────────
    public void ShowTool(string tool)
    {
        if (!Icons.Tools.Any(t => t.Name == tool)) tool = "Tasks";
        bool switched = tool != _currentTool;
        SavePendingNotes(releaseEditor: true);
        if (switched) _openNote = null;
        _currentTool = tool;
        _clockSignature = ClockSignature();
        _timerDisplay = null; _stopwatchDisplay = null; _focusDisplay = null; _sessionStats = null;
        TitleText.Text = tool;
        BuildRail();
        ContentPanel.Children.Clear();
        switch (tool)
        {
            case "Tasks": BuildTasks(); break;
            case "Notes": BuildNotes(); break;
            case "Launcher": BuildLauncher(); break;
            case "Timer": BuildTimer(); break;
            case "Clipboard": BuildClipboard(); break;
            case "Screenshot": BuildScreenshot(); break;
            case "Calculator": BuildCalculator(); break;
            case "Settings": BuildSettings(); break;
            case "System": BuildSystem(); break;
            case "Focus": BuildFocus(); break;
        }
        if (!switched) return;
        Scroller.ScrollToTop();
        if (_app.State.ReduceMotion) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ContentPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        ContentSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    private void Rebuild() => ShowTool(_currentTool);

    private void BuildRail()
    {
        Rail.Children.Clear();
        foreach (var (name, icon) in Icons.Tools)
        {
            var button = IconBtn(icon, name, () => ShowTool(name));
            button.Width = 36; button.Height = 36;
            button.HorizontalAlignment = HorizontalAlignment.Center;
            button.Margin = new Thickness(0, 0, 0, 4);
            AutomationProperties.SetName(button, name);
            if (name == _currentTool) button.SetResourceReference(BackgroundProperty, "AccentBrush");
            else
            {
                button.Foreground = Brush("#B9C3E0");
                button.MouseEnter += (_, _) => button.Background = Brush("#1FFFFFFF");
                button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
            }
            Rail.Children.Add(button);
        }
    }

    // ── Building blocks ─────────────────────────────────────────────────────
    private static SolidColorBrush Brush(string code) => (SolidColorBrush)new BrushConverter().ConvertFromString(code)!;

    private static TextBlock Text(string text, double size = 12.5, string color = "#EEF2FF") => new()
    {
        Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6)
    };
    private static TextBlock Muted(string text) => Text(text, 11.5, "#9AA6C8");
    private static TextBlock Section(string text) => new()
    {
        Text = text.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold,
        Foreground = Brush("#8492B8"), Margin = new Thickness(0, 14, 0, 8)
    };
    private static TextBox Input(string hint, string text = "") => new()
    {
        Text = text, Tag = hint, Height = 34, Margin = new Thickness(0, 0, 0, 8)
    };
    private Button Btn(string title, Action action, bool primary = false)
    {
        var button = new Button
        {
            Content = title, Style = (Style)FindResource(primary ? "AccentButton" : "GlassButton"),
            Margin = new Thickness(0, 0, 6, 6)
        };
        button.Click += (_, _) => action();
        return button;
    }
    private Button IconBtn(string icon, string tip, Action action)
    {
        var button = new Button { Tag = Icons.Geometry(icon), ToolTip = tip, Style = (Style)FindResource("IconButton") };
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => action();
        return button;
    }
    // Icon button on an accent background, used beside an input ("add").
    private Button AccentIconBtn(string icon, string tip, Action action)
    {
        var button = IconBtn(icon, tip, action);
        button.Width = 34; button.Height = 34;
        button.Margin = new Thickness(6, 0, 0, 8);
        button.SetResourceReference(BackgroundProperty, "AccentBrush");
        return button;
    }
    // Asks for a second click before doing something that cannot be undone.
    private Button ConfirmBtn(string title, Action action)
    {
        bool armed = false;
        Button button = null!;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) => { timer.Stop(); armed = false; button.Content = title; };
        button = Btn(title, () =>
        {
            if (armed) { timer.Stop(); action(); return; }
            armed = true; button.Content = "Click again to confirm"; timer.Start();
        });
        return button;
    }
    private void Add(params UIElement[] elements)
    {
        foreach (var element in elements) ContentPanel.Children.Add(element);
    }
    private static WrapPanel Wrap(params UIElement[] elements)
    {
        var panel = new WrapPanel();
        foreach (var element in elements) panel.Children.Add(element);
        return panel;
    }
    // Main element fills the width, the trailing ones keep their natural size on the right.
    private static DockPanel Row(UIElement main, params UIElement[] trailing)
    {
        var row = new DockPanel();
        foreach (var element in trailing.Reverse())
        {
            DockPanel.SetDock(element, Dock.Right);
            row.Children.Add(element);
        }
        row.Children.Add(main);
        return row;
    }
    private static Border Card(UIElement content) => new()
    {
        Child = content, Background = Brush("#10FFFFFF"), BorderBrush = Brush("#1CFFFFFF"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
        Padding = new Thickness(11, 9, 8, 9), Margin = new Thickness(0, 0, 0, 6)
    };
    private WrapPanel Chips(string[] options, int selected, Action<int> pick)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        string group = Guid.NewGuid().ToString("N");
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var chip = new RadioButton
            {
                Content = options[i], GroupName = group, IsChecked = i == selected, Style = (Style)FindResource("Chip")
            };
            chip.Checked += (_, _) => pick(index);
            panel.Children.Add(chip);
        }
        return panel;
    }
    private static TextBlock ClockLabel(string value)
    {
        var label = new TextBlock
        {
            Text = value, FontSize = 40, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular); // digits keep their width while ticking
        return label;
    }
    private static string Format(TimeSpan span) => $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";

    private void Toast(string message)
    {
        ToastText.Text = message;
        ToastHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        _toastTimer.Stop(); _toastTimer.Start();
    }

    // ── Tasks ───────────────────────────────────────────────────────────────
    private void BuildTasks()
    {
        var input = Input("Add a task");
        void AddTask()
        {
            string name = input.Text.Trim();
            if (name.Length == 0) { input.Focus(); return; }
            DateTime? due = _newDue switch
            {
                1 => DateTime.Today, 2 => DateTime.Today.AddDays(1), 3 => DateTime.Today.AddDays(7), _ => null
            };
            _app.State.Tasks.Insert(0, new TaskItem
            {
                Title = name.Length > 250 ? name[..250] : name, DueDate = due, Priority = _newPriority
            });
            _newDue = 0; _newPriority = "Normal";
            _app.Save(); Rebuild();
        }
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddTask(); };
        Add(Row(input, AccentIconBtn(Icons.Plus, "Add task", AddTask)),
            Chips(DueChoices, _newDue, i => _newDue = i),
            Chips(Priorities, Array.IndexOf(Priorities, _newPriority), i => _newPriority = Priorities[i]));

        var all = _app.State.Tasks;
        var today = DateTime.Today;
        Add(Section(all.Count == 0 ? "Your tasks" : $"Your tasks  ·  {all.Count(t => t.Done)} of {all.Count} done"),
            Chips(TaskFilters, Math.Max(0, Array.IndexOf(TaskFilters, _taskFilter)), i => { _taskFilter = TaskFilters[i]; Rebuild(); }));
        var shown = all.Where(t => _taskFilter switch
        {
            "Active" => !t.Done,
            "Done" => t.Done,
            "Today" => !t.Done && t.DueDate?.Date == today,
            "Overdue" => !t.Done && t.DueDate?.Date < today,
            _ => true
        }).OrderBy(t => t.Done).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ToList();
        if (shown.Count == 0) Add(Muted(all.Count == 0 ? "Nothing yet. Type above and press Enter." : "Nothing in this view."));
        foreach (var task in shown)
        {
            var check = new CheckBox { IsChecked = task.Done, Style = (Style)FindResource("RoundCheck"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
            AutomationProperties.SetName(check, task.Title);
            check.Click += (_, _) => { task.Done = check.IsChecked == true; _app.Save(); Rebuild(); };
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = Text(task.Title, 12.5, task.Done ? "#8492B8" : "#EEF2FF");
            title.Margin = new Thickness(0);
            if (task.Done) title.TextDecorations = TextDecorations.Strikethrough;
            words.Children.Add(title);
            bool overdue = !task.Done && task.DueDate?.Date < today;
            string due = task.DueDate?.Date switch
            {
                null => "",
                var d when d == today => "Today",
                var d when d == today.AddDays(1) => "Tomorrow",
                var d => d.Value.ToString("d MMM", CultureInfo.CurrentCulture)
            };
            string meta = string.Join("  ·  ", new[] { task.Priority == "Normal" ? "" : task.Priority, overdue ? "Overdue, " + due : due }.Where(s => s.Length > 0));
            if (meta.Length > 0)
            {
                var line = Text(meta, 11, overdue ? "#FF9C99" : task.Priority == "Urgent" && !task.Done ? "#FFC98A" : "#9AA6C8");
                line.Margin = new Thickness(0, 2, 0, 0);
                words.Children.Add(line);
            }
            var remove = IconBtn(Icons.Trash, "Delete task", () =>
            {
                _app.State.Tasks.RemoveAll(t => t.Id == task.Id); _app.Save(); Rebuild();
            });
            remove.Foreground = Brush("#8492B8");
            var row = new DockPanel();
            DockPanel.SetDock(check, Dock.Left); DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(check); row.Children.Add(remove); row.Children.Add(words);
            Add(Card(row));
        }
        // Keep typing: the box takes focus again after each add.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (_currentTool == "Tasks" && IsActive) input.Focus(); }));
    }

    // ── Notes ───────────────────────────────────────────────────────────────
    private void SavePendingNotes(bool releaseEditor = false)
    {
        _noteAutosave.Stop();
        if (_editingNote is null || _noteEditor is null || _noteTitle is null) return;
        if (_app.State.Notes.Any(n => n.Id == _editingNote.Id))
        {
            string title = _noteTitle.Text.Trim();
            if (title.Length == 0) title = "Untitled note";
            string body = _noteEditor.Text;
            if (_editingNote.Title != title || _editingNote.Content != body)
            {
                _editingNote.Title = title;
                _editingNote.Content = body;
                _editingNote.ModifiedUtc = DateTime.UtcNow;
                _app.Save();
            }
        }
        if (releaseEditor)
        {
            _editingNote = null;
            _noteTitle = null;
            _noteEditor = null;
        }
    }

    private void BuildNotes()
    {
        var open = _openNote is Guid id ? _app.State.Notes.FirstOrDefault(n => n.Id == id) : null;
        if (open is not null) { BuildNoteEditor(open); return; }
        _openNote = null;
        var search = Input("Search notes", _noteSearch);
        var list = new StackPanel();
        void Fill()
        {
            list.Children.Clear();
            var matches = _app.State.Notes.Where(n => _noteSearch.Length == 0
                || n.Title.Contains(_noteSearch, StringComparison.OrdinalIgnoreCase)
                || n.Content.Contains(_noteSearch, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.ModifiedUtc).ToList();
            if (matches.Count == 0)
                list.Children.Add(Muted(_app.State.Notes.Count == 0 ? "No notes yet. Press + to write one." : "Nothing matches that search."));
            foreach (var note in matches)
            {
                var words = new StackPanel();
                var title = Text(note.Title, 12.5); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0);
                title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
                words.Children.Add(title);
                string firstLine = note.Content.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "Empty note";
                var preview = Text(firstLine, 11.5, "#9AA6C8"); preview.Margin = new Thickness(0, 2, 0, 0);
                preview.TextWrapping = TextWrapping.NoWrap; preview.TextTrimming = TextTrimming.CharacterEllipsis;
                words.Children.Add(preview);
                var card = Card(words);
                card.Cursor = Cursors.Hand;
                card.MouseEnter += (_, _) => card.Background = Brush("#22FFFFFF");
                card.MouseLeave += (_, _) => card.Background = Brush("#10FFFFFF");
                card.MouseLeftButtonUp += (_, _) => { _openNote = note.Id; Rebuild(); };
                list.Children.Add(card);
            }
        }
        search.TextChanged += (_, _) => { _noteSearch = search.Text.Trim(); Fill(); };
        Add(Row(search, AccentIconBtn(Icons.Plus, "New note", () =>
        {
            var note = new NoteItem();
            _app.State.Notes.Insert(0, note);
            _app.Save(); _openNote = note.Id; Rebuild();
        })), list);
        Fill();
    }

    private void BuildNoteEditor(NoteItem note)
    {
        _editingNote = note;
        void Leave() { SavePendingNotes(releaseEditor: true); _openNote = null; Rebuild(); }
        var delete = ConfirmBtn("Delete", () =>
        {
            _app.State.Notes.RemoveAll(n => n.Id == note.Id);
            _editingNote = null; _noteTitle = null; _noteEditor = null; _openNote = null;
            _app.Save(); Rebuild();
        });
        delete.Margin = new Thickness(0, 0, 0, 8);
        var back = IconBtn(Icons.Back, "All notes", Leave);
        back.Margin = new Thickness(-6, 0, 0, 8); back.HorizontalAlignment = HorizontalAlignment.Left;
        Add(Row(back, delete));
        _noteTitle = Input("Title", note.Title == "Untitled note" ? "" : note.Title);
        _noteTitle.FontWeight = FontWeights.SemiBold;
        _noteEditor = new TextBox
        {
            Text = note.Content, Tag = "Write here…", AcceptsReturn = true, AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 330, VerticalContentAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8)
        };
        _noteTitle.TextChanged += (_, _) => { _noteAutosave.Stop(); _noteAutosave.Start(); };
        _noteEditor.TextChanged += (_, _) => { _noteAutosave.Stop(); _noteAutosave.Start(); };
        Add(_noteTitle, _noteEditor, Muted("Saved automatically as you type."));
        var editor = _noteEditor;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsActive) editor.Focus(); }));
    }

    // ── Launcher ────────────────────────────────────────────────────────────
    private void BuildLauncher()
    {
        Add(Wrap(Btn("Notepad", () => Launch("notepad.exe")), Btn("Explorer", () => Launch("explorer.exe")),
            Btn("Paint", () => Launch("mspaint.exe")), Btn("Task Manager", () => Launch("taskmgr.exe"))));
        Add(Section("Pinned"));
        if (_app.State.Shortcuts.Count == 0) Add(Muted("Pin an app, file, folder or website below."));
        foreach (var entry in _app.State.Shortcuts.ToList())
        {
            var open = Btn(entry.Name, () => Launch(entry.Target), true);
            open.Margin = new Thickness(0); open.HorizontalAlignment = HorizontalAlignment.Left; open.ToolTip = entry.Target;
            var remove = IconBtn(Icons.Trash, "Remove shortcut", () =>
            {
                _app.State.Shortcuts.RemoveAll(x => x.Id == entry.Id); _app.Save(); Rebuild();
            });
            remove.Foreground = Brush("#8492B8");
            Add(Card(Row(open, remove)));
        }
        Add(Section("Pin something"));
        var name = Input("Name"); var target = Input("Path or https:// link");
        Add(name, target, Wrap(
            Btn("File…", () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose an app, document or shortcut", Filter = "All files (*.*)|*.*" };
                if (dialog.ShowDialog(this) != true) return;
                target.Text = dialog.FileName;
                if (name.Text == "") name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }),
            Btn("Folder…", () =>
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder" };
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                target.Text = dialog.SelectedPath;
                if (name.Text == "") name.Text = Path.GetFileName(dialog.SelectedPath);
            }),
            Btn("Pin", () =>
            {
                string n = name.Text.Trim(), path = target.Text.Trim();
                if (n.Length == 0 || path.Length == 0) { Toast("Add a name and a path or link."); return; }
                if (!(path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || File.Exists(path) || Directory.Exists(path)))
                { Toast("That file or folder does not exist. Links must start with http."); return; }
                _app.State.Shortcuts.Add(new LauncherItem { Name = n[..Math.Min(n.Length, 80)], Target = path });
                _app.Save(); Rebuild();
            }, true)));
    }

    private void Launch(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Toast("Could not open it: " + ex.Message); }
    }

    // ── Timer and stopwatch ─────────────────────────────────────────────────
    private (bool, bool, bool) ClockSignature() => (_app.Clock.TimerRunning, _app.Clock.FocusRunning, _app.Clock.FocusIsBreak);

    private void UpdateClocks()
    {
        // A countdown or session that ends on its own must also refresh the
        // Start/Pause buttons and the focus/break choice, not only the digits.
        if (_currentTool is "Timer" or "Focus" && _clockSignature != ClockSignature())
        {
            Rebuild();
            return;
        }
        if (_timerDisplay is not null) _timerDisplay.Text = Format(_app.Clock.TimerRemaining);
        if (_stopwatchDisplay is not null) _stopwatchDisplay.Text = Format(_app.Clock.StopwatchElapsed);
        if (_focusDisplay is not null) _focusDisplay.Text = Format(_app.Clock.FocusRemaining);
        if (_sessionStats is not null) _sessionStats.Text = SessionText();
    }

    private void BuildTimer()
    {
        _timerDisplay = ClockLabel(Format(_app.Clock.TimerRemaining));
        void Set(int minutes) { _timerMinutes = minutes; _app.Clock.SetTimer(minutes); Rebuild(); }
        int[] presets = { 1, 5, 15, 30, 60 };
        var custom = Input("Minutes");
        custom.Width = 90; custom.Margin = new Thickness(0, 0, 6, 6); custom.Height = 31;
        custom.Padding = new Thickness(10, 0, 10, 0); // 31px leaves no room for the default 7px vertical padding
        void SetCustom()
        {
            if (!int.TryParse(custom.Text, out int n) || n < 1 || n > 180) { Toast("Enter 1 to 180 minutes."); return; }
            Set(n);
        }
        custom.KeyDown += (_, e) => { if (e.Key == Key.Enter) SetCustom(); };
        Add(_timerDisplay,
            Wrap(Btn(_app.Clock.TimerRunning ? "Pause" : "Start", () => { _app.Clock.ToggleTimer(); Rebuild(); }, true),
                 Btn("Reset", () => Set(_timerMinutes))),
            Chips(presets.Select(p => p + " min").ToArray(), Array.IndexOf(presets, _timerMinutes), i => Set(presets[i])),
            Wrap(custom, Btn("Set", SetCustom)));
        Add(Section("Stopwatch"));
        _stopwatchDisplay = ClockLabel(Format(_app.Clock.StopwatchElapsed));
        Add(_stopwatchDisplay,
            Wrap(Btn(_app.Clock.StopwatchRunning ? "Pause" : "Start", () => { _app.Clock.ToggleStopwatch(); Rebuild(); }, true),
                 Btn("Reset", () => { _app.Clock.ResetStopwatch(); Rebuild(); })));
    }

    // ── Focus sessions ──────────────────────────────────────────────────────
    private string SessionText()
    {
        int count = _app.Clock.CompletedFocusSessions;
        return count == 1 ? "1 focus session completed" : $"{count} focus sessions completed";
    }

    private void BuildFocus()
    {
        _focusDisplay = ClockLabel(Format(_app.Clock.FocusRemaining));
        _sessionStats = Muted(SessionText());
        Add(Chips(new[] { "Focus", "Break" }, _app.Clock.FocusIsBreak ? 1 : 0, i =>
            {
                if ((i == 1) != _app.Clock.FocusIsBreak) _app.Clock.SwitchFocusMode();
                Rebuild();
            }),
            _focusDisplay,
            Wrap(Btn(_app.Clock.FocusRunning ? "Pause" : "Start", () => { _app.Clock.ToggleFocus(); Rebuild(); }, true),
                 Btn("Reset", () => { _app.Clock.ResetFocus(); Rebuild(); })),
            _sessionStats);
        Add(Section("Lengths in minutes"));
        var focusInput = Input("Focus", _app.Clock.FocusMinutes.ToString());
        var breakInput = Input("Break", _app.Clock.BreakMinutes.ToString());
        focusInput.Width = 70; breakInput.Width = 70;
        focusInput.Margin = breakInput.Margin = new Thickness(0, 0, 6, 6);
        focusInput.Height = breakInput.Height = 31;
        focusInput.Padding = breakInput.Padding = new Thickness(10, 0, 10, 0);
        focusInput.ToolTip = "Focus length, 1 to 120"; breakInput.ToolTip = "Break length, 1 to 60";
        Add(Wrap(focusInput, breakInput, Btn("Save", () =>
        {
            if (!int.TryParse(focusInput.Text, out int focus) || focus is < 1 or > 120 ||
                !int.TryParse(breakInput.Text, out int rest) || rest is < 1 or > 60)
            { Toast("Focus is 1 to 120 minutes, break is 1 to 60."); return; }
            _app.State.FocusMinutes = focus; _app.State.BreakMinutes = rest;
            _app.Clock.UpdateFocusDurations(focus, rest);
            _app.Save(); Rebuild();
        })));
    }

    // ── Clipboard (explicit capture or session-only opt in) ─────────────────
    private void BuildClipboard()
    {
        var listen = new CheckBox { Content = "Save everything I copy", IsChecked = _app.Overlay.ClipboardListening };
        listen.Checked += (_, _) =>
        {
            if (!_app.Overlay.SetClipboardListening(true)) { Toast("Could not start watching the clipboard."); listen.IsChecked = false; }
        };
        listen.Unchecked += (_, _) => _app.Overlay.SetClipboardListening(false);
        Add(listen,
            Muted("Off every time Floatly starts. Saved text is stored unencrypted on this PC, so don't keep passwords here."),
            Wrap(Btn("Save what's copied now", () => { _app.SaveClipboardSnapshot(); Rebuild(); }, true),
                 ConfirmBtn("Clear all", () => { _app.State.ClipboardItems.Clear(); _app.Save(); Rebuild(); })));
        var items = _app.State.ClipboardItems.ToList();
        Add(Section(items.Count == 0 ? "Saved" : $"Saved  ·  {items.Count} of 30"));
        if (items.Count == 0) Add(Muted("Nothing saved yet."));
        foreach (var entry in items)
        {
            var words = new StackPanel();
            var body = Text(entry.Content.Length > 180 ? entry.Content[..180] + "…" : entry.Content, 12);
            body.Margin = new Thickness(0); body.MaxHeight = 52; body.TextTrimming = TextTrimming.CharacterEllipsis;
            var when = Text(entry.CapturedUtc.ToLocalTime().ToString("g"), 10.5, "#8492B8"); when.Margin = new Thickness(0, 3, 0, 0);
            words.Children.Add(body); words.Children.Add(when);
            var copy = IconBtn(Icons.Copy, "Copy", () =>
            {
                try { System.Windows.Clipboard.SetText(entry.Content); Toast("Copied."); }
                catch (Exception ex) { Toast("Clipboard is busy: " + ex.Message); }
            });
            var remove = IconBtn(Icons.Trash, "Delete", () =>
            {
                _app.State.ClipboardItems.RemoveAll(x => x.Id == entry.Id); _app.Save(); Rebuild();
            });
            remove.Foreground = Brush("#8492B8");
            copy.VerticalAlignment = remove.VerticalAlignment = VerticalAlignment.Top;
            Add(Card(Row(words, copy, remove)));
        }
    }

    // ── Screenshots ─────────────────────────────────────────────────────────
    private void BuildScreenshot()
    {
        Add(Wrap(Btn("Select area", () => _ = CaptureAsync("region"), true),
                 Btn("This screen", () => _ = CaptureAsync("primary")),
                 Btn("All screens", () => _ = CaptureAsync("all"))),
            Muted("Floatly hides itself while capturing. Area selection works on the main screen."));
        Add(Section("Saved to Pictures\\Floatly Captures"));
        if (_lastCapture is not null) Add(Text("Last: " + Path.GetFileName(_lastCapture), 12, "#7FE0B4"));
        Add(Wrap(Btn("Open folder", () =>
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Floatly Captures");
            Directory.CreateDirectory(folder); Launch(folder);
        })));
    }

    private async Task CaptureAsync(string mode)
    {
        bool bubbleWasVisible = _app.Overlay.IsVisible;
        Hide(); _app.Overlay.Hide();
        await Task.Delay(300);
        string? path = null;
        try
        {
            using var screen = mode == "all" ? ScreenshotService.CaptureAllDisplays() : ScreenshotService.CapturePrimary();
            if (mode == "region")
            {
                var selector = new RegionSelectWindow(screen);
                if (selector.ShowDialog() == true)
                {
                    using var cropped = screen.Clone(selector.SelectedPixels, screen.PixelFormat);
                    path = ScreenshotService.Save(cropped);
                }
            }
            else path = ScreenshotService.Save(screen);
        }
        catch (Exception ex) { Toast("Screenshot failed: " + ex.Message); }
        finally
        {
            if (bubbleWasVisible) _app.Overlay.ShowAndActivate();
            Show(); Activate();
        }
        if (path is null) return;
        _lastCapture = path;
        if (_currentTool == "Screenshot") Rebuild();
        Toast("Screenshot saved.");
    }

    // ── Calculator ──────────────────────────────────────────────────────────
    private void BuildCalculator()
    {
        var expression = Input("0");
        expression.FontSize = 20; expression.Height = 46; expression.Margin = new Thickness(0, 0, 0, 4);
        var output = Text(" ", 15, "#B9C3E0");
        output.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        output.Margin = new Thickness(2, 0, 0, 8); output.FontWeight = FontWeights.SemiBold;
        var history = new StackPanel();
        void FillHistory()
        {
            history.Children.Clear();
            foreach (var item in _app.State.Calculations.Take(5))
            {
                var line = Muted(item.Expression + " = " + item.Result);
                line.Cursor = Cursors.Hand; line.ToolTip = "Use this result";
                line.MouseLeftButtonUp += (_, _) => { expression.Text = item.Result; expression.CaretIndex = expression.Text.Length; expression.Focus(); };
                history.Children.Add(line);
            }
        }
        void Calculate()
        {
            try
            {
                double answer = CalculatorEngine.Evaluate(expression.Text);
                string result = answer.ToString("G14", CultureInfo.InvariantCulture);
                output.Text = "= " + result;
                _app.State.Calculations.Insert(0, new CalculationEntry { Expression = expression.Text, Result = result });
                if (_app.State.Calculations.Count > 15) _app.State.Calculations.RemoveRange(15, _app.State.Calculations.Count - 15);
                _app.Save(); FillHistory();
            }
            catch (Exception ex) when (ex is FormatException or ArithmeticException)
            { output.Text = ex.Message; }
        }
        expression.KeyDown += (_, e) => { if (e.Key == Key.Enter) Calculate(); };
        var keys = new UniformGrid { Columns = 4, Margin = new Thickness(-3, 0, -3, 0) };
        foreach (string symbol in new[] { "7", "8", "9", "/", "4", "5", "6", "*", "1", "2", "3", "-", "0", ".", "(", ")", "C", "⌫", "+", "=" })
        {
            var key = Btn(symbol, () =>
            {
                switch (symbol)
                {
                    case "=": Calculate(); break;
                    case "C": expression.Clear(); output.Text = " "; break;
                    case "⌫": if (expression.Text.Length > 0) expression.Text = expression.Text[..^1]; break;
                    default: expression.Text += symbol; break;
                }
                expression.CaretIndex = expression.Text.Length;
            }, symbol == "=");
            key.Height = 40; key.Margin = new Thickness(3); key.FontSize = 14; key.Padding = new Thickness(0);
            keys.Children.Add(key);
        }
        Add(expression, output, keys, Section("Recent"), history);
        FillHistory();
    }

    // ── Settings ────────────────────────────────────────────────────────────
    private void BuildSettings()
    {
        Add(Section("Colour"));
        var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (name, code) in App.AccentThemes)
        {
            bool chosen = name == _app.State.Accent;
            var swatch = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Brush(code),
                BorderBrush = chosen ? Brushes.White : Brush("#30FFFFFF"), BorderThickness = new Thickness(chosen ? 2.5 : 1),
                Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, ToolTip = name
            };
            swatch.MouseLeftButtonUp += (_, _) => { _app.State.Accent = name; _app.ApplyTheme(); _app.Save(); Rebuild(); };
            swatches.Children.Add(swatch);
        }
        Add(swatches);
        Add(Section("Floating button"));
        var opacity = new Slider { Minimum = 0.35, Maximum = 1, Value = _app.State.BubbleOpacity };
        opacity.ValueChanged += (_, _) =>
        {
            _app.State.BubbleOpacity = opacity.Value;
            _app.Overlay.SetBubbleOpacity(opacity.Value);
            _app.SaveSoon();
        };
        var size = new Slider { Minimum = 48, Maximum = 72, TickFrequency = 4, IsSnapToTickEnabled = true, Value = _app.State.BubbleSize };
        size.ValueChanged += (_, _) => { _app.Overlay.SetBubbleSize((int)size.Value); _app.SaveSoon(); };
        Add(Muted("Opacity"), opacity, Muted("Size"), size);
        var motion = new CheckBox { Content = "Reduce motion", IsChecked = _app.State.ReduceMotion };
        motion.Click += (_, _) => { _app.State.ReduceMotion = motion.IsChecked == true; _app.Save(); };
        Add(motion);

        Add(Section("Windows"));
        var startup = new CheckBox { Content = "Start with Windows", IsChecked = StartupService.IsEnabled() };
        startup.Checked += (_, _) => ToggleStartup(true, startup);
        startup.Unchecked += (_, _) => ToggleStartup(false, startup);
        var hotkey = new CheckBox { Content = "Open with Ctrl + Alt + Space", IsChecked = _app.State.HotkeyEnabled };
        hotkey.Click += (_, _) =>
        {
            _app.State.HotkeyEnabled = hotkey.IsChecked == true; _app.Save();
            if (!_app.Overlay.RefreshHotkey()) Toast("Another program is already using Ctrl + Alt + Space.");
        };
        Add(startup, hotkey);

        Add(Section("Your data"),
            Muted("Everything stays on this PC, unencrypted, in a folder only your account uses."),
            Wrap(Btn("Open folder", () => Launch(_app.Storage.Folder)),
                 Btn("Export backup", () =>
                 {
                     try { _app.Save(); Toast("Backup saved: " + Path.GetFileName(_app.Storage.ExportBackup())); }
                     catch (Exception ex) { Toast("Backup failed: " + ex.Message); }
                 })));
    }

    private void ToggleStartup(bool enabled, CheckBox box)
    {
        if (_startupReverting) return;
        try { StartupService.SetEnabled(enabled); }
        catch (Exception ex)
        {
            Toast(ex.Message);
            _startupReverting = true; box.IsChecked = !enabled; _startupReverting = false;
        }
    }

    // ── System controls ─────────────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    private static void PressMediaKey(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, 2, UIntPtr.Zero);
    }

    private void BuildSystem()
    {
        Add(Section("Sound"),
            Wrap(Btn("Volume −", () => PressMediaKey(0xAE)), Btn("Volume +", () => PressMediaKey(0xAF)),
                 Btn("Mute", () => PressMediaKey(0xAD))));
        Add(Section("Windows settings"),
            Wrap(Btn("Wi-Fi", () => Launch("ms-settings:network-wifi")), Btn("Bluetooth", () => Launch("ms-settings:bluetooth")),
                 Btn("Display", () => Launch("ms-settings:display")), Btn("Night light", () => Launch("ms-settings:nightlight")),
                 Btn("Sound", () => Launch("ms-settings:sound")), Btn("Power", () => Launch("ms-settings:powersleep")),
                 Btn("All settings", () => Launch("ms-settings:"))));
    }

    // ── Window ──────────────────────────────────────────────────────────────
    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SavePendingNotes(); Hide();
    }
    protected override void OnClosed(EventArgs e)
    {
        SavePendingNotes();
        _noteAutosave.Stop(); _toastTimer.Stop();
        _app.Clock.Changed -= UpdateClocks;
        base.OnClosed(e);
    }
}
