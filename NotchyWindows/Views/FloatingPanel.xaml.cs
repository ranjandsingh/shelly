using System.IO;
using System.Windows;
using System.Windows.Input;
using NotchyWindows.Services;

namespace NotchyWindows.Views;

public partial class FloatingPanel : Window
{
    public FloatingPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        SessionStore.Instance.ActiveSessionChanged += OnActiveSessionChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Logger.Log("FloatingPanel: OnLoaded");
        PositionTopCenter();

        // Attach the initial session if one exists
        var activeId = SessionStore.Instance.ActiveSessionId;
        Logger.Log($"FloatingPanel: activeSessionId={activeId}");
        if (activeId.HasValue)
            TerminalHost.AttachSession(activeId.Value);
    }

    private void OnActiveSessionChanged(Guid sessionId)
    {
        Logger.Log($"FloatingPanel: ActiveSessionChanged -> {sessionId}");
        TerminalHost.AttachSession(sessionId);
    }

    private bool _isShowing;

    public void ShowPanel()
    {
        _isShowing = true;
        PositionTopCenter();
        Show();
        Activate();
        IdeDetector.Instance.StartPolling();

        // Route keyboard focus into WebView2/xterm (panel is no longer non-activating)
        Dispatcher.BeginInvoke(() => TerminalHost.FocusTerminal(), System.Windows.Threading.DispatcherPriority.Input);

        // Brief guard so OnDeactivated doesn't fire immediately
        Task.Delay(300).ContinueWith(_ =>
            Dispatcher.InvokeAsync(() => _isShowing = false));
    }

    public void HidePanel()
    {
        Hide();

        if (!SessionStore.Instance.IsPinned)
            IdeDetector.Instance.StopPolling();
    }

    private void PositionTopCenter()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = 0;
    }

    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.S:
                    _ = CreateCheckpointAsync();
                    e.Handled = true;
                    break;
                case Key.T:
                    SessionStore.Instance.AddSession();
                    e.Handled = true;
                    break;
                case Key.W:
                    var activeId = SessionStore.Instance.ActiveSessionId;
                    if (activeId.HasValue && SessionStore.Instance.Sessions.Count > 1)
                        SessionStore.Instance.RemoveSession(activeId.Value);
                    e.Handled = true;
                    break;
            }
        }
    }

    private async Task CreateCheckpointAsync()
    {
        var session = SessionStore.Instance.ActiveSession;
        if (session?.ProjectPath == null) return;

        var success = await CheckpointManager.CreateCheckpoint(session.ProjectPath, session.ProjectName);
        if (success)
        {
            // Brief visual feedback on the title
            Title = "Notchy — Checkpoint Saved";
            await Task.Delay(2000);
            Title = "Notchy";
        }
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths == null) return;

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var name = Path.GetFileName(path);
                SessionStore.Instance.AddSession(name, path, path);
            }
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (SessionStore.Instance.IsPinned || _isShowing)
            return;

        // WebView2 hosts its own HWND. When focus moves into the embedded browser, the WPF window
        // often deactivates even though the user is still interacting with the panel — do not auto-hide.
        Dispatcher.BeginInvoke(() =>
        {
            if (SessionStore.Instance.IsPinned)
                return;

            try
            {
                var pos = Mouse.GetPosition(this);
                if (pos.X >= 0 && pos.Y >= 0 && pos.X <= ActualWidth && pos.Y <= ActualHeight)
                {
                    Logger.Log("FloatingPanel: OnDeactivated skipped — pointer still over panel (WebView2 focus)");
                    return;
                }
            }
            catch
            {
                // ignore
            }

            HidePanel();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
