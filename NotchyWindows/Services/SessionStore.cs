using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NotchyWindows.Models;

namespace NotchyWindows.Services;

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

    private SessionStore()
    {
        // Create a default session
        AddSession();
    }

    public TerminalSession AddSession(string? projectName = null, string? projectPath = null, string? workingDirectory = null)
    {
        var session = new TerminalSession
        {
            ProjectName = projectName ?? "Terminal",
            ProjectPath = projectPath,
            WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        Sessions.Add(session);

        if (ActiveSessionId == null)
            SelectSession(session.Id);

        return session;
    }

    public void SelectSession(Guid sessionId)
    {
        ActiveSessionId = sessionId;
        ActiveSessionChanged?.Invoke(sessionId);
    }

    public void RemoveSession(Guid sessionId)
    {
        var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null) return;

        TerminalManager.Instance.DestroyTerminal(sessionId);
        Sessions.Remove(session);

        if (ActiveSessionId == sessionId)
        {
            ActiveSessionId = Sessions.FirstOrDefault()?.Id;
            if (ActiveSessionId.HasValue)
                ActiveSessionChanged?.Invoke(ActiveSessionId.Value);
        }
    }

    public event Action<Guid>? ActiveSessionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
