using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace Floatly.Services;

// Owned by App, NOT by the tools window. Counts down correctly when panels are hidden.
public sealed class ClockService : IDisposable
{
    private readonly DispatcherTimer _pulse = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch _stopwatch = new();
    private TimeSpan _stopwatchAccumulated = TimeSpan.Zero;
    private TimeSpan _timerRemaining = TimeSpan.FromMinutes(5);
    private TimeSpan _focusRemaining = TimeSpan.FromMinutes(25);
    private DateTimeOffset? _timerDeadline;
    private DateTimeOffset? _focusDeadline;
    public int CompletedFocusSessions { get; private set; }
    public bool FocusIsBreak { get; private set; }
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public event Action? Changed;
    public event Action<string, string>? Alert;
    public event Action? FocusSessionCompleted;

    public ClockService(int focusMinutes, int breakMinutes, int completedSessions)
    {
        FocusMinutes = Math.Clamp(focusMinutes, 1, 120);
        BreakMinutes = Math.Clamp(breakMinutes, 1, 60);
        CompletedFocusSessions = Math.Max(0, completedSessions);
        _focusRemaining = TimeSpan.FromMinutes(FocusMinutes);
        _pulse.Tick += (_, _) => Tick();
        _pulse.Start();
    }

    public TimeSpan TimerRemaining => _timerDeadline is null ? _timerRemaining : Clamp(_timerDeadline.Value - DateTimeOffset.UtcNow);
    public bool TimerRunning => _timerDeadline.HasValue;
    public TimeSpan StopwatchElapsed => _stopwatchAccumulated + _stopwatch.Elapsed;
    public bool StopwatchRunning => _stopwatch.IsRunning;
    public TimeSpan FocusRemaining => _focusDeadline is null ? _focusRemaining : Clamp(_focusDeadline.Value - DateTimeOffset.UtcNow);
    public bool FocusRunning => _focusDeadline.HasValue;
    private static TimeSpan Clamp(TimeSpan x) => x < TimeSpan.Zero ? TimeSpan.Zero : x;

    private void Tick()
    {
        if (_timerDeadline.HasValue && DateTimeOffset.UtcNow >= _timerDeadline.Value)
        {
            _timerDeadline = null;
            _timerRemaining = TimeSpan.Zero;
            Alert?.Invoke("Countdown complete", "Your Floatly timer finished.");
        }
        if (_focusDeadline.HasValue && DateTimeOffset.UtcNow >= _focusDeadline.Value)
        {
            _focusDeadline = null;
            if (!FocusIsBreak)
            {
                CompletedFocusSessions++;
                FocusSessionCompleted?.Invoke();
            }
            FocusIsBreak = !FocusIsBreak;
            _focusRemaining = TimeSpan.FromMinutes(FocusIsBreak ? BreakMinutes : FocusMinutes);
            Alert?.Invoke("Focus session finished", FocusIsBreak ? "Focus complete. Take a short break." : "Break complete. Ready to focus?");
        }
        Changed?.Invoke();
    }

    public void SetTimer(int minutes)
    {
        if (minutes is < 1 or > 180) throw new ArgumentOutOfRangeException(nameof(minutes));
        _timerRemaining = TimeSpan.FromMinutes(minutes);
        _timerDeadline = null;
        Changed?.Invoke();
    }
    public void ToggleTimer()
    {
        if (_timerDeadline is not null)
        {
            _timerRemaining = TimerRemaining;
            _timerDeadline = null;
        }
        else if (_timerRemaining > TimeSpan.Zero)
            _timerDeadline = DateTimeOffset.UtcNow + _timerRemaining;
        Changed?.Invoke();
    }
    public void ToggleStopwatch()
    {
        if (_stopwatch.IsRunning) { _stopwatchAccumulated += _stopwatch.Elapsed; _stopwatch.Reset(); }
        else _stopwatch.Restart();
        Changed?.Invoke();
    }
    public void ResetStopwatch()
    {
        _stopwatch.Reset(); _stopwatchAccumulated = TimeSpan.Zero; Changed?.Invoke();
    }
    public void ToggleFocus()
    {
        if (_focusDeadline is not null)
        {
            _focusRemaining = FocusRemaining;
            _focusDeadline = null;
        }
        else if (_focusRemaining > TimeSpan.Zero)
            _focusDeadline = DateTimeOffset.UtcNow + _focusRemaining;
        Changed?.Invoke();
    }
    public void ResetFocus()
    {
        _focusDeadline = null;
        _focusRemaining = TimeSpan.FromMinutes(FocusIsBreak ? BreakMinutes : FocusMinutes);
        Changed?.Invoke();
    }
    public void SwitchFocusMode()
    {
        FocusIsBreak = !FocusIsBreak;
        ResetFocus();
    }
    public void UpdateFocusDurations(int focus, int rest)
    {
        FocusMinutes = Math.Clamp(focus, 1, 120);
        BreakMinutes = Math.Clamp(rest, 1, 60);
        ResetFocus();
    }
    public void Dispose() => _pulse.Stop();
}
