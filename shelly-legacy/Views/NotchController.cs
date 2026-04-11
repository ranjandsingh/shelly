using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shelly.Models;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>
/// Manages the collapsed notch bar: status indicators, mascot icon,
/// greeting animation, and dynamic sizing. Extracted from FloatingPanel.
/// </summary>
public class NotchController
{
    private readonly FloatingPanel _panel;
    private bool _iconVisible;
    private bool _greetingActive;
    private DispatcherTimer? _hideIconTimer;
    private DispatcherTimer? _greetingTimer;

    public const double CollapsedWidth = 48;
    public const double CollapsedWidthWithIcon = 84;
    public const double CollapsedHeight = 18;
    public const double CollapsedHeightWithIcon = 36;

    private const double CollapsedWidthGreeting = 124;
    private const double CollapsedHeightGreeting = 48;

    public bool IsGreetingActive => _greetingActive;
    public bool IsIconVisible => _iconVisible;

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
        img.DecodePixelWidth = 40;
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private static readonly string[] Greetings =
    [
        "Hello!", "Hey!", "Namaste!", "Hola!", "Bonjour!",
        "Ciao!", "Hallo!", "Olá!", "Ahoj!", "Salut!",
        "Hej!", "Aloha!", "Salam!", "Sawubona!", "Merhaba!",
    ];

    public NotchController(FloatingPanel panel)
    {
        _panel = panel;
    }

