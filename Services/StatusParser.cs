using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Shelly.Models;

namespace Shelly.Services;

public static class StatusParser
{
    private static readonly Dictionary<Guid, DateTime> _lastWorkingTime = new();
    private static readonly Dictionary<Guid, System.Timers.Timer> _completionTimers = new();
    private static readonly Dictionary<Guid, System.Timers.Timer> _soundConfirmTimers = new();

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
            Logger.Log("StatusParser: completion detected in raw output");
            MarkTaskCompleted(sessionId);
            return;
        }

        // Fast-path: detect START of working from raw output.
        // Only from Idle — raw output contains stale indicators during redraws,
        // so don't transition from TaskCompleted (causes sound loop).
        // The visible text parser handles TaskCompleted → Working reliably.
        if (session.Status is TerminalStatus.Idle)
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
        var bottom3Lines = lines.TakeLast(3).ToArray();
        var bottom3 = string.Join("\n", bottom3Lines);
        // Bottom 8 lines = multi-line prompts, approval dialogs
        var bottom8 = string.Join("\n", lines.TakeLast(8));

        TerminalStatus result;

        // === INTERRUPTED (specific, check early) ===
        if (bottom8.Contains("Interrupted", StringComparison.OrdinalIgnoreCase) &&
            !bottom8.Contains("esc to interrupt", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.Interrupted; goto done; }

        // === WORKING: only from bottom 3 lines (status bar) ===
        // Stale "esc to interrupt" higher up is from a previous work phase.
        if (bottom3.Contains("esc to interrupt", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.Working; goto done; }
        if (bottom3.Contains("Clauding", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.Working; goto done; }
        if (bottom3.Contains("thinking with", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.Working; goto done; }
        if (bottom3.Contains("Reading") && bottom3.Contains("file"))
        { result = TerminalStatus.Working; goto done; }
        if (bottom3.Contains("Writing") && bottom3.Contains("file"))
        { result = TerminalStatus.Working; goto done; }

        // === WAITING FOR INPUT: from bottom 8 lines ===
        // Check BEFORE completion — if user action is needed, that takes priority
        // over a completion message still visible on screen.
        if (bottom8.Contains("Esc to cancel", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.WaitingForInput; goto done; }
        if (bottom8.Contains("Do you want to proceed", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.WaitingForInput; goto done; }
        if (bottom8.Contains("Would you like to proceed", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.WaitingForInput; goto done; }
        if (bottom8.Contains("Do you want to make this edit", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.WaitingForInput; goto done; }
        if (bottom8.Contains("Yes / No", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.WaitingForInput; goto done; }
        // Tool permission prompts: "(Y)es / (N)o"
        if (bottom8.Contains("(Y)es", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.WaitingForInput; goto done; }
        // Plan mode approval menu
        if (bottom8.Contains("Keep planning", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.WaitingForInput; goto done; }
        // Plan execution approval
        if (bottom8.Contains("auto-accept edits", StringComparison.OrdinalIgnoreCase))
        { result = TerminalStatus.WaitingForInput; goto done; }
        // Selection menu with ❯ or ? selector and numbered options (plan approval, permission prompts, etc.)
        // Pattern: leading whitespace, selector char (❯ or ?), space, digit
        foreach (var line in lines.TakeLast(8))
        {
            var trimmed = line.TrimStart();
            if ((trimmed.StartsWith("❯") || trimmed.StartsWith("?")) &&
                trimmed.Length > 1 && trimmed.Substring(1).TrimStart().Any(char.IsDigit))
            { result = TerminalStatus.WaitingForInput; goto done; }
        }

        // === COMPLETED: "✻ Cogitated for 4m 2s" etc. ===
        // Spinner char + "for" + time duration = Claude just finished a task.
        // Only matches if no waiting-for-input prompt is visible.
        var bottom5 = string.Join("\n", lines.TakeLast(5));
        if (CompletionPattern.IsMatch(bottom5))
        { result = TerminalStatus.Idle; goto done; }

        result = TerminalStatus.Idle;

        done:
        if (result != current)
            Logger.Log($"StatusParser: {current} → {result}");
        return result;
    }

    private static void UpdateStatus(Guid sessionId, TerminalStatus newStatus)
    {
        var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null) return;

        var oldStatus = session.Status;
        if (newStatus == oldStatus) return;

        // Don't let polling clear TaskCompleted — the 3s clear timer handles that
        if (oldStatus == TerminalStatus.TaskCompleted && newStatus == TerminalStatus.Idle)
            return;

        // Sticky: don't drop from Working to Idle too quickly (polling can miss spinner)
        if (oldStatus == TerminalStatus.Working && newStatus == TerminalStatus.Idle)
        {
            if (_lastWorkingTime.TryGetValue(sessionId, out var lastWork) &&
                (DateTime.UtcNow - lastWork).TotalSeconds < 2)
                return;
        }

        if (newStatus == TerminalStatus.Working)
        {
            _lastWorkingTime[sessionId] = DateTime.UtcNow;
            CancelCompletionTimer(sessionId);
            CancelSoundConfirmation(sessionId);
        }

        // Working → Idle: delay 3s then trigger TaskCompleted (only if worked >10s)
        if (oldStatus == TerminalStatus.Working && newStatus == TerminalStatus.Idle)
        {
            if (_lastWorkingTime.TryGetValue(sessionId, out var start) &&
                (DateTime.UtcNow - start).TotalSeconds > 10)
            {
                if (!_completionTimers.ContainsKey(sessionId))
                {
                    Logger.Log($"StatusParser: starting completion timer (worked {(DateTime.UtcNow - start).TotalSeconds:F0}s)");
                    StartCompletionTimer(sessionId);
                }
                return;
            }
        }

        Logger.Log($"StatusParser: {oldStatus} → {newStatus}");

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            session.Status = newStatus;

            if (newStatus == TerminalStatus.TaskCompleted)
                ScheduleSoundConfirmation(sessionId);
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
            if (session.Status == TerminalStatus.TaskCompleted) return;
            session.Status = TerminalStatus.TaskCompleted;
            ScheduleSoundConfirmation(sessionId);
        });
    }

    /// <summary>Clear TaskCompleted status when the user views the session.</summary>
    public static void AcknowledgeCompletion(Guid sessionId)
    {
        CancelSoundConfirmation(sessionId);
        var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null) return;
        if (session.Status == TerminalStatus.TaskCompleted)
        {
            Logger.Log("StatusParser: TaskCompleted acknowledged");
            session.Status = TerminalStatus.Idle;
        }
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
                    if (session.Status == TerminalStatus.TaskCompleted) return;
                    session.Status = TerminalStatus.TaskCompleted;
                    ScheduleSoundConfirmation(sessionId);
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

    /// <summary>
    /// Schedule a sound to play after a confirmation delay.
    /// If the session is no longer TaskCompleted when the timer fires, the sound is skipped.
    /// This prevents false sounds from transient status blips during tab switch, panel expand, or typing.
    /// </summary>
    private static void ScheduleSoundConfirmation(Guid sessionId)
    {
        CancelSoundConfirmation(sessionId);

        var timer = new System.Timers.Timer(1500) { AutoReset = false };
        timer.Elapsed += (_, _) =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _soundConfirmTimers.Remove(sessionId);
                var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session?.Status != TerminalStatus.TaskCompleted)
                {
                    Logger.Log("StatusParser: sound cancelled, status changed before confirmation");
                    return;
                }

                // Only play sound for background sessions — if the user is actively
                // viewing this session, the visual indicator is enough and the sound
                // would be a false positive from typing/interaction disrupting the poll.
                var isActiveSession = SessionStore.Instance.ActiveSessionId == sessionId;
                if (isActiveSession)
                {
                    Logger.Log("StatusParser: sound skipped, session is active");
                    return;
                }

                Logger.Log("StatusParser: sound confirmed, playing TaskCompleted sound");
                SoundPlayer.PlayTaskCompleted();
            });
        };
        timer.Start();
        _soundConfirmTimers[sessionId] = timer;
    }

    private static void CancelSoundConfirmation(Guid sessionId)
    {
        if (_soundConfirmTimers.TryGetValue(sessionId, out var existing))
        {
            existing.Stop();
            existing.Dispose();
            _soundConfirmTimers.Remove(sessionId);
        }
    }
}
