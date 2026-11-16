using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Floatly.Models;

namespace Floatly.Services;

public sealed class StorageService
{
    public string Folder { get; }
    public StorageService(string? folder = null)
    {
        Folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Floatly");
    }
    public string StatePath => Path.Combine(Folder, "state.json");
    private string BackupPath => Path.Combine(Folder, "state.backup.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public AppState Load()
    {
        Directory.CreateDirectory(Folder);
        AppState? state = TryRead(StatePath);
        if (state is null && File.Exists(StatePath))
        {
            // Never silently overwrite unreadable user data.
            try { File.Copy(StatePath, Path.Combine(Folder, "state.corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            state = TryRead(BackupPath);
            if (state is not null)
            {
                // Restore the last good state before any future save rotates backups.
                try { File.Copy(BackupPath, StatePath, overwrite: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        state ??= new AppState();
        state.Tasks ??= new();
        state.Notes ??= new();
        state.Shortcuts ??= new();
        state.ClipboardItems ??= new();
        state.ClipboardHistory ??= new();
        state.Calculations ??= new();
        if (state.Notes.Count == 0 && !string.IsNullOrWhiteSpace(state.QuickNote))
            state.Notes.Add(new NoteItem { Title = "My first note", Content = state.QuickNote });
        if (state.ClipboardItems.Count == 0 && state.ClipboardHistory.Count > 0)
            state.ClipboardItems = state.ClipboardHistory.Select(x => new ClipboardEntry { Content = x }).ToList();
        state.BubbleOpacity = Math.Clamp(state.BubbleOpacity, 0.35, 1.0);
        state.BubbleSize = Math.Clamp(state.BubbleSize, 48, 72);
        state.FocusMinutes = Math.Clamp(state.FocusMinutes, 1, 120);
        state.BreakMinutes = Math.Clamp(state.BreakMinutes, 1, 60);
        return state;
    }

    private static AppState? TryRead(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), Options) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(Folder);
        var temp = StatePath + ".tmp";
        var json = JsonSerializer.Serialize(state, Options);
        File.WriteAllText(temp, json);
        if (File.Exists(StatePath))
            File.Replace(temp, StatePath, BackupPath, true);
        else
            File.Move(temp, StatePath);
    }

    public string ExportBackup()
    {
        Directory.CreateDirectory(Folder);
        string file = Path.Combine(Folder, "Floatly-export-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        File.Copy(StatePath, file, overwrite: false);
        return file;
    }
}
