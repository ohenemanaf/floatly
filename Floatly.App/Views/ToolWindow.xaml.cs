using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Floatly.Models;
using Floatly.Services;

// Explicit WPF aliases prevent collisions when Windows Forms / System.Drawing are referenced.
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace Floatly.Views;

public partial class ToolWindow : Window
{
    private readonly App _app = (App)System.Windows.Application.Current;
    private readonly string[] _tools = { "Tasks", "Notes", "Launcher", "Timer", "Clipboard", "Screenshot", "Calculator", "Settings", "System", "Focus" };
    private string _currentTool = "Tasks";
    private string _taskFilter = "All";
    private string _noteSearch = "";
    private NoteItem? _editingNote;
    private TextBox? _noteEditor;
    private TextBox? _noteTitle;
    private readonly DispatcherTimer _noteAutosave = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private TextBlock? _timerDisplay;
    private TextBlock? _stopwatchDisplay;
    private TextBlock? _focusDisplay;
    private TextBlock? _sessionStats;
    private bool _startupReverting;

    public ToolWindow(string initialTool)
    {
        InitializeComponent();
        _noteAutosave.Tick += (_, _) => SavePendingNotes();
        _app.Clock.Changed += UpdateClocks;
        ShowTool(initialTool);
    }

    public void ShowTool(string tool)
    {
        if (!_tools.Contains(tool)) tool = "Tasks";
        SavePendingNotes(releaseEditor: true);
        _currentTool = tool;
        _timerDisplay = null; _stopwatchDisplay = null; _focusDisplay = null; _sessionStats = null;
        Navigation.Children.Clear();
        foreach (var name in _tools)
        {
            var button = MakeButton(ToolIcon(name) + "   " + name, () => ShowTool(name));
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 6);
            button.Background = name == tool ? (Brush)_app.Resources["AccentBrush"] : Brush("#272D42");
            Navigation.Children.Add(button);
        }
        ContentPanel.Children.Clear();
        Heading(tool, tool switch
        {
            "Tasks" => "Stay on top of what matters.",
            "Notes" => "Ideas captured instantly, saved locally.",
            "Launcher" => "Your favorite apps, files, folders and sites.",
            "Timer" => "A timer that keeps going when the panel is hidden.",
            "Clipboard" => "Your clipboard, on your terms.",
            "Screenshot" => "Capture locally. Nothing is uploaded.",
            "Calculator" => "Instant calculations, no web required.",
            "Settings" => "Make Floatly your own.",
            "System" => "Useful Windows controls in one place.",
            "Focus" => "Make time for focused work and breaks.",
            _ => ""
        });
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
    }

    private static string ToolIcon(string name) => name switch
    {
        "Tasks" => "✓", "Notes" => "✎", "Launcher" => "↗", "Timer" => "◷", "Clipboard" => "▤",
        "Screenshot" => "▣", "Calculator" => "=", "Settings" => "⚙", "System" => "♫", "Focus" => "◎", _ => "•"
    };

    private static SolidColorBrush Brush(string code) => (SolidColorBrush)new BrushConverter().ConvertFromString(code)!;
    private static TextBlock Label(string text, int size = 13, string color = "#DDE2F2") => new()
    {
        Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 0, 9)
    };
    private static TextBox Input(string text = "", int height = 39) => new()
    {
        Text = text, Height = height, MinWidth = 70, Margin = new Thickness(0, 0, 0, 9)
    };
    private static Button MakeButton(string title, Action action, bool primary = false)
    {
        var button = new Button
        {
            Content = title,
            Style = (Style)System.Windows.Application.Current.FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            Margin = new Thickness(0, 0, 7, 9)
        };
        button.Click += (_, _) => action();
        return button;
    }
    private void AddButtons(params Button[] buttons)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 3, 0, 11) };
        foreach (var b in buttons) panel.Children.Add(b);
        ContentPanel.Children.Add(panel);
    }
    private void Heading(string title, string subtitle)
    {
        ContentPanel.Children.Add(new TextBlock
        {
            Text = title, FontSize = 25, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 5)
        });
        ContentPanel.Children.Add(Label(subtitle, 12, "#9CA8C5"));
    }
    private static string Format(TimeSpan span)
    {
        int hours = (int)span.TotalHours;
        return $"{hours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }
    private static TextBlock ClockLabel(string value) => new()
    {
        Text = value, FontSize = 35, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
        Margin = new Thickness(0, 7, 0, 18), FontFamily = new FontFamily("Consolas")
    };
    private static bool Confirm(string message) => MessageBox.Show(message, "Floatly", MessageBoxButton.YesNo,
        MessageBoxImage.Question) == MessageBoxResult.Yes;
    private static void Error(string message) => MessageBox.Show(message, "Floatly", MessageBoxButton.OK, MessageBoxImage.Warning);

    // ── Tasks ───────────────────────────────────────────────────────────────
    private void BuildTasks()
    {
        var input = Input();
        input.ToolTip = "Write a task";
        ContentPanel.Children.Add(Label("Add a task", 13));
        ContentPanel.Children.Add(input);
        var due = new DatePicker { SelectedDate = null, Margin = new Thickness(0, 0, 0, 9) };
        ContentPanel.Children.Add(Label("Due date (optional)", 12, "#AEB8D2"));
        ContentPanel.Children.Add(due);
        var priority = new ComboBox { ItemsSource = new[] { "Normal", "Important", "Urgent" }, SelectedIndex = 0 };
        ContentPanel.Children.Add(Label("Priority", 12, "#AEB8D2"));
        ContentPanel.Children.Add(priority);
        void Add()
        {
            string name = input.Text.Trim();
            if (name.Length == 0) { Error("Enter a task first."); return; }
            _app.State.Tasks.Insert(0, new TaskItem
            {
                Title = name.Length > 250 ? name[..250] : name,
                DueDate = due.SelectedDate,
                Priority = priority.SelectedItem?.ToString() ?? "Normal"
            });
            _app.Save(); ShowTool("Tasks");
        }
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) Add(); };
        AddButtons(MakeButton("+ Add task", Add, true));
        var filter = new ComboBox { ItemsSource = new[] { "All", "Active", "Completed", "Due today", "Overdue" },
            SelectedItem = _taskFilter };
        filter.SelectionChanged += (_, _) =>
        {
            if (filter.SelectedItem is string f && _taskFilter != f) { _taskFilter = f; ShowTool("Tasks"); }
        };
        ContentPanel.Children.Add(filter);
        var list = _app.State.Tasks.OrderBy(t => t.Done).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ToList();
        var today = DateTime.Today;
        list = list.Where(t => _taskFilter switch
        {
            "Active" => !t.Done,
            "Completed" => t.Done,
            "Due today" => !t.Done && t.DueDate?.Date == today,
            "Overdue" => !t.Done && t.DueDate?.Date < today,
            _ => true
        }).ToList();
        int complete = _app.State.Tasks.Count(t => t.Done);
        ContentPanel.Children.Add(Label($"{complete}/{_app.State.Tasks.Count} completed   •   {list.Count} shown", 12, "#9EABCD"));
        if (list.Count == 0) ContentPanel.Children.Add(Label("Nothing here yet. Add something above!", 13, "#9EABCD"));
        foreach (var task in list)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var check = new CheckBox { IsChecked = task.Done, Content = task.Title,
                FontSize = 13 };
            check.Checked += (_, _) => { task.Done = true; _app.Save(); ShowTool("Tasks"); };
            check.Unchecked += (_, _) => { task.Done = false; _app.Save(); ShowTool("Tasks"); };
            row.Children.Add(check);
            string dueText = task.DueDate?.ToString("dd MMM yyyy") ?? "No deadline";
            string color = task.DueDate?.Date < today && !task.Done ? "#FF928F" : "#9CA8C5";
            row.Children.Add(Label($"{task.Priority}   •   {dueText}", 11, color));
            row.Children.Add(MakeButton("Delete", () =>
            {
                _app.State.Tasks.RemoveAll(t => t.Id == task.Id); _app.Save(); ShowTool("Tasks");
            }));
            ContentPanel.Children.Add(Card(row));
        }
    }

    private static Border Card(UIElement content) => new()
    {
        Child = content, Background = Brush("#23293B"), Padding = new Thickness(12),
        CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 8),
        BorderBrush = Brush("#3D445D"), BorderThickness = new Thickness(0.8)
    };

    // ── Notes ───────────────────────────────────────────────────────────────
    private void SavePendingNotes(bool releaseEditor = false)
    {
        _noteAutosave.Stop();
        if (_editingNote is null || _noteEditor is null || _noteTitle is null) return;
        if (_app.State.Notes.Any(n => n.Id == _editingNote.Id))
        {
            string title = _noteTitle.Text.Trim();
            string body = _noteEditor.Text;
            if (_editingNote.Title != title || _editingNote.Content != body)
            {
                _editingNote.Title = title.Length == 0 ? "Untitled note" : title;
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
        AddButtons(MakeButton("+ New note", () =>
        {
            _app.State.Notes.Insert(0, new NoteItem { Title = "Untitled note" });
            _app.Save(); ShowTool("Notes");
        }, true));
        var search = Input(_noteSearch);
        search.ToolTip = "Search note titles and text. Press Enter.";
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { _noteSearch = search.Text.Trim(); ShowTool("Notes"); }
        };
        ContentPanel.Children.Add(Label("Search notes (press Enter)", 12, "#AEB8D2"));
        ContentPanel.Children.Add(search);
        var matches = _app.State.Notes.Where(n => string.IsNullOrWhiteSpace(_noteSearch)
            || n.Title.Contains(_noteSearch, StringComparison.OrdinalIgnoreCase)
            || n.Content.Contains(_noteSearch, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.ModifiedUtc).ToList();
        if (matches.Count == 0) ContentPanel.Children.Add(Label("No matching notes.", 12));
        var choose = new ComboBox { ItemsSource = matches,
            DisplayMemberPath = nameof(NoteItem.Title), SelectedIndex = matches.Count > 0 ? 0 : -1 };
        ContentPanel.Children.Add(Label($"{_app.State.Notes.Count} saved notes", 12, "#AEB8D2"));
        ContentPanel.Children.Add(choose);
        var holder = new StackPanel();
        ContentPanel.Children.Add(holder);
        void Edit(NoteItem? selected)
        {
            SavePendingNotes(releaseEditor: true);
            holder.Children.Clear();
            if (selected is null) return;
            _editingNote = selected;
            _noteTitle = Input(selected.Title);
            holder.Children.Add(Label("Title", 12));
            holder.Children.Add(_noteTitle);
            _noteEditor = new TextBox
            {
                Text = selected.Content, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 195, Padding = new Thickness(12), FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            _noteTitle.TextChanged += (_, _) => { _noteAutosave.Stop(); _noteAutosave.Start(); };
            _noteEditor.TextChanged += (_, _) => { _noteAutosave.Stop(); _noteAutosave.Start(); };
            holder.Children.Add(Label("Content", 12));
            holder.Children.Add(_noteEditor);
            var actions = new WrapPanel();
            actions.Children.Add(MakeButton("Save now", () =>
            {
                SavePendingNotes(); _app.Notify("Note saved", "Your note was saved locally.");
            }, true));
            actions.Children.Add(MakeButton("Delete note", () =>
            {
                if (!Confirm("Delete this note permanently?")) return;
                _app.State.Notes.RemoveAll(n => n.Id == selected.Id);
                _editingNote = null; _noteTitle = null; _noteEditor = null;
                _app.Save(); ShowTool("Notes");
            }));
            holder.Children.Add(actions);
            holder.Children.Add(Label("Automatically saved after typing stops.", 11, "#A1ACC7"));
        }
        choose.SelectionChanged += (_, _) => Edit(choose.SelectedItem as NoteItem);
        Edit(choose.SelectedItem as NoteItem);
    }

    // ── Launcher ────────────────────────────────────────────────────────────
    private void BuildLauncher()
    {
        ContentPanel.Children.Add(Label("Built-in Windows shortcuts", 13));
        AddButtons(
            MakeButton("Notepad", () => Launch("notepad.exe"), true),
            MakeButton("Explorer", () => Launch("explorer.exe")),
            MakeButton("Paint", () => Launch("mspaint.exe")));
        ContentPanel.Children.Add(Label("Your pinned shortcuts", 13));
        foreach (var entry in _app.State.Shortcuts.ToList())
        {
            var row = new WrapPanel();
            row.Children.Add(MakeButton("↗ " + entry.Name, () => Launch(entry.Target), true));
            row.Children.Add(MakeButton("Remove", () =>
            {
                _app.State.Shortcuts.RemoveAll(x => x.Id == entry.Id); _app.Save(); ShowTool("Launcher");
            }));
            ContentPanel.Children.Add(Card(row));
        }
        ContentPanel.Children.Add(Label("Add a shortcut", 14));
        var name = Input(); var target = Input();
        ContentPanel.Children.Add(Label("Shortcut name", 12)); ContentPanel.Children.Add(name);
        ContentPanel.Children.Add(Label("Path, folder, or https:// website", 12)); ContentPanel.Children.Add(target);
        AddButtons(
            MakeButton("Browse file", () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose an application, document or shortcut", Filter = "All files (*.*)|*.*" };
                if (dialog.ShowDialog(this) == true) { target.Text = dialog.FileName; if (name.Text == "") name.Text = Path.GetFileNameWithoutExtension(dialog.FileName); }
            }),
            MakeButton("Browse folder", () =>
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder" };
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { target.Text = dialog.SelectedPath; if (name.Text == "") name.Text = Path.GetFileName(dialog.SelectedPath); }
            }));
        AddButtons(MakeButton("+ Save shortcut", () =>
        {
            string n = name.Text.Trim(), path = target.Text.Trim();
            if (n.Length == 0 || path.Length == 0) { Error("Add a name and a path or URL."); return; }
            if (!(path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || File.Exists(path) || Directory.Exists(path)))
            { Error("Select an existing file or folder, or enter an http(s) URL."); return; }
            _app.State.Shortcuts.Add(new LauncherItem { Name = n[..Math.Min(n.Length, 80)], Target = path });
            _app.Save(); ShowTool("Launcher");
        }, true));
    }
    private static void Launch(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Error("Could not open the selected item: " + ex.Message); }
    }

    // ── Timer and stopwatch ─────────────────────────────────────────────────
    private void UpdateClocks()
    {
        if (_timerDisplay is not null) _timerDisplay.Text = Format(_app.Clock.TimerRemaining);
        if (_stopwatchDisplay is not null) _stopwatchDisplay.Text = Format(_app.Clock.StopwatchElapsed);
        if (_focusDisplay is not null) _focusDisplay.Text = Format(_app.Clock.FocusRemaining);
        if (_sessionStats is not null) _sessionStats.Text = $"Completed focus sessions: {_app.Clock.CompletedFocusSessions}";
    }

    private void BuildTimer()
    {
        ContentPanel.Children.Add(Label("Countdown", 15, "#BEB4FF"));
        _timerDisplay = ClockLabel(Format(_app.Clock.TimerRemaining));
        ContentPanel.Children.Add(_timerDisplay);
        var minutes = Input("5");
        ContentPanel.Children.Add(Label("Minutes (1–180)", 12));
        ContentPanel.Children.Add(minutes);
        AddButtons(
            MakeButton(_app.Clock.TimerRunning ? "Pause countdown" : "Start countdown", () =>
            {
                _app.Clock.ToggleTimer(); ShowTool("Timer");
            }, true),
            MakeButton("Set / Reset", () =>
            {
                if (!int.TryParse(minutes.Text, out int n) || n < 1 || n > 180) { Error("Enter between 1 and 180 minutes."); return; }
                _app.Clock.SetTimer(n); ShowTool("Timer");
            }));
        AddButtons(
            MakeButton("5 min", () => { _app.Clock.SetTimer(5); ShowTool("Timer"); }),
            MakeButton("15 min", () => { _app.Clock.SetTimer(15); ShowTool("Timer"); }),
            MakeButton("30 min", () => { _app.Clock.SetTimer(30); ShowTool("Timer"); }));
        ContentPanel.Children.Add(Label("Stopwatch", 15, "#BEB4FF"));
        _stopwatchDisplay = ClockLabel(Format(_app.Clock.StopwatchElapsed));
        ContentPanel.Children.Add(_stopwatchDisplay);
        AddButtons(
            MakeButton(_app.Clock.StopwatchRunning ? "Pause stopwatch" : "Start stopwatch", () =>
            { _app.Clock.ToggleStopwatch(); ShowTool("Timer"); }, true),
            MakeButton("Reset stopwatch", () => { _app.Clock.ResetStopwatch(); ShowTool("Timer"); }));
        ContentPanel.Children.Add(Label("Timers continue while Floatly is running, even if you close this panel.", 12, "#A8B2CC"));
    }

    // ── Clipboard (explicit capture or SESSION-ONLY opt in) ────────────────
    private void BuildClipboard()
    {
        ContentPanel.Children.Add(Label("Nothing is collected automatically unless you enable the option below. Saved text is stored as readable local data. Never save passwords or secrets.", 12, "#B3BED7"));
        var listen = new CheckBox { Content = "Automatically save copied text for this session", IsChecked = _app.Overlay.ClipboardListening };
        listen.Checked += (_, _) =>
        {
            if (!Confirm("Automatic clipboard capture may include passwords, private messages, or other sensitive text. Enable until Floatly exits?"))
            { listen.IsChecked = false; return; }
            if (!_app.Overlay.SetClipboardListening(true)) { Error("Could not start clipboard monitoring."); listen.IsChecked = false; }
        };
        listen.Unchecked += (_, _) => _app.Overlay.SetClipboardListening(false);
        ContentPanel.Children.Add(listen);
        AddButtons(MakeButton("Save current copied text", () =>
        {
            _app.SaveClipboardSnapshot(); ShowTool("Clipboard");
        }, true), MakeButton("Clear saved entries", () =>
        {
            if (!Confirm("Permanently delete all saved clipboard entries?")) return;
            _app.State.ClipboardItems.Clear(); _app.Save(); ShowTool("Clipboard");
        }));
        ContentPanel.Children.Add(Label($"{_app.State.ClipboardItems.Count} saved text snippets (up to 30)", 12, "#9EAAC7"));
        foreach (var entry in _app.State.ClipboardItems.ToList())
        {
            var stack = new StackPanel();
            stack.Children.Add(Label(entry.Content.Length > 180 ? entry.Content[..180] + "…" : entry.Content, 12));
            stack.Children.Add(Label(entry.CapturedUtc.ToLocalTime().ToString("g"), 10, "#9EAAC7"));
            var actions = new WrapPanel();
            actions.Children.Add(MakeButton("Copy", () =>
            {
                try { System.Windows.Clipboard.SetText(entry.Content); }
                catch (Exception ex) { Error("Clipboard is currently unavailable: " + ex.Message); }
            }, true));
            actions.Children.Add(MakeButton("Delete", () =>
            {
                _app.State.ClipboardItems.RemoveAll(x => x.Id == entry.Id); _app.Save(); ShowTool("Clipboard");
            }));
            stack.Children.Add(actions);
            ContentPanel.Children.Add(Card(stack));
        }
    }

    // ── Screenshots ─────────────────────────────────────────────────────────
    private void BuildScreenshot()
    {
        ContentPanel.Children.Add(Label("Choose exactly what to capture. Floatly hides itself before the screenshot and restores afterward.", 13));
        AddButtons(
            MakeButton("Select region", () => _ = CaptureAsync("region"), true),
            MakeButton("Primary monitor", () => _ = CaptureAsync("primary")),
            MakeButton("All monitors", () => _ = CaptureAsync("all")));
        AddButtons(MakeButton("Open screenshot folder", () =>
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var folder = Path.Combine(pictures, "Floatly Captures");
            Directory.CreateDirectory(folder); Launch(folder);
        }));
        ContentPanel.Children.Add(Label("Screen images are saved as PNG files in Pictures\\Floatly Captures. Region selection uses the primary monitor.", 12, "#9EAAC7"));
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
        catch (Exception ex) { Error("Screenshot failed: " + ex.Message); }
        finally
        {
            if (bubbleWasVisible) _app.Overlay.ShowAndActivate();
            Show(); Activate();
        }
        if (path is not null)
        {
            _app.Notify("Screenshot saved", Path.GetFileName(path));
            MessageBox.Show("Screenshot saved to:\n" + path, "Floatly", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ── Calculator ──────────────────────────────────────────────────────────
    private void BuildCalculator()
    {
        var expression = Input();
        expression.FontSize = 20;
        expression.ToolTip = "Enter arithmetic using + - * / and parentheses";
        ContentPanel.Children.Add(expression);
        var output = Label("Enter a calculation", 19, "#BBAEFF");
        ContentPanel.Children.Add(output);
        void Calculate()
        {
            try
            {
                double answer = CalculatorEngine.Evaluate(expression.Text);
                string result = answer.ToString("G14", CultureInfo.InvariantCulture);
                output.Text = "= " + result;
                _app.State.Calculations.Insert(0, new CalculationEntry { Expression = expression.Text, Result = result });
                if (_app.State.Calculations.Count > 15) _app.State.Calculations.RemoveRange(15, _app.State.Calculations.Count - 15);
                _app.Save();
            }
            catch (Exception ex) when (ex is FormatException or ArithmeticException)
            { output.Text = ex.Message; }
        }
        expression.KeyDown += (_, e) => { if (e.Key == Key.Enter) Calculate(); };
        string[][] buttons =
        {
            new[] { "7", "8", "9", "/" },
            new[] { "4", "5", "6", "*" },
            new[] { "1", "2", "3", "-" },
            new[] { "0", ".", "(", ")" },
            new[] { "C", "⌫", "+", "=" }
        };
        foreach (var line in buttons)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var key in line)
            {
                string symbol = key;
                var button = MakeButton(symbol, () =>
                {
                    switch (symbol)
                    {
                        case "=": Calculate(); break;
                        case "C": expression.Clear(); output.Text = "Enter a calculation"; break;
                        case "⌫": if (expression.Text.Length > 0) expression.Text = expression.Text[..^1]; break;
                        default: expression.Text += symbol; break;
                    }
                    expression.CaretIndex = expression.Text.Length;
                }, symbol == "=");
                button.Width = 72; button.Height = 43;
                panel.Children.Add(button);
            }
            ContentPanel.Children.Add(panel);
        }
        ContentPanel.Children.Add(Label("Recent calculations", 13));
        foreach (var item in _app.State.Calculations.Take(5))
            ContentPanel.Children.Add(Label(item.Expression + " = " + item.Result, 12, "#9EAAC7"));
    }

    // ── Settings ────────────────────────────────────────────────────────────
    private void BuildSettings()
    {
        ContentPanel.Children.Add(Label("Appearance", 16, "#BBAEFF"));
        var color = new ComboBox { ItemsSource = App.AccentThemes.Select(x => x.Name).ToList(), SelectedItem = _app.State.Accent };
        color.SelectionChanged += (_, _) =>
        {
            if (color.SelectedItem is string theme)
            { _app.State.Accent = theme; _app.ApplyTheme(); _app.Save(); }
        };
        ContentPanel.Children.Add(Label("Accent color", 12)); ContentPanel.Children.Add(color);
        var opacity = new Slider { Minimum = 0.35, Maximum = 1, Value = _app.State.BubbleOpacity,
            Margin = new Thickness(0, 0, 0, 13) };
        opacity.ValueChanged += (_, _) =>
        {
            _app.State.BubbleOpacity = opacity.Value;
            _app.Overlay.SetBubbleOpacity(opacity.Value);
            _app.Save();
        };
        ContentPanel.Children.Add(Label("Floating button opacity", 12)); ContentPanel.Children.Add(opacity);
        var size = new Slider { Minimum = 48, Maximum = 72, TickFrequency = 4, IsSnapToTickEnabled = true,
            Value = _app.State.BubbleSize, Margin = new Thickness(0, 0, 0, 13) };
        size.ValueChanged += (_, _) => { _app.Overlay.SetBubbleSize((int)size.Value); _app.Save(); };
        ContentPanel.Children.Add(Label("Floating button size", 12)); ContentPanel.Children.Add(size);
        var motion = new CheckBox { Content = "Reduce menu animations", IsChecked = _app.State.ReduceMotion };
        motion.Checked += (_, _) => { _app.State.ReduceMotion = true; _app.Save(); };
        motion.Unchecked += (_, _) => { _app.State.ReduceMotion = false; _app.Save(); };
        ContentPanel.Children.Add(motion);
        ContentPanel.Children.Add(Label("Windows integration", 16, "#BBAEFF"));
        var startup = new CheckBox { Content = "Start automatically after Windows sign-in", IsChecked = StartupService.IsEnabled() };
        startup.Checked += (_, _) => ToggleStartup(true, startup);
        startup.Unchecked += (_, _) => ToggleStartup(false, startup);
        ContentPanel.Children.Add(startup);
        var hotkey = new CheckBox { Content = "Ctrl + Alt + Space opens Floatly", IsChecked = _app.State.HotkeyEnabled };
        hotkey.Checked += (_, _) => { _app.State.HotkeyEnabled = true; _app.Overlay.RefreshHotkey(); _app.Save(); };
        hotkey.Unchecked += (_, _) => { _app.State.HotkeyEnabled = false; _app.Overlay.RefreshHotkey(); _app.Save(); };
        ContentPanel.Children.Add(hotkey);
        ContentPanel.Children.Add(Label("Data and privacy", 16, "#BBAEFF"));
        ContentPanel.Children.Add(Label("All data stays on this computer in %LOCALAPPDATA%\\Floatly. It is not encrypted, so don't save passwords. Clipboard auto-capture always starts disabled.", 12, "#AAB5CE"));
        AddButtons(
            MakeButton("Open data folder", () => Launch(_app.Storage.Folder)),
            MakeButton("Export backup", () =>
            {
                try { _app.Save(); var file = _app.Storage.ExportBackup(); MessageBox.Show("Backup saved:\n" + file, "Floatly"); }
                catch (Exception ex) { Error("Backup failed: " + ex.Message); }
            }, true));
    }

    private void ToggleStartup(bool enabled, CheckBox box)
    {
        if (_startupReverting) return;
        try { StartupService.SetEnabled(enabled); }
        catch (Exception ex)
        {
            Error("Unable to change startup setting: " + ex.Message);
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
        ContentPanel.Children.Add(Label("Sound", 16, "#BBAEFF"));
        AddButtons(
            MakeButton("− Volume", () => PressMediaKey(0xAE)),
            MakeButton("+ Volume", () => PressMediaKey(0xAF)),
            MakeButton("Mute / Unmute", () => PressMediaKey(0xAD), true));
        ContentPanel.Children.Add(Label("Windows quick settings", 16, "#BBAEFF"));
        AddButtons(
            MakeButton("Sound", () => Launch("ms-settings:sound")),
            MakeButton("Display", () => Launch("ms-settings:display")),
            MakeButton("Wi-Fi", () => Launch("ms-settings:network-wifi")),
            MakeButton("Bluetooth", () => Launch("ms-settings:bluetooth")),
            MakeButton("Power", () => Launch("ms-settings:powersleep")),
            MakeButton("Night light", () => Launch("ms-settings:nightlight")));
        AddButtons(
            MakeButton("Task Manager", () => Launch("taskmgr.exe")),
            MakeButton("Windows Settings", () => Launch("ms-settings:")));
        ContentPanel.Children.Add(Label("Hardware-specific options (such as monitor brightness) open Windows Settings instead of using unreliable hardware controls.", 12, "#9EABCD"));
    }

    // ── Focus sessions ──────────────────────────────────────────────────────
    private void BuildFocus()
    {
        ContentPanel.Children.Add(Label(_app.Clock.FocusIsBreak ? "Short break" : "Deep work session", 16, "#BBAEFF"));
        _focusDisplay = ClockLabel(Format(_app.Clock.FocusRemaining));
        ContentPanel.Children.Add(_focusDisplay);
        AddButtons(
            MakeButton(_app.Clock.FocusRunning ? "Pause session" : "Start session", () =>
            { _app.Clock.ToggleFocus(); ShowTool("Focus"); }, true),
            MakeButton("Reset", () => { _app.Clock.ResetFocus(); ShowTool("Focus"); }),
            MakeButton("Switch focus / break", () => { _app.Clock.SwitchFocusMode(); ShowTool("Focus"); }));
        _sessionStats = Label($"Completed focus sessions: {_app.Clock.CompletedFocusSessions}", 13, "#BBAEFF");
        ContentPanel.Children.Add(_sessionStats);
        ContentPanel.Children.Add(Label("Custom durations", 13));
        var focusInput = Input(_app.Clock.FocusMinutes.ToString());
        var breakInput = Input(_app.Clock.BreakMinutes.ToString());
        ContentPanel.Children.Add(Label("Work minutes (1–120)", 12)); ContentPanel.Children.Add(focusInput);
        ContentPanel.Children.Add(Label("Break minutes (1–60)", 12)); ContentPanel.Children.Add(breakInput);
        AddButtons(MakeButton("Save durations", () =>
        {
            if (!int.TryParse(focusInput.Text, out int focus) || focus is < 1 or > 120 ||
                !int.TryParse(breakInput.Text, out int rest) || rest is < 1 or > 60)
            { Error("Enter work minutes 1–120 and break minutes 1–60."); return; }
            _app.State.FocusMinutes = focus; _app.State.BreakMinutes = rest;
            _app.Clock.UpdateFocusDurations(focus, rest);
            _app.Save(); ShowTool("Focus");
        }, true));
        ContentPanel.Children.Add(Label("When a session ends, Floatly notifies you and prepares the next focus/break period. Timers work while the panel is hidden.", 12, "#A8B2CC"));
    }

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
        _noteAutosave.Stop();
        _app.Clock.Changed -= UpdateClocks;
        base.OnClosed(e);
    }
}
