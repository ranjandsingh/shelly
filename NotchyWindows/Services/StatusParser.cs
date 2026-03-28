using System.Text;
using System.Windows;
using NotchyWindows.Models;

namespace NotchyWindows.Services;

public static class StatusParser
{
    private static readonly Dictionary<Guid, DateTime> _lastWorkingTime = new();
    private static readonly Dictionary<Guid, System.Timers.Timer> _completionTimers = new();
    private static readonly char[] SpinnerChars = { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };

    public static void Parse(Guid sessionId, byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null) return;

        var newStatus = Classify(text, session.Status);
        if (newStatus == session.Status) return;

        if (newStatus == TerminalStatus.Working)
        {
            _lastWorkingTime[sessionId] = DateTime.UtcNow;
            CancelCompletionTimer(sessionId);
        }

        if (session.Status == TerminalStatus.Working && newStatus == TerminalStatus.Idle)
        {
            // Check if worked long enough for a "task completed" transition
            if (_lastWorkingTime.TryGetValue(sessionId, out var start) &&
                (DateTime.UtcNow - start).TotalSeconds > 10)
            {
                StartCompletionTimer(sessionId);
                return;
            }
        }

        Application.Current.Dispatcher.InvokeAsync(() => session.Status = newStatus);
    }

    private static TerminalStatus Classify(string text, TerminalStatus current)
    {
        if (text.IndexOfAny(SpinnerChars) >= 0 || text.Contains("esc to interrupt", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Working;

        if (text.Contains("Esc to cancel", StringComparison.OrdinalIgnoreCase) ||
            (text.Contains("❯") && text.Any(char.IsDigit)))
            return TerminalStatus.WaitingForInput;

        if (text.Contains("Interrupted", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Interrupted;

        return TerminalStatus.Idle;
    }

    private static void StartCompletionTimer(Guid sessionId)
    {
        CancelCompletionTimer(sessionId);

        var timer = new System.Timers.Timer(3000) { AutoReset = false };
        timer.Elapsed += (_, _) =>
        {
            var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    session.Status = TerminalStatus.TaskCompleted;

                    // Auto-clear after 3 seconds
                    var clearTimer = new System.Timers.Timer(3000) { AutoReset = false };
                    clearTimer.Elapsed += (_, _) =>
                    {
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (session.Status == TerminalStatus.TaskCompleted)
                                session.Status = TerminalStatus.Idle;
                        });
                    };
                    clearTimer.Start();
                });
            }
        };
        timer.Start();
        _completionTimers[sessionId] = timer;
    }

    private static void CancelCompletionTimer(Guid sessionId)
    {
        if (_completionTimers.TryGetValue(sessionId, out var existing))
        {
            existing.Stop();
            existing.Dispose();
            _completionTimers.Remove(sessionId);
        }
    }
}
