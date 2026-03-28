using System.Text;
using System.Windows;
using NotchyWindows.Models;

namespace NotchyWindows.Services;

public static class StatusParser
{
    private static readonly Dictionary<Guid, DateTime> _lastWorkingTime = new();
    private static readonly Dictionary<Guid, System.Timers.Timer> _completionTimers = new();

    // Claude spinner characters — only the decorative ones, NOT middle dot (·) which appears in normal text
    private static readonly string[] SpinnerPatterns = { "✢", "✳", "✶", "✻", "✽" };
    // Braille spinner chars from terminal raw output
    private static readonly char[] BrailleSpinnerChars = { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };

    /// <summary>Parse raw ConPTY output for fast-path status detection.</summary>
    public static void Parse(Guid sessionId, byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);

        // Fast-path: detect working from raw output
        if (text.Contains("esc to interrupt", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Clauding", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("thinking with", StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatus(sessionId, TerminalStatus.Working);
        }
    }

    /// <summary>Parse clean visible text from xterm.js buffer (no ANSI codes).</summary>
    public static void ParseVisibleText(Guid sessionId, string visibleText)
    {
        var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null) return;

        // Get last ~20 non-blank lines for classification
        var lines = visibleText.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .TakeLast(20)
            .ToArray();

        var relevantText = string.Join("\n", lines);
        var newStatus = ClassifyVisibleText(relevantText, session.Status);

        UpdateStatus(sessionId, newStatus);
    }

    private static TerminalStatus ClassifyVisibleText(string text, TerminalStatus current)
    {
        // Working signals — any of these mean Claude is actively processing
        if (text.Contains("esc to interrupt", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Working;
        if (text.Contains("Clauding", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Working;
        if (text.Contains("thinking with", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Working;
        if (text.Contains("Reading") && text.Contains("file"))
            return TerminalStatus.Working;
        if (text.Contains("Writing") && text.Contains("file"))
            return TerminalStatus.Working;
        if (text.Contains("ctrl+o to expand", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Working;

        // WaitingForInput: Claude asking for user decision
        if (text.Contains("Esc to cancel", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.WaitingForInput;
        if (text.Contains("Do you want to proceed", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.WaitingForInput;
        if (text.Contains("Yes / No", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.WaitingForInput;

        // Claude prompt with option numbers (❯ followed by digit)
        if (text.Contains("❯"))
        {
            var lines = text.Split('\n');
            foreach (var line in lines.TakeLast(5))
            {
                if (line.Contains("❯") && line.Any(char.IsDigit))
                    return TerminalStatus.WaitingForInput;
            }
        }

        // Interrupted
        if (text.Contains("Interrupted", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("esc to interrupt", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Interrupted;

        return TerminalStatus.Idle;
    }

    private static void UpdateStatus(Guid sessionId, TerminalStatus newStatus)
    {
        var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null) return;

        var oldStatus = session.Status;
        if (newStatus == oldStatus) return;

        // Sticky: don't drop from Working to Idle too quickly (polling can miss spinner)
        if (oldStatus == TerminalStatus.Working && newStatus == TerminalStatus.Idle)
        {
            if (_lastWorkingTime.TryGetValue(sessionId, out var lastWork) &&
                (DateTime.UtcNow - lastWork).TotalSeconds < 2)
                return; // stay Working for at least 2s
        }

        if (newStatus == TerminalStatus.Working)
        {
            _lastWorkingTime[sessionId] = DateTime.UtcNow;
            CancelCompletionTimer(sessionId);
        }

        // Working → Idle: delay 3s then trigger TaskCompleted (only if worked >10s)
        if (oldStatus == TerminalStatus.Working && newStatus == TerminalStatus.Idle)
        {
            if (_lastWorkingTime.TryGetValue(sessionId, out var start) &&
                (DateTime.UtcNow - start).TotalSeconds > 10)
            {
                StartCompletionTimer(sessionId);
                return;
            }
        }

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            session.Status = newStatus;

            // Sound notifications on key transitions
            if (newStatus == TerminalStatus.WaitingForInput)
                SoundPlayer.PlayWaitingForInput();
            else if (newStatus == TerminalStatus.TaskCompleted)
                SoundPlayer.PlayTaskCompleted();
        });
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
                    SoundPlayer.PlayTaskCompleted();

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
