using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shelly.Interop;
using Shelly.Models;
using Shelly.Services;

namespace Shelly.Views;

public partial class SessionTabBar : UserControl
{
    private static readonly string[] Hints =
    [
        "Ctrl+` to toggle panel",
        "Drop a folder to open it",
        "Right-click tab to rename",
        "Pin to keep panel open",
        "Ctrl+T opens a new session",
        "Ctrl+W closes current tab",
        "Drag the top bar to move",
        "Drop a file to paste its path",
        "Customize hotkey in Settings",
        "Notch can go top or bottom",
        "Auto-expands when Claude asks",
    ];

    private int _hintIndex;
    private System.Windows.Threading.DispatcherTimer? _hintTimer;
    private bool _hintVisible; // current phase: true = showing, false = hidden gap
    private bool _tabsOverflow; // true when tabs need scrolling

    private const double ScrollStep = 120;
    private const double HintShowSeconds = 5;
    private const double HintHideSeconds = 10;

    public SessionTabBar()
    {
        InitializeComponent();
        DataContext = SessionStore.Instance;

        _hintIndex = Random.Shared.Next(Hints.Length);

        _hintTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(12)
        };
        _hintTimer.Tick += HintTimer_Tick;
        if (AppSettings.LoadShowHints())
        {
            // Show first hint immediately (no fade — avoids layout flicker)
            HintText.Text = $"Tip: {Hints[_hintIndex]}";
            HintText.Opacity = 1;
            HintText.Visibility = Visibility.Visible;
            _hintVisible = true;
            _hintTimer.Interval = TimeSpan.FromSeconds(HintShowSeconds);
            _hintTimer.Start();
        }
        else
        {
            HintText.Visibility = Visibility.Collapsed;
        }

