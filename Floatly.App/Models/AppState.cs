using System;
using System.Collections.Generic;

namespace Floatly.Models;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 2;
    public List<TaskItem> Tasks { get; set; } = new();
    public string QuickNote { get; set; } = ""; // Compatibility with early starter version.
    public List<NoteItem> Notes { get; set; } = new();
    public List<LauncherItem> Shortcuts { get; set; } = new();
    public List<ClipboardEntry> ClipboardItems { get; set; } = new();
    public List<string> ClipboardHistory { get; set; } = new(); // Legacy migration only.
    public List<CalculationEntry> Calculations { get; set; } = new();
    public double BubbleLeft { get; set; } = -1;
    public double BubbleTop { get; set; } = -1;
    public double BubbleOpacity { get; set; } = 0.96;
    public int BubbleSize { get; set; } = 64;
    public string Accent { get; set; } = "Violet";
    public bool ReduceMotion { get; set; }
    public bool HotkeyEnabled { get; set; } = true;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public int CompletedFocusSessions { get; set; }
}

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public bool Done { get; set; }
    public DateTime? DueDate { get; set; }
    public string Priority { get; set; } = "Normal";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class NoteItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled note";
    public string Content { get; set; } = "";
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LauncherItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
}

public sealed class ClipboardEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = "";
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CalculationEntry
{
    public string Expression { get; set; } = "";
    public string Result { get; set; } = "";
}
