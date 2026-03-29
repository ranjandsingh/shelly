using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Shelly.Models;

namespace Shelly.Services;

public static class StatusParser
{
    private static readonly Dictionary<Guid, DateTime> _lastWorkingTime = new();
    private static readonly Dictionary<Guid, System.Timers.Timer> _completionTimers = new();

    // Completion message: "✻ Cogitated for 4m 2s", "✻ Brewed for 6m 41s", etc.
    private static readonly Regex CompletionPattern = new(@"[✢✳✶✻✽].*\bfor\b.*\d+[ms]", RegexOptions.Compiled);

    /// <summary>Parse raw ConPTY output for fast-path status detection.</summary>
    public static void Parse(Guid sessionId, byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);

        var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null) return;

        // Fast-path: detect completion message ("✻ Cogitated for 4m 2s") immediately
        if (session.Status == TerminalStatus.Working && CompletionPattern.IsMatch(text))
        {
            MarkTaskCompleted(sessionId);
            return;
        }

        // Fast-path: detect START of working from raw output.
        // Only transition TO Working from Idle/TaskCompleted — don't override
        // WaitingForInput or other states (terminal redraws can contain stale indicators).
        if (session.Status is TerminalStatus.Idle or TerminalStatus.TaskCompleted)
        {
            if (text.Contains("esc to interrupt", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Clauding", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("thinking with", StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatus(sessionId, TerminalStatus.Working);
            }
        }
    }

    /// <summary>Parse clean visible text from xterm.js buffer (no ANSI codes).</summary>
    public static void ParseVisibleText(Guid sessionId, string visibleText)
    {
        var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null) return;

        var newStatus = ClassifyVisibleText(visibleText, session.Status);
        UpdateStatus(sessionId, newStatus);
    }

    private static TerminalStatus ClassifyVisibleText(string text, TerminalStatus current)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToArray();

        if (lines.Length == 0) return TerminalStatus.Idle;

        // Bottom 3 lines = status bar area (current state indicator)
        var bottom3 = string.Join("\n", lines.TakeLast(3));
        // Bottom 8 lines = multi-line prompts, approval dialogs
        var bottom8 = string.Join("\n", lines.TakeLast(8));

        // === INTERRUPTED (specific, check early) ===
        if (bottom8.Contains("Interrupted", StringComparison.OrdinalIgnoreCase) &&
            !bottom8.Contains("esc to interrupt", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Interrupted;

        // === WORKING: only from bottom 3 lines (status bar) ===
        // Stale "esc to interrupt" higher up is from a previous work phase.
        if (bottom3.Contains("esc to interrupt", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Working;
        if (bottom3.Contains("Clauding", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Working;
        if (bottom3.Contains("thinking with", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.Working;
        if (bottom3.Contains("Reading") && bottom3.Contains("file"))
            return TerminalStatus.Working;
        if (bottom3.Contains("Writing") && bottom3.Contains("file"))
            return TerminalStatus.Working;

        // === COMPLETED: "✻ Cogitated for 4m 2s" etc. ===
        // Spinner char + "for" + time duration = Claude just finished a task.
        // Check before WaitingForInput so stale approval text doesn't win.
        var bottom5 = string.Join("\n", lines.TakeLast(5));
        if (CompletionPattern.IsMatch(bottom5))
            return TerminalStatus.Idle;

        // === WAITING FOR INPUT: from bottom 8 lines ===
        // Edit approval
        if (bottom8.Contains("Esc to cancel", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.WaitingForInput;
        if (bottom8.Contains("Do you want to proceed", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.WaitingForInput;
        if (bottom8.Contains("Yes / No", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.WaitingForInput;
        // Tool permission prompts: "(Y)es / (N)o"
        if (bottom8.Contains("(Y)es", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.WaitingForInput;
        // Plan mode approval menu
        if (bottom8.Contains("Keep planning", StringComparison.OrdinalIgnoreCase))
            return TerminalStatus.WaitingForInput;
        // Selection menu with ❯ and numbered options (plan approval, etc.)
        foreach (var line in lines.TakeLast(8))
        {
            if (line.Contains("❯") && line.Any(char.IsDigit))
                return TerminalStatus.WaitingForInput;
        }

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

    /// <summary>Immediately mark a task as completed (bypasses the 3s delay timer).</summary>
    private static void MarkTaskCompleted(Guid sessionId)
    {
        CancelCompletionTimer(sessionId);
        var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null) return;

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            session.Status = TerminalStatus.TaskCompleted;
            SoundPlayer.PlayTaskCompleted();

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
