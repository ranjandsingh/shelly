using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shelly.Animations;
using Shelly.Interop;
using Shelly.Services;

namespace Shelly.Views;

public partial class FloatingPanel : Window
{
    private bool _isExpanded;
    private bool _isShowing;
    private bool _hasInteracted; // true once user clicks on the expanded panel
    private DispatcherTimer? _hoverCollapseTimer;
    private DateTime? _outsideSince; // tracks when cursor first left the window
    private bool _iconVisible; // tracks whether the mascot icon is currently shown
    private bool _greetingActive; // prevents UpdateCollapsedBar from hiding during greeting
    private const double CollapsedWidth = 48;
    private const double CollapsedWidthWithIcon = 84;
    private const double CollapsedWidthGreeting = 124;
    private const double CollapsedHeight = 18;
    private const double CollapsedHeightWithIcon = 36;
    private const double CollapsedHeightGreeting = 48;

    // Mascot icon images for each status
    private static readonly BitmapImage IconIdle = LoadIcon("Resources/icon.png");
    private static readonly BitmapImage IconProcessing = LoadIcon("Resources/icon-processing.png");
    private static readonly BitmapImage IconWaiting = LoadIcon("Resources/icon-waiting.png");
    private static readonly BitmapImage IconSuccess = LoadIcon("Resources/icon-success.png");

