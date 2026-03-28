using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NotchyWindows.Models;

public class TerminalSession : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();

    private string _projectName = "Terminal";
    public string ProjectName
    {
        get => _projectName;
        set { _projectName = value; OnPropertyChanged(); }
    }

    private string? _projectPath;
    public string? ProjectPath
    {
        get => _projectPath;
        set { _projectPath = value; OnPropertyChanged(); }
    }

    private string _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set { _workingDirectory = value; OnPropertyChanged(); }
    }

    private bool _hasStarted;
    public bool HasStarted
    {
        get => _hasStarted;
        set { _hasStarted = value; OnPropertyChanged(); }
    }

    private TerminalStatus _terminalStatus = TerminalStatus.Idle;
    public TerminalStatus Status
    {
        get => _terminalStatus;
        set { _terminalStatus = value; OnPropertyChanged(); }
    }

    private bool _isProjectOpen = true;
    public bool IsProjectOpen
    {
        get => _isProjectOpen;
        set { _isProjectOpen = value; OnPropertyChanged(); }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
