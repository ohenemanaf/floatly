using System;
using System.IO;
using Floatly.Models;
using Floatly.Services;

int passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS  " + name);
    passed++;
}
Check("operator precedence", Math.Abs(CalculatorEngine.Evaluate("2 + 3 * 4") - 14) < 1e-9);
Check("parentheses", Math.Abs(CalculatorEngine.Evaluate("(2+3)*4") - 20) < 1e-9);
Check("unary minus", Math.Abs(CalculatorEngine.Evaluate("-7 / 2") + 3.5) < 1e-9);
Check("decimals", Math.Abs(CalculatorEngine.Evaluate("2.5 * 2") - 5) < 1e-9);
try { CalculatorEngine.Evaluate("1/0"); throw new Exception("Divide by zero not rejected"); }
catch (DivideByZeroException) { Check("division by zero blocked", true); }
try { CalculatorEngine.Evaluate("2 + unknown"); throw new Exception("Unrecognized input not rejected"); }
catch (FormatException) { Check("invalid input blocked", true); }
string folder = Path.Combine(Path.GetTempPath(), "FloatlyTest-" + Guid.NewGuid().ToString("N"));
try
{
    var storage = new StorageService(folder);
    var state = new AppState();
    state.Tasks.Add(new TaskItem { Title = "Test task", DueDate = DateTime.Today.AddDays(1) });
    state.Notes.Add(new NoteItem { Title = "Meeting", Content = "Draft" });
    storage.Save(state);
    var reloaded = storage.Load();
    Check("task persists", reloaded.Tasks.Count == 1 && reloaded.Tasks[0].Title == "Test task");
    Check("note persists", reloaded.Notes.Count == 1 && reloaded.Notes[0].Content == "Draft");
    reloaded.Notes[0].Content = "Edited";
    storage.Save(reloaded);
    Check("atomic update persists", storage.Load().Notes[0].Content == "Edited");
    File.WriteAllText(storage.StatePath, "invalid-json");
    Check("corrupt state recovers from backup", storage.Load().Notes[0].Content == "Draft");
    using var clock = new ClockService(25, 5, 3);
    Check("focus settings", clock.FocusMinutes == 25 && clock.BreakMinutes == 5);
    Check("focus history", clock.CompletedFocusSessions == 3);
    clock.SetTimer(3);
    Check("timer setting", clock.TimerRemaining.TotalSeconds == 180);
}
finally
{
    try { Directory.Delete(folder, recursive: true); } catch { }
}
Console.WriteLine($"All {passed} logic checks passed.");