    public void PlayLaunchGreeting()
    {
        _greetingActive = true;

        _panel.CollapsedGreeting.Text = Greetings[Random.Shared.Next(Greetings.Length)];

        _panel.CollapsedIcon.Width = 28;
        _panel.CollapsedIcon.Height = 28;
        _panel.CollapsedBar.CornerRadius = new CornerRadius(16);
        _panel.CollapsedIcon.Source = IconIdle;
        _panel.CollapsedIcon.Opacity = 0;
        _panel.CollapsedIcon.Visibility = Visibility.Visible;
        _panel.CollapsedGreeting.Opacity = 0;
        _panel.CollapsedGreeting.Visibility = Visibility.Visible;
        _iconVisible = true;

        _panel.Width = CollapsedWidthGreeting;
        _panel.Height = CollapsedHeightGreeting;
        _panel.PositionCenter();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        fadeIn.Completed += (_, _) => { _panel.CollapsedIcon.Opacity = 1; _panel.CollapsedGreeting.Opacity = 1; };
        _panel.CollapsedIcon.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        _panel.CollapsedGreeting.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        _greetingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _greetingTimer.Tick += (_, _) =>
        {
            _greetingTimer!.Stop();
            _greetingActive = false;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop
            };
            fadeOut.Completed += (_, _) =>
            {
                _panel.CollapsedIcon.Opacity = 1;
                _panel.CollapsedIcon.Visibility = Visibility.Collapsed;
                _panel.CollapsedIcon.Width = 24;
                _panel.CollapsedIcon.Height = 24;
                _panel.CollapsedGreeting.Opacity = 1;
                _panel.CollapsedGreeting.Visibility = Visibility.Collapsed;
                _panel.CollapsedBar.CornerRadius = new CornerRadius(8);
                _iconVisible = false;

                var anyActive = SessionStore.Instance.Sessions.Any(s => s.Status != TerminalStatus.Idle);
                if (anyActive)
                    Update();
                else
                {
                    _panel.Width = CollapsedWidth;
                    _panel.Height = CollapsedHeight;
                    _panel.PositionCenter();
                }
            };
            _panel.CollapsedIcon.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            _panel.CollapsedGreeting.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        };
        _greetingTimer.Start();
    }

    /// <summary>Queue an update on the dispatcher (safe from background threads).</summary>
    public void Update()
    {
        _panel.Dispatcher.InvokeAsync(UpdateSync);
    }

    /// <summary>Synchronously update indicators, mascot icon, and size. Must be on UI thread.</summary>
    public void UpdateSync()
    {
        var sessions = SessionStore.Instance.Sessions;

        int workingCount = 0, waitingCount = 0, completedCount = 0, interruptedCount = 0;
        foreach (var s in sessions)
        {
            switch (s.Status)
            {
                case TerminalStatus.Working: workingCount++; break;
                case TerminalStatus.WaitingForInput: waitingCount++; break;
                case TerminalStatus.TaskCompleted: completedCount++; break;
                case TerminalStatus.Interrupted: interruptedCount++; break;
            }
        }

        var overallStatus = waitingCount > 0 ? TerminalStatus.WaitingForInput
            : workingCount > 0 ? TerminalStatus.Working
            : completedCount > 0 ? TerminalStatus.TaskCompleted
            : interruptedCount > 0 ? TerminalStatus.Interrupted
            : TerminalStatus.Idle;

        // Hide all groups and stop animations
        _panel.WorkingGroup.Visibility = Visibility.Collapsed;
        _panel.WaitingGroup.Visibility = Visibility.Collapsed;
        _panel.CompletedGroup.Visibility = Visibility.Collapsed;
        _panel.InterruptedGroup.Visibility = Visibility.Collapsed;
        StopAnimations();

        if (workingCount > 0)
        {
            _panel.WorkingGroup.Visibility = Visibility.Visible;
            _panel.WorkingCount.Text = workingCount.ToString();
            _panel.WorkingCount.Visibility = workingCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            StartSpinnerAnimation();
        }
        if (waitingCount > 0)
        {
            _panel.WaitingGroup.Visibility = Visibility.Visible;
            _panel.WaitingCount.Text = waitingCount.ToString();
            _panel.WaitingCount.Visibility = waitingCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            StartPulseAnimation();
        }
        if (completedCount > 0)
        {
            _panel.CompletedGroup.Visibility = Visibility.Visible;
            _panel.CompletedCount.Text = completedCount.ToString();
            _panel.CompletedCount.Visibility = completedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (interruptedCount > 0)
        {
            _panel.InterruptedGroup.Visibility = Visibility.Visible;
            _panel.InterruptedCount.Text = interruptedCount.ToString();
            _panel.InterruptedCount.Visibility = interruptedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Mascot icon
        switch (overallStatus)
        {
            case TerminalStatus.Working:
                _hideIconTimer?.Stop();
                ShowIcon(IconProcessing);
                break;
            case TerminalStatus.WaitingForInput:
                _hideIconTimer?.Stop();
                ShowIcon(IconWaiting);
                break;
            case TerminalStatus.TaskCompleted:
                _hideIconTimer?.Stop();
                ShowIcon(IconSuccess);
                break;
            case TerminalStatus.Interrupted:
                _hideIconTimer?.Stop();
                ShowIcon(IconIdle);
                break;
            case TerminalStatus.Idle:
                if (!_greetingActive && _iconVisible)
                {
                    if (_hideIconTimer == null)
                    {
                        _hideIconTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                        _hideIconTimer.Tick += (_, _) =>
                        {
                            _hideIconTimer!.Stop();
                            var stillIdle = !SessionStore.Instance.Sessions.Any(s => s.Status != TerminalStatus.Idle);
                            if (stillIdle && !_greetingActive)
                                HideIcon();
                        };
                    }
                    _hideIconTimer.Stop();
                    _hideIconTimer.Start();
                }
                break;
        }

        // Dynamic size
        if (!_panel.IsExpanded)
        {
            if (overallStatus != TerminalStatus.Idle)
            {
                _panel.Width = GetBarWidth(workingCount, waitingCount, completedCount, interruptedCount);
                _panel.Height = CollapsedHeightWithIcon;
            }
            else if (!_greetingActive && !_iconVisible)
            {
                _panel.Width = CollapsedWidth;
                _panel.Height = CollapsedHeight;
            }
            _panel.PositionCenter();
        }
    }

    private double GetBarWidth(int workingCount, int waitingCount, int completedCount, int interruptedCount)
    {
        double width = 56;
        int visibleGroups = 0;
        if (workingCount > 0) { visibleGroups++; width += workingCount > 1 ? 28 : 16; }
        if (waitingCount > 0) { visibleGroups++; width += waitingCount > 1 ? 28 : 16; }
        if (completedCount > 0) { visibleGroups++; width += completedCount > 1 ? 28 : 16; }
        if (interruptedCount > 0) { visibleGroups++; width += interruptedCount > 1 ? 24 : 12; }
        if (visibleGroups > 1) width += (visibleGroups - 1) * 2;
        return Math.Max(width, CollapsedWidthWithIcon);
    }

    private void ShowIcon(BitmapImage icon)
    {
        _panel.CollapsedIcon.Source = icon;
        if (!_iconVisible)
        {
            _iconVisible = true;
            _panel.CollapsedIcon.Opacity = 0;
            _panel.CollapsedIcon.Visibility = Visibility.Visible;

            if (!_panel.IsExpanded)
                _panel.Height = CollapsedHeightWithIcon;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            fadeIn.Completed += (_, _) => _panel.CollapsedIcon.Opacity = 1;
            _panel.CollapsedIcon.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
    }

    private void HideIcon()
    {
        if (!_iconVisible) return;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };
        fadeOut.Completed += (_, _) =>
        {
            _panel.CollapsedIcon.Opacity = 1;
            _panel.CollapsedIcon.Visibility = Visibility.Collapsed;
            _iconVisible = false;

            if (!_panel.IsExpanded)
            {
                _panel.Width = CollapsedWidth;
                _panel.Height = CollapsedHeight;
                _panel.PositionCenter();
            }
        };
        _panel.CollapsedIcon.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void StartSpinnerAnimation()
    {
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(800)) { RepeatBehavior = RepeatBehavior.Forever };
        _panel.SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    private void StartPulseAnimation()
    {
        var pulse = new DoubleAnimation(0.7, 1.2, TimeSpan.FromMilliseconds(600))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase()
        };
        _panel.AlertPulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        _panel.AlertPulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private void StopAnimations()
    {
        _panel.SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _panel.AlertPulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _panel.AlertPulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }
}
