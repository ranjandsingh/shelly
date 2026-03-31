using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shelly.Animations;
using Shelly.Interop;
using Shelly.Services;

namespace Shelly.Views;

public partial class FloatingPanel : Window
{
    private bool _isExpanded;
    private bool _isShowing;
    private bool _hasInteracted;
    private DispatcherTimer? _hoverCollapseTimer;
    private DateTime? _outsideSince;
    private DispatcherTimer? _showingGuardTimer;

    private const double DefaultExpandedWidth = 720;
    private const double DefaultExpandedHeight = 400;
    private double _expandedWidth = DefaultExpandedWidth;
    private double _expandedHeight = DefaultExpandedHeight;
    private DispatcherTimer? _resizeCaptureTimer;

    private NotchController _notch = null!;
    private Models.TerminalStatus _lastAutoExpandStatus;

    public bool IsExpanded => _isExpanded;

    public FloatingPanel()
    {
        InitializeComponent();
        _notch = new NotchController(this);

        Width = NotchController.CollapsedWidth;
        Height = NotchController.CollapsedHeight;
        Loaded += OnLoaded;

        // Capture expanded size after user finishes resizing (debounced)
        _resizeCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _resizeCaptureTimer.Tick += (_, _) =>
        {
            _resizeCaptureTimer!.Stop();
            if (_isExpanded && Width >= DefaultExpandedWidth * 0.5)
            {
                _expandedWidth = Width;
                _expandedHeight = Height;
            }
        };
        SizeChanged += (_, _) =>
        {
            if (!_isExpanded || ResizeMode != ResizeMode.CanResizeWithGrip) return;
            if (Width < DefaultExpandedWidth * 0.5) return;
            _resizeCaptureTimer.Stop();
            _resizeCaptureTimer.Start();
        };

        // Hover auto-collapse timer
        _hoverCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _hoverCollapseTimer.Tick += (_, _) =>
        {
            if (!_isExpanded || _hasInteracted) { _hoverCollapseTimer.Stop(); return; }

            bool inside = false;
            if (NativeMethods.GetCursorPos(out var pt))
            {
                try
                {
                    var topLeft = PointToScreen(new Point(0, 0));
                    var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
                    inside = pt.X >= topLeft.X && pt.X <= bottomRight.X &&
                             pt.Y >= topLeft.Y && pt.Y <= bottomRight.Y;
                }
                catch { }
            }

            if (inside)
                _outsideSince = null;
            else
            {
                _outsideSince ??= DateTime.UtcNow;
                if ((DateTime.UtcNow - _outsideSince.Value).TotalMilliseconds >= 500)
                {
                    _hoverCollapseTimer.Stop();
                    _outsideSince = null;
                    CollapsePanel();
                }
            }
        };

        SessionStore.Instance.ActiveSessionChanged += OnActiveSessionChanged;
        SessionStore.Instance.Sessions.CollectionChanged += (_, _) => _notch.Update();
        SessionStore.Instance.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SessionStore.NotchAtBottom))
                Dispatcher.InvokeAsync(PositionCenter);
        };

        foreach (var s in SessionStore.Instance.Sessions)
            s.PropertyChanged += OnSessionPropertyChanged;
        SessionStore.Instance.Sessions.CollectionChanged += (_, args) =>
        {
            if (args.NewItems != null)
                foreach (Models.TerminalSession s in args.NewItems)
                    s.PropertyChanged += OnSessionPropertyChanged;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Logger.Log("FloatingPanel: OnLoaded");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | (IntPtr)NativeMethods.WS_EX_TOOLWINDOW);

        PositionCenter();
        _notch.UpdateSync();

        var activeId = SessionStore.Instance.ActiveSessionId;
        Logger.Log($"FloatingPanel: activeSessionId={activeId}");
        if (activeId.HasValue)
            TerminalHost.AttachSession(activeId.Value);

        _notch.PlayLaunchGreeting();
    }

    private void OnActiveSessionChanged(Guid sessionId)
    {
        Logger.Log($"FloatingPanel: ActiveSessionChanged -> {sessionId}");
        TerminalHost.AttachSession(sessionId);
        _notch.Update();
    }

    // --- Expand / Collapse ---

    public void ExpandPanel(bool pinOpen = false)
    {
        if (_isExpanded) return;
        _isExpanded = true;
        _isShowing = true;
        _hasInteracted = pinOpen;
        _outsideSince = null;
        if (!pinOpen)
            _hoverCollapseTimer?.Start();

        TerminalHost.Visibility = Visibility.Hidden;
        CollapsedBar.Visibility = Visibility.Collapsed;
        ExpandedPanel.Opacity = 0;
        ExpandedPanel.Visibility = Visibility.Visible;

        Width = _expandedWidth;
        Height = _expandedHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        bool bottom = SessionStore.Instance.NotchAtBottom;
        ExpandedPanel.RenderTransformOrigin = new Point(0.5, 0.5);
        double translateFrom = bottom ? 20 : -20;

        var transformDuration = TimeSpan.FromMilliseconds(350);
        var opacityDuration = TimeSpan.FromMilliseconds(300);
        var springEase = new SpringEase { Overshoot = 0.07, EasingMode = EasingMode.EaseOut };
        var opacityEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        ExpandedPanelScale.ScaleX = 0.93;
        ExpandedPanelScale.ScaleY = 0.93;
        ExpandedPanelTranslate.Y = translateFrom;

        var scaleY = new DoubleAnimation(0.93, 1.0, transformDuration) { EasingFunction = springEase, FillBehavior = FillBehavior.Stop };
        scaleY.Completed += (_, _) =>
        {
            if (!_isExpanded) return;
            ExpandedPanelScale.ScaleY = 1.0;
            TerminalHost.Visibility = Visibility.Visible;
            TerminalHost.FocusTerminal();
        };
        var scaleX = new DoubleAnimation(0.93, 1.0, transformDuration) { EasingFunction = springEase, FillBehavior = FillBehavior.Stop };
        scaleX.Completed += (_, _) => ExpandedPanelScale.ScaleX = 1.0;
        var slideDown = new DoubleAnimation(translateFrom, 0, transformDuration) { EasingFunction = springEase, FillBehavior = FillBehavior.Stop };
        slideDown.Completed += (_, _) => ExpandedPanelTranslate.Y = 0;
        var fadeIn = new DoubleAnimation(0, 1, opacityDuration) { EasingFunction = opacityEase, BeginTime = TimeSpan.FromMilliseconds(30), FillBehavior = FillBehavior.Stop };
        fadeIn.Completed += (_, _) => ExpandedPanel.Opacity = 1;

        ExpandedPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        ExpandedPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        ExpandedPanelTranslate.BeginAnimation(TranslateTransform.YProperty, slideDown);
        ExpandedPanel.BeginAnimation(OpacityProperty, fadeIn);

        PositionCenter();
        Show();
        Activate();

        var activeId = SessionStore.Instance.ActiveSessionId;
        if (activeId.HasValue)
        {
            var active = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == activeId.Value);
            if (active?.Status == Models.TerminalStatus.TaskCompleted)
                StatusParser.AcknowledgeCompletion(activeId.Value);
        }

        if (_showingGuardTimer == null)
        {
            _showingGuardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _showingGuardTimer.Tick += (_, _) => { _showingGuardTimer.Stop(); _isShowing = false; };
        }
        _showingGuardTimer.Stop();
        _showingGuardTimer.Start();
    }

    public void CollapsePanel()
    {
        if (!_isExpanded) return;
        _isExpanded = false;

        ResizeMode = ResizeMode.NoResize;
        TerminalHost.Visibility = Visibility.Hidden;

        bool bottom = SessionStore.Instance.NotchAtBottom;
        ExpandedPanel.RenderTransformOrigin = new Point(0.5, 0.5);
        double translateTo = bottom ? 15 : -15;

        var transformDuration = TimeSpan.FromMilliseconds(380);
        var opacityDuration = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var scaleY = new DoubleAnimation(1.0, 0.9, transformDuration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        scaleY.Completed += (_, _) => ExpandedPanelScale.ScaleY = 1.0;
        var scaleX = new DoubleAnimation(1.0, 0.98, transformDuration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        scaleX.Completed += (_, _) => ExpandedPanelScale.ScaleX = 1.0;
        var slideUp = new DoubleAnimation(0, translateTo, transformDuration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        slideUp.Completed += (_, _) => ExpandedPanelTranslate.Y = 0;
        var fadeOut = new DoubleAnimation(1, 0, opacityDuration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        fadeOut.Completed += (_, _) =>
        {
            if (_isExpanded) return;
            ExpandedPanel.Opacity = 1;
            ExpandedPanelTranslate.Y = 0;
            ExpandedPanelScale.ScaleX = 1.0;
            ExpandedPanelScale.ScaleY = 1.0;
            ExpandedPanel.Visibility = Visibility.Collapsed;
            CollapsedBar.Visibility = Visibility.Visible;
            _notch.UpdateSync();

            if (!SessionStore.Instance.IsPinned)
                IdeDetector.Instance.StopPolling();
        };

        ExpandedPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        ExpandedPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        ExpandedPanelTranslate.BeginAnimation(TranslateTransform.YProperty, slideUp);
        ExpandedPanel.BeginAnimation(OpacityProperty, fadeOut);
    }

    public void TogglePanel()
    {
        if (_isExpanded) CollapsePanel();
        else ExpandPanel(pinOpen: true);
    }

    public void PositionCenter()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = SessionStore.Instance.NotchAtBottom ? screen.Height - Height : 0;
    }

    // --- Status change handling ---

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Models.TerminalSession.Status)) return;
        _notch.Update();

        if (sender is Models.TerminalSession session &&
            session.Id == SessionStore.Instance.ActiveSessionId &&
            session.Status == Models.TerminalStatus.WaitingForInput &&
            _lastAutoExpandStatus != Models.TerminalStatus.WaitingForInput &&
            !_isExpanded)
        {
            _lastAutoExpandStatus = session.Status;
            Dispatcher.InvokeAsync(() => ExpandPanel(pinOpen: true));
        }
        else if (sender is Models.TerminalSession s2)
        {
            _lastAutoExpandStatus = s2.Status;
        }
    }

    // --- UI event handlers ---

    private void CollapsedBar_MouseEnter(object sender, MouseEventArgs e) => ExpandPanel();
    private void CollapsedBar_Click(object sender, MouseButtonEventArgs e)
    {
        _hasInteracted = true;
        ExpandPanel();
    }

    private void ExpandedPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _hasInteracted = true;
        _hoverCollapseTimer?.Stop();
    }

    private void ExpandedPanel_MouseEnter(object sender, MouseEventArgs e) { }
    private void ExpandedPanel_MouseLeave(object sender, MouseEventArgs e) { }

    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var sessions = SessionStore.Instance.Sessions;
            if (sessions.Count < 2) { e.Handled = true; return; }

            var currentIndex = -1;
            for (int i = 0; i < sessions.Count; i++)
                if (sessions[i].IsActive) { currentIndex = i; break; }

            int next = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? (currentIndex - 1 + sessions.Count) % sessions.Count
                : (currentIndex + 1) % sessions.Count;

            SessionStore.Instance.SelectSession(sessions[next].Id);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.T:
                    var newSession = SessionStore.Instance.AddSession();
                    SessionStore.Instance.SelectSession(newSession.Id);
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

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        e.Handled = true;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths == null) return;

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var existing = SessionStore.Instance.Sessions
                    .FirstOrDefault(s => string.Equals(s.ProjectPath, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    SessionStore.Instance.SelectSession(existing.Id);
                else
                {
                    var session = SessionStore.Instance.AddSession(Path.GetFileName(path), path, path);
                    SessionStore.Instance.SelectSession(session.Id);
                }
                if (!_isExpanded) ExpandPanel();
            }
            else if (File.Exists(path))
            {
                var activeId = SessionStore.Instance.ActiveSessionId;
                if (activeId.HasValue && TerminalManager.Instance.HasTerminal(activeId.Value))
                {
                    var quotedPath = path.Contains(' ') ? $"\"{path}\"" : path;
                    TerminalManager.Instance.WriteInput(activeId.Value, quotedPath);
                }
            }
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (!_isExpanded || SessionStore.Instance.IsPinned || _isShowing)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (SessionStore.Instance.IsPinned) return;

            try
            {
                var pos = Mouse.GetPosition(this);
                if (pos.X >= 0 && pos.Y >= 0 && pos.X <= ActualWidth && pos.Y <= ActualHeight)
                {
                    Logger.Log("FloatingPanel: OnDeactivated skipped — pointer still over panel (WebView2 focus)");
                    return;
                }
            }
            catch { }

            CollapsePanel();
        }, DispatcherPriority.ApplicationIdle);
    }
}
