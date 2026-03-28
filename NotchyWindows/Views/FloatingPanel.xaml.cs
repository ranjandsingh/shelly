using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotchyWindows.Services;

namespace NotchyWindows.Views;

public partial class FloatingPanel : Window
{
    private bool _isExpanded;
    private bool _isPinned;       // clicked while expanded → stays open until click outside
    private bool _isTransitioning;
    private DispatcherTimer? _collapseTimer;

    private const double ExpandedWidth = 720;
    private const double ExpandedHeight = 400;

    public FloatingPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        SessionStore.Instance.ActiveSessionChanged += OnActiveSessionChanged;
        SessionStore.Instance.Sessions.CollectionChanged += (_, _) => UpdateCollapsedBar();

        // Timer for delayed collapse on mouse leave
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (_isExpanded && !_isPinned)
                CollapsePanel();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Logger.Log("FloatingPanel: OnLoaded");
        PositionCenter();
        UpdateCollapsedBar();

        var activeId = SessionStore.Instance.ActiveSessionId;
        Logger.Log($"FloatingPanel: activeSessionId={activeId}");
        if (activeId.HasValue)
            TerminalHost.AttachSession(activeId.Value);
    }

    private void OnActiveSessionChanged(Guid sessionId)
    {
        Logger.Log($"FloatingPanel: ActiveSessionChanged -> {sessionId}");
        TerminalHost.AttachSession(sessionId);
        UpdateCollapsedBar();
    }

    public bool IsExpanded => _isExpanded;

    public void ExpandPanel()
    {
        if (_isExpanded) return;
        _isExpanded = true;
        _isTransitioning = true;
        _collapseTimer?.Stop();

        SizeToContent = SizeToContent.Manual;
        Width = ExpandedWidth;
        Height = ExpandedHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        CollapsedBar.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Visible;

        PositionCenter();
        Show();
        Activate();
        IdeDetector.Instance.StartPolling();

        Dispatcher.BeginInvoke(() => TerminalHost.FocusTerminal(),
            DispatcherPriority.Input);

        Task.Delay(400).ContinueWith(_ =>
            Dispatcher.InvokeAsync(() => _isTransitioning = false));
    }

    public void CollapsePanel()
    {
        if (!_isExpanded) return;
        _isExpanded = false;
        _isPinned = false;

        CollapsedBar.Visibility = Visibility.Visible;
        ExpandedPanel.Visibility = Visibility.Collapsed;

        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        PositionCenter();

        if (!SessionStore.Instance.IsPinned)
            IdeDetector.Instance.StopPolling();
    }

    public void TogglePanel()
    {
        if (_isExpanded)
        {
            _isPinned = false;
            CollapsePanel();
        }
        else
        {
            _isPinned = true; // hotkey/tray toggle always pins
            ExpandPanel();
        }
    }

    private void PositionCenter()
    {
        var screen = SystemParameters.WorkArea;
        if (SizeToContent == SizeToContent.Manual)
            Left = (screen.Width - Width) / 2;
        else
            Dispatcher.BeginInvoke(() =>
            {
                Left = (screen.Width - ActualWidth) / 2;
            }, DispatcherPriority.Loaded);
        Top = 0;
    }

    private void UpdateCollapsedBar()
    {
        var session = SessionStore.Instance.ActiveSession;
        if (session == null) return;

        Dispatcher.InvokeAsync(() =>
        {
            var count = SessionStore.Instance.Sessions.Count;
            if (count > 1)
            {
                CollapsedBadge.Visibility = Visibility.Visible;
                CollapsedBadgeText.Text = count.ToString();
            }
            else
            {
                CollapsedBadge.Visibility = Visibility.Collapsed;
            }

            CollapsedStatusDot.Fill = session.Status switch
            {
                Models.TerminalStatus.Working => new SolidColorBrush(Color.FromRgb(0xFF, 0xA7, 0x26)),
                Models.TerminalStatus.WaitingForInput => new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
                Models.TerminalStatus.TaskCompleted => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                Models.TerminalStatus.Interrupted => new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
                _ => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
            };
        });
    }

    // Hover on collapsed bar → expand (unpinned, will collapse on mouse leave)
    private void CollapsedBar_MouseEnter(object sender, MouseEventArgs e)
    {
        _isPinned = false;
        ExpandPanel();
    }

    // Click on collapsed bar → expand and pin
    private void CollapsedBar_Click(object sender, MouseButtonEventArgs e)
    {
        _isPinned = true;
        ExpandPanel();
    }

    // Any click inside expanded panel → pin it open
    private void ExpandedPanel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isPinned = true;
        _collapseTimer?.Stop();
    }

    // Mouse leaves expanded panel → collapse after delay (unless pinned)
    private void ExpandedPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPinned || _isTransitioning) return;
        _collapseTimer?.Start();
    }

    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _collapseTimer?.Stop(); // cancel pending collapse if mouse re-enters
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

        if (!_isExpanded || !_isPinned || SessionStore.Instance.IsPinned || _isTransitioning)
            return;

        // When pinned and click outside → collapse
        Dispatcher.BeginInvoke(() =>
        {
            if (SessionStore.Instance.IsPinned || !_isExpanded)
                return;

            try
            {
                var pos = Mouse.GetPosition(this);
                if (pos.X >= 0 && pos.Y >= 0 && pos.X <= ActualWidth && pos.Y <= ActualHeight)
                {
                    // Mouse still over panel (WebView2 took focus) — pin it
                    _isPinned = true;
                    return;
                }
            }
            catch { }

            CollapsePanel();
        }, DispatcherPriority.ApplicationIdle);
    }
}
