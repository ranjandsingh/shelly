using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Shelly.Models;

namespace Shelly.Services;

public class SessionStore : INotifyPropertyChanged
{
    public static SessionStore Instance { get; } = new();

    public ObservableCollection<TerminalSession> Sessions { get; } = new();

    private Guid? _activeSessionId;
    public Guid? ActiveSessionId
    {
        get => _activeSessionId;
        private set { _activeSessionId = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveSession)); }
    }

    public TerminalSession? ActiveSession => Sessions.FirstOrDefault(s => s.Id == ActiveSessionId);

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set { _isPinned = value; OnPropertyChanged(); }
    }

    private bool _notchAtBottom;
    /// <summary>When true, the collapsed pill and expanded panel anchor to the bottom of the screen.</summary>
    public bool NotchAtBottom
    {
        get => _notchAtBottom;
        set { _notchAtBottom = value; OnPropertyChanged(); }
    }

    private SessionStore()
    {
        // Create a default session
        AddSession();

        // Wire IDE detection
        IdeDetector.Instance.ProjectsDetected += OnProjectsDetected;
    }

    public TerminalSession AddSession(string? projectName = null, string? projectPath = null, string? workingDirectory = null)
    {
        var session = new TerminalSession
        {
            ProjectName = projectName ?? "Terminal",
            ProjectPath = projectPath,
            WorkingDirectory = workingDirectory ?? projectPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        Sessions.Add(session);

        // Listen for status changes for sleep prevention
        session.PropertyChanged += OnSessionPropertyChanged;

        if (ActiveSessionId == null)
            SelectSession(session.Id);

        return session;
    }

    public void SelectSession(Guid sessionId)
    {
        // Update IsActive on all sessions
        foreach (var s in Sessions)
            s.IsActive = s.Id == sessionId;

        ActiveSessionId = sessionId;
        ActiveSessionChanged?.Invoke(sessionId);
    }

    public void RemoveSession(Guid sessionId)
    {
        var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null) return;

        session.PropertyChanged -= OnSessionPropertyChanged;
        TerminalManager.Instance.DestroyTerminal(sessionId);
        Sessions.Remove(session);

        if (ActiveSessionId == sessionId)
        {
            ActiveSessionId = Sessions.FirstOrDefault()?.Id;
            if (ActiveSessionId.HasValue)
                ActiveSessionChanged?.Invoke(ActiveSessionId.Value);
        }

        UpdateSleepPrevention();
    }

    private void OnProjectsDetected(List<DetectedProject> projects)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var detectedPaths = new HashSet<string>(
                projects.Where(p => p.Path != null).Select(p => p.Path!),
                StringComparer.OrdinalIgnoreCase);

            // Update IsProjectOpen for existing sessions
            foreach (var session in Sessions)
            {
                if (session.ProjectPath != null)
                    session.IsProjectOpen = detectedPaths.Contains(session.ProjectPath);
            }

            // Auto-create sessions for newly detected projects
            var existingPaths = new HashSet<string>(
                Sessions.Where(s => s.ProjectPath != null).Select(s => s.ProjectPath!),
                StringComparer.OrdinalIgnoreCase);

            foreach (var project in projects)
            {
                if (project.Path != null && !existingPaths.Contains(project.Path))
                {
                    AddSession(project.Name, project.Path, project.Path);
                }
            }
        });
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalSession.Status))
            UpdateSleepPrevention();
    }

    private void UpdateSleepPrevention()
    {
        var anyWorking = Sessions.Any(s => s.Status == TerminalStatus.Working);
        if (anyWorking)
            SleepPrevention.PreventSleep();
        else
            SleepPrevention.AllowSleep();
    }

    public event Action<Guid>? ActiveSessionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