        SizeChanged += (_, _) => UpdateScrollButtons();
    }

    private void HintTimer_Tick(object? sender, EventArgs e)
    {
        if (_hintVisible)
        {
            // Was showing → fade out and enter hidden gap
            FadeOutHint(() =>
            {
                HintText.Visibility = Visibility.Collapsed;
                _hintVisible = false;
                _hintIndex = (_hintIndex + 1) % Hints.Length;
                _hintTimer!.Interval = TimeSpan.FromSeconds(HintHideSeconds);
            });
        }
        else
        {
            // Was hidden → show next hint (if tabs aren't overflowing)
            _hintVisible = true;
            _hintTimer!.Interval = TimeSpan.FromSeconds(HintShowSeconds);
            if (!_tabsOverflow)
                ShowHintNow();
        }
    }

    private void ShowHintNow()
    {
        // Don't show hint if tabs are using most of the available space —
        // showing the hint would steal layout space, causing tabs to overflow,
        // which hides the hint, freeing space, showing the hint again (oscillation).
        // Use ExtentWidth (total tab content) vs ActualWidth (bar width) with a margin
        // for the hint itself (~150px) to decide before affecting layout.
        if (TabScrollViewer != null)
        {
            var spareSpace = TabScrollViewer.ActualWidth - TabScrollViewer.ExtentWidth;
            if (spareSpace < 150)
                return;
        }

        HintText.Text = $"Tip: {Hints[_hintIndex]}";
        HintText.BeginAnimation(OpacityProperty, null);
        HintText.Opacity = 0;
        HintText.Visibility = Visibility.Visible;
        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        HintText.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void FadeOutHint(Action? onComplete = null)
    {
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) =>
        {
            HintText.BeginAnimation(OpacityProperty, null);
            HintText.Opacity = 0;
            onComplete?.Invoke();
        };
        HintText.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void HintText_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // No-op: visibility is managed by UpdateScrollButtons (_tabsOverflow)
        // and HintTimer_Tick. Changing Visibility here causes layout oscillation.
    }

    private void Tab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TerminalSession session)
        {
            SessionStore.Instance.SelectSession(session.Id);
        }
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        var session = SessionStore.Instance.AddSession();
        SessionStore.Instance.SelectSession(session.Id);
        // Scroll to the end so the new tab is visible
        Dispatcher.BeginInvoke(() => TabScrollViewer.ScrollToRightEnd(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select folder for new terminal session"
        };

        if (dialog.ShowDialog() == true)
        {
            var folderPath = dialog.FolderName;
            var folderName = System.IO.Path.GetFileName(folderPath) ?? folderPath;
            var session = SessionStore.Instance.AddSession(folderName, folderPath, folderPath);
            SessionStore.Instance.SelectSession(session.Id);

            // Re-expand the panel (folder dialog steals focus and triggers collapse)
            if (Window.GetWindow(this) is FloatingPanel panel)
                panel.ExpandPanel(pinOpen: true);
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        SessionStore.Instance.IsPinned = !SessionStore.Instance.IsPinned;
        if (sender is Button btn)
            btn.Foreground = SessionStore.Instance.IsPinned
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        BuildViewSection(menu);
        BuildSettingsSection(menu);
        BuildAppSection(menu);

        menu.PlacementTarget = sender as Button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void BuildViewSection(ContextMenu menu)
    {
        var collapseItem = new MenuItem { Header = "Collapse to bar" };
        collapseItem.Click += (_, _) =>
        {
            if (Window.GetWindow(this) is FloatingPanel panel)
                panel.CollapsePanel();
        };
        menu.Items.Add(collapseItem);

        var positionItem = new MenuItem
        {
            Header = SessionStore.Instance.NotchAtBottom ? "Move notch to top" : "Move notch to bottom"
        };
        positionItem.Click += (_, _) => SessionStore.Instance.NotchAtBottom = !SessionStore.Instance.NotchAtBottom;
        menu.Items.Add(positionItem);
    }

    private void BuildSettingsSection(ContextMenu menu)
    {
        var settingsMenu = new MenuItem { Header = "Settings" };

        // Default shell
        var shellMenu = new MenuItem { Header = "Default Shell" };
        foreach (var (label, path) in ConPtyTerminal.GetAvailableShells())
        {
            var shellPath = path;
            var item = new MenuItem
            {
                Header = label,
                IsChecked = string.Equals(ConPtyTerminal.DefaultShell, shellPath, System.StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => { ConPtyTerminal.DefaultShell = shellPath; AppSettings.SaveDefaultShell(shellPath); };
            shellMenu.Items.Add(item);
        }
        settingsMenu.Items.Add(shellMenu);

        // Text size
        var textSizeMenu = new MenuItem { Header = "Text Size" };
        var currentFontSize = AppSettings.LoadFontSize();
        foreach (var (label, size) in new[] { ("Small", 9), ("Medium", 11), ("Large", 14), ("Extra Large", 18) })
        {
            var s = size;
            var item = new MenuItem { Header = label, IsChecked = currentFontSize == s };
            item.Click += (_, _) =>
            {
                AppSettings.SaveFontSize(s);
                if (Window.GetWindow(this) is FloatingPanel panel)
                    panel.TerminalHost.ApplyFontSize(s);
            };
            textSizeMenu.Items.Add(item);
        }
        settingsMenu.Items.Add(textSizeMenu);

        // Keybinding
        var hkMgr = (Application.Current as App)?.HotkeyManager;
        var currentBinding = hkMgr?.CustomVk != null
            ? HotkeyManager.FormatHotkey(hkMgr.CustomModifiers!.Value, hkMgr.CustomVk!.Value)
            : null;
        var keybindItem = new MenuItem
        {
            Header = currentBinding != null ? $"Keybinding: {currentBinding}" : "Set custom keybinding"
        };
        keybindItem.Click += (_, _) => { if (hkMgr != null) KeybindingDialog.Show(hkMgr); };
        settingsMenu.Items.Add(keybindItem);

        if (currentBinding != null)
        {
            var clearItem = new MenuItem { Header = "Remove custom keybinding" };
            clearItem.Click += (_, _) => hkMgr?.ClearCustomHotkey();
            settingsMenu.Items.Add(clearItem);
        }

        // Toggles
        AddToggle(settingsMenu, "Auto-launch Claude", AppSettings.LoadAutoLaunchClaude,
            v => AppSettings.SaveAutoLaunchClaude(v));
        AddToggle(settingsMenu, "Show hints", AppSettings.LoadShowHints, v =>
        {
            AppSettings.SaveShowHints(v);
            if (v)
            {
                _hintVisible = true;
                _hintTimer!.Interval = TimeSpan.FromSeconds(HintShowSeconds);
                if (!_tabsOverflow) ShowHintNow();
                _hintTimer.Start();
            }
            else
            {
                _hintTimer?.Stop();
                FadeOutHint(() => HintText.Visibility = Visibility.Collapsed);
            }
        });
        AddToggle(settingsMenu, "Sound", AppSettings.LoadSoundEnabled,
            v => AppSettings.SaveSoundEnabled(v));

        var autoStartItem = new MenuItem { Header = "Start with Windows", IsChecked = AutoStartManager.IsEnabled };
        autoStartItem.Click += (_, _) => AutoStartManager.Toggle();
        settingsMenu.Items.Add(autoStartItem);

        menu.Items.Add(settingsMenu);
    }

    private static void AddToggle(MenuItem parent, string header, Func<bool> load, Action<bool> save)
    {
        var item = new MenuItem { Header = header, IsChecked = load() };
        item.Click += (_, _) => save(!load());
        parent.Items.Add(item);
    }

    private void BuildAppSection(ContextMenu menu)
    {
        var updateItem = new MenuItem { Header = "Check for updates" };
        updateItem.Click += (_, _) => UpdateFlowController.RunInMenu(updateItem);
        menu.Items.Add(updateItem);

        var quitItem = new MenuItem { Header = "Quit Shelly" };
        quitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(quitItem);
    }

    private void RenameTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TerminalSession session)
        {
            var dialog = new Window
            {
                Title = "Rename Session",
                Width = 300, Height = 120,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };
            var panel = new StackPanel { Margin = new Thickness(10) };
            var textBox = new TextBox { Text = session.ProjectName, Margin = new Thickness(0, 0, 0, 10) };
            var okBtn = new Button { Content = "OK", Width = 60, HorizontalAlignment = HorizontalAlignment.Right };
            okBtn.Click += (_, _) => { session.ProjectName = textBox.Text; dialog.Close(); };
            panel.Children.Add(textBox);
            panel.Children.Add(okBtn);
            dialog.Content = panel;
            dialog.ShowDialog();
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TerminalSession session)
        {
            SessionStore.Instance.RemoveSession(session.Id);
        }
    }

    private void CloseTabInline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TerminalSession session)
        {
            if (SessionStore.Instance.Sessions.Count > 1)
                SessionStore.Instance.RemoveSession(session.Id);
        }
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset - ScrollStep);
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset + ScrollStep);
    }

    private void TabScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void TabScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateScrollButtons();
    }

    private void UpdateScrollButtons()
    {
        if (TabScrollViewer == null) return;

        var canScroll = TabScrollViewer.ScrollableWidth > 0;
        ScrollLeftBtn.Visibility = canScroll && TabScrollViewer.HorizontalOffset > 0
            ? Visibility.Visible : Visibility.Collapsed;
        ScrollRightBtn.Visibility = canScroll && TabScrollViewer.HorizontalOffset < TabScrollViewer.ScrollableWidth
            ? Visibility.Visible : Visibility.Collapsed;

        // Tabs overflow → always hide hint; tabs fit with room to spare → show if in visible phase
        var wasOverflow = _tabsOverflow;
        _tabsOverflow = canScroll;

        if (!AppSettings.LoadShowHints()) return;

        if (_tabsOverflow && HintText.Visibility == Visibility.Visible)
        {
            // Immediately fade out — tabs need the space
            FadeOutHint(() => HintText.Visibility = Visibility.Collapsed);
        }
        else if (!_tabsOverflow && wasOverflow && _hintVisible)
        {
            // Tabs stopped overflowing — only bring hint back if there's enough spare space
            // (ShowHintNow checks this internally to avoid oscillation)
            ShowHintNow();
        }
    }

}