    private static BitmapImage LoadIcon(string path)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.UriSource = new Uri($"pack://application:,,,/{path}");
        img.DecodePixelWidth = 40; // 2x for crisp rendering at 20px
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }
    private const double ExpandedWidth = 720;
    private const double ExpandedHeight = 400;

    public FloatingPanel()
    {
        InitializeComponent();
        Width = CollapsedWidth;
        Height = CollapsedHeight;
        Loaded += OnLoaded;

        // Timer for hover-only collapse. Polls cursor position via Win32 GetCursorPos
        // (reliable regardless of WebView2 HWND focus). Collapses only after cursor
        // has been outside the window for 500ms continuously.
        _hoverCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _hoverCollapseTimer.Tick += (_, _) =>
        {
            if (!_isExpanded || _hasInteracted) { _hoverCollapseTimer.Stop(); return; }

            bool inside = false;
            if (NativeMethods.GetCursorPos(out var pt))
            {
                // Compare in screen pixels — use PointToScreen for DPI-awareness
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
            {
                _outsideSince = null; // reset
            }
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
        SessionStore.Instance.Sessions.CollectionChanged += (_, _) => UpdateCollapsedBar();
        SessionStore.Instance.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SessionStore.NotchAtBottom))
                Dispatcher.InvokeAsync(PositionCenter);
        };

        // Listen for status changes on all sessions
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

        // Hide from Alt+Tab by applying WS_EX_TOOLWINDOW
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | (IntPtr)NativeMethods.WS_EX_TOOLWINDOW);

        PositionCenter();
        UpdateCollapsedBar();

        var activeId = SessionStore.Instance.ActiveSessionId;
        Logger.Log($"FloatingPanel: activeSessionId={activeId}");
        if (activeId.HasValue)
            TerminalHost.AttachSession(activeId.Value);

        // Launch greeting: briefly show the mascot icon, then fade it away
        PlayLaunchGreeting();
    }

    private static readonly string[] Greetings =
    [
        "Hello!",
        "Hey!",
        "Namaste!",
        "Hola!",
        "Bonjour!",
        "Ciao!",
        "Hallo!",
        "Olá!",
        "Ahoj!",
        "Salut!",
        "Hej!",
        "Aloha!",
        "Salam!",
        "Sawubona!",
        "Merhaba!",
    ];

    private void PlayLaunchGreeting()
    {
        _greetingActive = true;

        CollapsedGreeting.Text = Greetings[Random.Shared.Next(Greetings.Length)];

        // Show mascot icon with greeting text
        CollapsedIcon.Width = 28;
        CollapsedIcon.Height = 28;
        CollapsedBar.CornerRadius = new CornerRadius(16);
        CollapsedIcon.Source = IconIdle;
        CollapsedIcon.Opacity = 0;
        CollapsedIcon.Visibility = Visibility.Visible;
        CollapsedGreeting.Opacity = 0;
        CollapsedGreeting.Visibility = Visibility.Visible;
        _iconVisible = true;

        Width = CollapsedWidthGreeting;
        Height = CollapsedHeightGreeting;
        PositionCenter();

        // Fade in icon and text
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        fadeIn.Completed += (_, _) => { CollapsedIcon.Opacity = 1; CollapsedGreeting.Opacity = 1; };
        CollapsedIcon.BeginAnimation(OpacityProperty, fadeIn);
        CollapsedGreeting.BeginAnimation(OpacityProperty, fadeIn);

        // Hold for 3 seconds, then fade out and collapse
        Task.Delay(3000).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
        {
            _greetingActive = false;

            // Fade out everything together, then collapse to pill
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop
            };
            fadeOut.Completed += (_, _) =>
            {
                CollapsedIcon.Opacity = 1;
                CollapsedIcon.Visibility = Visibility.Collapsed;
                CollapsedIcon.Width = 24;
                CollapsedIcon.Height = 24;
                CollapsedGreeting.Opacity = 1;
                CollapsedGreeting.Visibility = Visibility.Collapsed;
                CollapsedBar.CornerRadius = new CornerRadius(8);
                _iconVisible = false;

                // Check if a status is active — if so, let UpdateCollapsedBar handle the icon
                var anyActive = SessionStore.Instance.Sessions.Any(s => s.Status != Models.TerminalStatus.Idle);
                if (anyActive)
                {
                    UpdateCollapsedBar();
                }
                else
                {
                    Width = CollapsedWidth;
                    Height = CollapsedHeight;
                    PositionCenter();
                }
            };
            CollapsedIcon.BeginAnimation(OpacityProperty, fadeOut);
            CollapsedGreeting.BeginAnimation(OpacityProperty, fadeOut);
        }));
    }

    private void OnActiveSessionChanged(Guid sessionId)
    {
        Logger.Log($"FloatingPanel: ActiveSessionChanged -> {sessionId}");
        TerminalHost.AttachSession(sessionId);
        UpdateCollapsedBar();
    }

    public bool IsExpanded => _isExpanded;

    /// <summary>Expand from notch to full panel.</summary>
    /// <param name="pinOpen">If true, panel stays open until user clicks outside (used for hotkey/explicit activation).</param>
    public void ExpandPanel(bool pinOpen = false)
    {
        if (_isExpanded) return;
        _isExpanded = true;
        _isShowing = true;
        _hasInteracted = pinOpen;
        _outsideSince = null;
        if (!pinOpen)
            _hoverCollapseTimer?.Start(); // only auto-collapse on hover-triggered expand

        // Hide collapsed bar and set expanded panel to invisible BEFORE resizing
        // to prevent a flash frame where the pill is visible at 720x400
        CollapsedBar.Visibility = Visibility.Collapsed;
        ExpandedPanel.Opacity = 0;
        ExpandedPanel.Visibility = Visibility.Visible;

        Width = ExpandedWidth;
        Height = ExpandedHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        // Fluid expand animation: scale from center + translate + staggered opacity
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
        scaleY.Completed += (_, _) => ExpandedPanelScale.ScaleY = 1.0;
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
        // IDE detection disabled — title-based detection doesn't reliably resolve full paths
        // IdeDetector.Instance.StartPolling();

        // Clear TaskCompleted for all sessions when the user opens the panel
        foreach (var s in SessionStore.Instance.Sessions)
        {
            if (s.Status == Models.TerminalStatus.TaskCompleted)
                Services.StatusParser.AcknowledgeCompletion(s.Id);
        }

        // Focus terminal
        Dispatcher.BeginInvoke(() => TerminalHost.FocusTerminal(), DispatcherPriority.Input);

        // Force WebView2 HWND reposition
        Task.Delay(50).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
        {
            TerminalHost.Visibility = Visibility.Hidden;
            Task.Delay(30).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                TerminalHost.Visibility = Visibility.Visible;
                TerminalHost.FocusTerminal();
            }));
        }));

        // Brief guard so OnDeactivated doesn't fire immediately (covers 350ms expand animation)
        Task.Delay(400).ContinueWith(_ =>
            Dispatcher.InvokeAsync(() => _isShowing = false));
    }

    /// <summary>Collapse back to notch. Mirrors the original HidePanel logic.</summary>
    public void CollapsePanel()
    {
        if (!_isExpanded) return;
        _isExpanded = false;

        // Hide resize grip and WebView2 HWND immediately so the entire panel
        // animates as one clean unit (no flashing grip icon or split layers)
        ResizeMode = ResizeMode.NoResize;
        TerminalHost.Visibility = Visibility.Hidden;

        // Fluid collapse animation: scale + translate + opacity
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
            ExpandedPanel.Opacity = 1;
            ExpandedPanelTranslate.Y = 0;
            ExpandedPanelScale.ScaleX = 1.0;
            ExpandedPanelScale.ScaleY = 1.0;
            ExpandedPanel.Visibility = Visibility.Collapsed;
            CollapsedBar.Visibility = Visibility.Visible;
            TerminalHost.Visibility = Visibility.Visible; // restore for next expand
            // Let UpdateCollapsedBar handle all width/height/position
            UpdateCollapsedBarSync();

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
        if (_isExpanded)
            CollapsePanel();
        else
            ExpandPanel(pinOpen: true);
    }

    private void PositionCenter()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = SessionStore.Instance.NotchAtBottom ? screen.Height - Height : 0;
    }

    // --- Claude status features ---

    private Models.TerminalStatus _lastAutoExpandStatus;

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Models.TerminalSession.Status)) return;
        UpdateCollapsedBar();

        // Auto-expand when Claude needs attention (once per transition)
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

    /// <summary>Queue a collapsed bar update on the dispatcher (used by event handlers).</summary>
    private void UpdateCollapsedBar()
    {
        Dispatcher.InvokeAsync(UpdateCollapsedBarSync);
    }

    /// <summary>Synchronously update collapsed bar indicators, mascot icon, and size.
    /// Must be called on the UI thread.</summary>
    private void UpdateCollapsedBarSync()
    {
        var sessions = SessionStore.Instance.Sessions;

        // Count sessions per status type
        int workingCount = 0, waitingCount = 0, completedCount = 0, interruptedCount = 0;
        foreach (var s in sessions)
        {
            switch (s.Status)
            {
                case Models.TerminalStatus.Working: workingCount++; break;
                case Models.TerminalStatus.WaitingForInput: waitingCount++; break;
                case Models.TerminalStatus.TaskCompleted: completedCount++; break;
                case Models.TerminalStatus.Interrupted: interruptedCount++; break;
            }
        }

        // Determine highest-priority status for mascot icon
        var overallStatus = waitingCount > 0 ? Models.TerminalStatus.WaitingForInput
            : workingCount > 0 ? Models.TerminalStatus.Working
            : completedCount > 0 ? Models.TerminalStatus.TaskCompleted
            : interruptedCount > 0 ? Models.TerminalStatus.Interrupted
            : Models.TerminalStatus.Idle;

        // Hide all groups and stop animations first
        WorkingGroup.Visibility = Visibility.Collapsed;
        WaitingGroup.Visibility = Visibility.Collapsed;
        CompletedGroup.Visibility = Visibility.Collapsed;
        InterruptedGroup.Visibility = Visibility.Collapsed;
        StopAnimations();

        // Show each status group with count badge (hidden when count is 1)
        if (workingCount > 0)
        {
            WorkingGroup.Visibility = Visibility.Visible;
            WorkingCount.Text = workingCount.ToString();
            WorkingCount.Visibility = workingCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            StartSpinnerAnimation();
        }
        if (waitingCount > 0)
        {
            WaitingGroup.Visibility = Visibility.Visible;
            WaitingCount.Text = waitingCount.ToString();
            WaitingCount.Visibility = waitingCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            StartPulseAnimation();
        }
        if (completedCount > 0)
        {
            CompletedGroup.Visibility = Visibility.Visible;
            CompletedCount.Text = completedCount.ToString();
            CompletedCount.Visibility = completedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (interruptedCount > 0)
        {
            InterruptedGroup.Visibility = Visibility.Visible;
            InterruptedCount.Text = interruptedCount.ToString();
            InterruptedCount.Visibility = interruptedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Mascot icon reflects highest-priority status
        switch (overallStatus)
        {
            case Models.TerminalStatus.Working: ShowCollapsedIcon(IconProcessing); break;
            case Models.TerminalStatus.WaitingForInput: ShowCollapsedIcon(IconWaiting); break;
            case Models.TerminalStatus.TaskCompleted: ShowCollapsedIcon(IconSuccess); break;
            case Models.TerminalStatus.Interrupted: ShowCollapsedIcon(IconIdle); break;
            case Models.TerminalStatus.Idle:
                if (!_greetingActive) HideCollapsedIcon();
                break;
        }

        // Dynamic width/height based on visible groups
        if (!_isExpanded)
        {
            if (overallStatus != Models.TerminalStatus.Idle)
            {
                // Icon area (56px) + indicator groups
                Width = GetCollapsedBarWidth(workingCount, waitingCount, completedCount, interruptedCount);
                Height = CollapsedHeightWithIcon;
            }
            else if (!_greetingActive && !_iconVisible)
            {
                Width = CollapsedWidth;
                Height = CollapsedHeight;
            }
            PositionCenter();
        }
    }

    private double GetCollapsedBarWidth(int workingCount, int waitingCount, int completedCount, int interruptedCount)
    {
        // 56 = mascot icon (24px) + icon margin (6px) + bar Padding (10px * 2) + extra (6px)
        // Per group: Canvas 14px wide (or 8px for Interrupted Ellipse) + Margin 1px each side
        //   without count badge: 16px (14+2) or 12px (8+4) for interrupted
        //   with count badge: +12px for TextBlock (~10px text + 2px margin)
        double width = 56;
        int visibleGroups = 0;
        if (workingCount > 0) { visibleGroups++; width += workingCount > 1 ? 28 : 16; }
        if (waitingCount > 0) { visibleGroups++; width += waitingCount > 1 ? 28 : 16; }
        if (completedCount > 0) { visibleGroups++; width += completedCount > 1 ? 28 : 16; }
        if (interruptedCount > 0) { visibleGroups++; width += interruptedCount > 1 ? 24 : 12; }
        if (visibleGroups > 1) width += (visibleGroups - 1) * 2;
        return Math.Max(width, CollapsedWidthWithIcon);
    }

    private void ShowCollapsedIcon(BitmapImage icon)
    {
        CollapsedIcon.Source = icon;
        if (!_iconVisible)
        {
            _iconVisible = true;
            CollapsedIcon.Opacity = 0;
            CollapsedIcon.Visibility = Visibility.Visible;

            if (!_isExpanded)
            {
                Height = CollapsedHeightWithIcon;
                // Width is set by UpdateCollapsedBar after all groups are configured
            }

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            fadeIn.Completed += (_, _) => CollapsedIcon.Opacity = 1;
            CollapsedIcon.BeginAnimation(OpacityProperty, fadeIn);
        }
    }

    private void HideCollapsedIcon()
    {
        if (!_iconVisible) return;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };
        fadeOut.Completed += (_, _) =>
        {
            CollapsedIcon.Opacity = 1;
            CollapsedIcon.Visibility = Visibility.Collapsed;
            _iconVisible = false;

            if (!_isExpanded)
            {
                Width = CollapsedWidth;
                Height = CollapsedHeight;
                PositionCenter();
            }
        };
        CollapsedIcon.BeginAnimation(OpacityProperty, fadeOut);
    }

    // --- UI event handlers ---

    // --- Notch animations ---

    private void StartSpinnerAnimation()
    {
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(800))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    private void StartPulseAnimation()
    {
        var pulse = new DoubleAnimation(0.7, 1.2, TimeSpan.FromMilliseconds(600))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase()
        };
        AlertPulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        AlertPulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private void StopAnimations()
    {
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        AlertPulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        AlertPulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

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
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                // Ctrl+S checkpoint disabled — Claude Code has built-in checkpoints
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
                // Folder: create a new terminal session in that directory and switch to it
                // Skip if a session with this path already exists
                var existing = SessionStore.Instance.Sessions
                    .FirstOrDefault(s => string.Equals(s.ProjectPath, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    SessionStore.Instance.SelectSession(existing.Id);
                }
                else
                {
                    var session = SessionStore.Instance.AddSession(Path.GetFileName(path), path, path);
                    SessionStore.Instance.SelectSession(session.Id);
                }
                if (!_isExpanded) ExpandPanel();
            }
            else if (File.Exists(path))
            {
                // File: paste the file path into the active terminal
                var activeId = SessionStore.Instance.ActiveSessionId;
                if (activeId.HasValue && TerminalManager.Instance.HasTerminal(activeId.Value))
                {
                    var quotedPath = path.Contains(' ') ? $"\"{path}\"" : path;
                    TerminalManager.Instance.WriteInput(activeId.Value, quotedPath);
                }
            }
        }
    }

    /// <summary>Original OnDeactivated from main branch — with WebView2 focus-steal protection and pin support.</summary>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (!_isExpanded || SessionStore.Instance.IsPinned || _isShowing)
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
            catch { }

            CollapsePanel();
        }, DispatcherPriority.ApplicationIdle);
    }
}
